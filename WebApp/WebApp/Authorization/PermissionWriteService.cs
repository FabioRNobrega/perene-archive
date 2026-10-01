using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Identity;

namespace WebApp.Authorization;

public enum PermissionWriteOutcome { Ok, NotFound, Invalid, Forbidden, Conflict }

public sealed record PermissionWriteResult(PermissionWriteOutcome Outcome, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Outcome == PermissionWriteOutcome.Ok;
    public static readonly PermissionWriteResult Ok = new(PermissionWriteOutcome.Ok, []);
    public static PermissionWriteResult Fail(PermissionWriteOutcome outcome, params string[] errors) => new(outcome, errors);
}

/// <summary>
/// Owns permission writes: delegation rules, optimistic concurrency (a stale stamp is a conflict; a racing save gets one reload and
/// retry), one transaction per save that also bumps the global policy version, and secret-free audit events.
/// </summary>
internal sealed class PermissionWriteService(
    AppDbContext db,
    FolderAccessService access,
    AuthzVersionStore versions,
    UserManager<ApplicationUser> users)
{
    private const int MaxChanges = 500;

    public async Task<PermissionWriteResult> ApplyAsync(
        string actorUserId, string targetUserName, IReadOnlyList<FolderAccessChangeDto> changes, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(actorUserId, cancellationToken);
        if (actor is null) return PermissionWriteResult.Fail(PermissionWriteOutcome.Forbidden, "You cannot change folder access.");
        var target = await users.FindByNameAsync(targetUserName);
        if (target is null) return PermissionWriteResult.Fail(PermissionWriteOutcome.NotFound, "That account no longer exists.");
        if (await users.IsInRoleAsync(target, AccountLifecycleService.AdminRole))
            return PermissionWriteResult.Fail(PermissionWriteOutcome.Invalid, "Administrators already have full access.");
        if (changes.Count == 0) return PermissionWriteResult.Ok;
        if (changes.Count > MaxChanges || changes.Select(change => change.FolderKey).Distinct(StringComparer.Ordinal).Count() != changes.Count)
            return PermissionWriteResult.Fail(PermissionWriteOutcome.Invalid, "The change set is not valid.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(actor, target, changes, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt == 0)
            {
                // One reload: drop everything we tracked and evaluate the same request against fresh rows.
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return PermissionWriteResult.Fail(PermissionWriteOutcome.Conflict, "Folder access changed while saving. Reload and try again.");
            }
        }
    }

    private async Task<PermissionWriteResult> ApplyOnceAsync(
        AccessActor actor, ApplicationUser target, IReadOnlyList<FolderAccessChangeDto> changes, CancellationToken cancellationToken)
    {
        var tree = await access.GetTreeAsync(cancellationToken);
        var byKey = tree.All.ToDictionary(FolderKeys.Of, StringComparer.Ordinal);
        var rejected = new List<string>();
        var staleFolders = new List<string>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var audits = new List<AuditEvent>();
        foreach (var change in changes)
        {
            if (!byKey.TryGetValue(change.FolderKey, out var record))
                return PermissionWriteResult.Fail(PermissionWriteOutcome.Invalid, "A folder in the change set no longer exists.");

            var cells = new Dictionary<FolderOperation, PermissionState>();
            void Collect(FolderOperation operation, AccessCellState? state)
            {
                if (state is { } value) cells[operation] = (PermissionState)(int)value;
            }

            Collect(FolderOperation.Read, change.Read);
            Collect(FolderOperation.Write, change.Write);
            Collect(FolderOperation.Create, change.Create);
            Collect(FolderOperation.Delete, change.Delete);
            Collect(FolderOperation.Manage, change.Manage);

            var row = await db.FolderPermissions.FirstOrDefaultAsync(
                permission => permission.FolderId == record.Id && permission.UserId == target.Id, cancellationToken);
            if (row is null ? !string.IsNullOrEmpty(change.ConcurrencyStamp) : row.ConcurrencyStamp != change.ConcurrencyStamp)
            {
                staleFolders.Add(record.Label);
                continue;
            }

            var changesMode = change.IsPrivate is { } makePrivate && makePrivate != (record.Mode == FolderAccessMode.Private);
            var changesEnforcement = change.Enforced is { } enforced && enforced != (row?.Enforced ?? false);
            var changesLock = change.LockMask is { } mask && (FolderOperation)mask != (row?.LockMask ?? FolderOperation.None);

            var manages = actor.IsAdmin || await access.CheckAsync(actor.UserId, FolderOperation.Manage, record.Location, cancellationToken);
            var held = new Dictionary<FolderOperation, bool>();
            if (!actor.IsAdmin)
            {
                foreach (var operation in cells.Where(cell => cell.Value == PermissionState.Allow).Select(cell => cell.Key))
                    held[operation] = await access.CheckAsync(actor.UserId, operation, record.Location, cancellationToken);
            }

            var violation = DelegationRules.Validate(
                actor.IsAdmin, actor.UserId == target.Id, manages, operation => held.GetValueOrDefault(operation),
                row, cells, changesMode, changesEnforcement, changesLock, change.ClearRow);
            if (violation is not null)
            {
                rejected.Add($"{record.Label}: {violation}");
                continue;
            }

            if (changesMode)
            {
                var folder = await db.Folders.FirstAsync(item => item.Id == record.Id, cancellationToken);
                folder.AccessMode = change.IsPrivate == true ? FolderAccessMode.Private : FolderAccessMode.Shared;
                audits.Add(Audit("folder.mode-changed", actor, target, $"folder={record.Label}; mode={folder.AccessMode}"));
            }

            if (change.ClearRow)
            {
                if (row is not null)
                {
                    db.FolderPermissions.Remove(row);
                    audits.Add(Audit("permission.cleared", actor, target, $"folder={record.Label}"));
                }

                continue;
            }

            if (cells.Count == 0 && !changesEnforcement && !changesLock) continue;

            var isNew = row is null;
            row ??= new FolderPermission { FolderId = record.Id, UserId = target.Id };
            foreach (var (operation, state) in cells) row.Set(operation, state);
            if (change.Enforced is { } enforcedValue) row.Enforced = enforcedValue;
            if (change.LockMask is { } lockValue) row.LockMask = (FolderOperation)lockValue;
            row.GrantedByUserId = actor.UserId;
            row.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            if (isNew) db.FolderPermissions.Add(row);

            if (row.IsEmpty)
            {
                db.FolderPermissions.Remove(row);
                audits.Add(Audit("permission.cleared", actor, target, $"folder={record.Label}"));
                continue;
            }

            var detail = string.Join("; ", cells.Select(cell => $"{cell.Key.ToString().ToLowerInvariant()}={cell.Value}").Prepend($"folder={record.Label}"));
            audits.Add(Audit(isNew ? "permission.granted" : "permission.changed", actor, target, detail));
        }

        if (staleFolders.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return PermissionWriteResult.Fail(PermissionWriteOutcome.Conflict, $"Access changed since you loaded it: {string.Join(", ", staleFolders.Distinct())}.");
        }

        if (rejected.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new PermissionWriteResult(PermissionWriteOutcome.Forbidden, rejected);
        }

        db.AuditEvents.AddRange(audits);
        await db.SaveChangesAsync(cancellationToken);
        if (audits.Count > 0) await versions.BumpGlobalAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PermissionWriteResult.Ok;
    }

    private static AuditEvent Audit(string action, AccessActor actor, ApplicationUser target, string detail) => new()
    {
        OccurredUtc = DateTimeOffset.UtcNow,
        Action = action,
        ActorUserId = actor.UserId,
        TargetUserId = target.Id,
        TargetUserName = target.UserName,
        Detail = detail.Length > 512 ? detail[..512] : detail
    };
}
