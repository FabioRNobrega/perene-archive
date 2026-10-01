using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Identity;
using WebApp.Models;

namespace WebApp.Authorization;

/// <summary>Builds the access editor's view of one member (folder tree, explicit and effective cells, provenance, stamps) and previews the impact of pending edits.</summary>
internal sealed class FolderAccessEditorService(
    AppDbContext db,
    FolderAccessService access,
    FolderCatalog catalog,
    FolderLocator locator,
    UserManager<ApplicationUser> users)
{
    private static readonly FolderOperation[] Cells =
        [FolderOperation.Read, FolderOperation.Write, FolderOperation.Create, FolderOperation.Delete, FolderOperation.Manage];

    public async Task<(FolderAccessDto? Result, PermissionWriteResult? Failure)> GetAsync(string actorUserId, string targetUserName, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(actorUserId, cancellationToken);
        if (actor is null) return (null, PermissionWriteResult.Fail(PermissionWriteOutcome.Forbidden, "You cannot view folder access."));
        var target = await users.FindByNameAsync(targetUserName);
        if (target is null) return (null, PermissionWriteResult.Fail(PermissionWriteOutcome.NotFound, "That account no longer exists."));
        if (await users.IsInRoleAsync(target, AccountLifecycleService.AdminRole))
            return (null, PermissionWriteResult.Fail(PermissionWriteOutcome.Invalid, "Administrators already have full access."));

        await catalog.SyncTreeAsync(cancellationToken);
        var tree = await access.GetTreeAsync(cancellationToken);
        var rows = await db.FolderPermissions.AsNoTracking().Where(permission => permission.UserId == target.Id).ToListAsync(cancellationToken);
        var rowsByFolder = rows.ToDictionary(row => row.FolderId);
        var grants = rowsByFolder.ToDictionary(pair => pair.Key, pair => PermissionGrant.From(pair.Value));
        var byId = tree.All.ToDictionary(folder => folder.Id);

        var ordered = OrderedFolders(tree).ToList();
        var folders = new List<FolderAccessFolderDto>(ordered.Count);
        foreach (var folder in ordered)
        {
            var chain = tree.Chain(folder.Location);
            var nodes = chain.Select(item => item.Node).ToList();
            var parent = chain.Count > 1 ? chain[^2] : null;
            rowsByFolder.TryGetValue(folder.Id, out var row);

            FolderAccessCellDto Cell(FolderOperation operation)
            {
                var (allowed, provenance, sourceId) = FolderPermissionResolver.Explain(operation, target.Id, nodes, grants, tree.Defaults);
                var effective = FolderPermissionResolver.Resolve(operation, target.Id, false, nodes, grants, tree.Defaults);
                var label = sourceId is { } id && id != folder.Id && byId.TryGetValue(id, out var source) ? $"{provenance} from {source.Label}" : provenance;
                var locked = (!actor.IsAdmin && row is not null && (row.LockMask & operation) != 0)
                    || chain.Any(ancestor => ancestor.Id != folder.Id && grants.TryGetValue(ancestor.Id, out var enforcing)
                        && enforcing.Enforced && enforcing.Get(operation) == PermissionState.Deny);
                return new FolderAccessCellDto(
                    row is null ? AccessCellState.Unset : (AccessCellState)(int)row.Get(operation), effective, label, locked);
            }

            folders.Add(new FolderAccessFolderDto(
                FolderKeys.Of(folder),
                parent is null ? null : FolderKeys.Of(parent),
                folder.Label,
                chain.Count - 1,
                folder.Mode == FolderAccessMode.Private,
                folder.OwnerUserId == target.Id,
                row?.ConcurrencyStamp,
                Cell(FolderOperation.Read), Cell(FolderOperation.Write), Cell(FolderOperation.Create), Cell(FolderOperation.Delete),
                actor.IsAdmin ? Cell(FolderOperation.Manage) : null));
        }

        return (new FolderAccessDto(target.UserName!, target.DisplayName, actor.IsAdmin, folders), null);
    }

    /// <summary>Counts and labels of what the pending changes would hide, and who would lose it. Nothing is written.</summary>
    public async Task<(FolderAccessImpactDto? Result, PermissionWriteResult? Failure)> PreviewAsync(
        string actorUserId, string targetUserName, IReadOnlyList<FolderAccessChangeDto> changes, CancellationToken cancellationToken = default)
    {
        var actor = await access.GetActorAsync(actorUserId, cancellationToken);
        if (actor is null) return (null, PermissionWriteResult.Fail(PermissionWriteOutcome.Forbidden, "You cannot view folder access."));
        var target = await users.FindByNameAsync(targetUserName);
        if (target is null) return (null, PermissionWriteResult.Fail(PermissionWriteOutcome.NotFound, "That account no longer exists."));

        var tree = await access.GetTreeAsync(cancellationToken);
        var byKey = tree.All.ToDictionary(FolderKeys.Of, StringComparer.Ordinal);
        var modeOverrides = new Dictionary<long, FolderAccessMode>();
        var targetEdits = new Dictionary<long, Dictionary<FolderOperation, PermissionState>>();
        var ownerConflicts = new List<string>();
        foreach (var change in changes)
        {
            if (!byKey.TryGetValue(change.FolderKey, out var folder)) continue;
            if (change.IsPrivate is { } isPrivate) modeOverrides[folder.Id] = isPrivate ? FolderAccessMode.Private : FolderAccessMode.Shared;
            var edits = new Dictionary<FolderOperation, PermissionState>();
            void Add(FolderOperation operation, AccessCellState? state)
            {
                if (state is { } value) edits[operation] = (PermissionState)(int)value;
            }

            Add(FolderOperation.Read, change.Read);
            Add(FolderOperation.Write, change.Write);
            Add(FolderOperation.Create, change.Create);
            Add(FolderOperation.Delete, change.Delete);
            if (change.ClearRow) foreach (var operation in Cells) edits[operation] = PermissionState.Unset;
            targetEdits[folder.Id] = edits;
            if (folder.OwnerUserId == target.Id && edits.Values.Any(state => state == PermissionState.Deny)) ownerConflicts.Add(folder.Label);
        }

        var members = await db.Users.AsNoTracking().Where(user => user.IsActive).ToListAsync(cancellationToken);
        var allRows = await db.FolderPermissions.AsNoTracking().ToListAsync(cancellationToken);
        var adminIds = (await users.GetUsersInRoleAsync(AccountLifecycleService.AdminRole)).Select(user => user.Id).ToHashSet();

        var lostFolders = new HashSet<long>();
        var affectedUsers = new List<string>();
        foreach (var member in members.Where(member => !adminIds.Contains(member.Id)))
        {
            var memberRows = allRows.Where(row => row.UserId == member.Id).ToDictionary(row => row.FolderId, PermissionGrant.From);
            var before = ReadableIds(tree, member.Id, memberRows, new Dictionary<long, FolderAccessMode>());
            var afterGrants = new Dictionary<long, PermissionGrant>(memberRows);
            if (member.Id == target.Id)
            {
                foreach (var (folderId, edits) in targetEdits)
                {
                    var current = afterGrants.GetValueOrDefault(folderId) ?? new PermissionGrant(0, 0, 0, 0, 0, false);
                    afterGrants[folderId] = current with
                    {
                        Read = edits.GetValueOrDefault(FolderOperation.Read, current.Read),
                        Write = edits.GetValueOrDefault(FolderOperation.Write, current.Write),
                        Create = edits.GetValueOrDefault(FolderOperation.Create, current.Create),
                        Delete = edits.GetValueOrDefault(FolderOperation.Delete, current.Delete)
                    };
                }
            }

            var after = ReadableIds(tree, member.Id, afterGrants, modeOverrides);
            var lost = before.Except(after).ToList();
            if (lost.Count == 0) continue;
            affectedUsers.Add(member.UserName!);
            lostFolders.UnionWith(lost);
        }

        var lostRecords = lostFolders.Select(tree.Find).OfType<FolderRecord>().ToList();
        var items = 0;
        foreach (var folder in lostRecords)
        {
            var physical = locator.PhysicalPath(folder.Location);
            if (physical is null || !Directory.Exists(physical)) continue;
            try { items += Directory.EnumerateFiles(physical).Count(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        return (new FolderAccessImpactDto(
            lostRecords.Count, items, lostRecords.Select(folder => folder.Label).Distinct().Take(20).ToList(),
            affectedUsers, ownerConflicts.Distinct().ToList()), null);
    }

    private static HashSet<long> ReadableIds(
        FolderTreeSnapshot tree, string userId, IReadOnlyDictionary<long, PermissionGrant> grants, IReadOnlyDictionary<long, FolderAccessMode> modeOverrides)
    {
        var readable = new HashSet<long>();
        foreach (var folder in tree.All)
        {
            var chain = tree.Chain(folder.Location)
                .Select(item => modeOverrides.TryGetValue(item.Id, out var mode) ? item.Node with { Mode = mode } : item.Node).ToList();
            if (FolderPermissionResolver.Resolve(FolderOperation.Read, userId, false, chain, grants, tree.Defaults)) readable.Add(folder.Id);
        }

        return readable;
    }

    private IEnumerable<FolderRecord> OrderedFolders(FolderTreeSnapshot tree)
    {
        var rootOrder = ArchiveCategory.Defaults.Select(category => category.Key)
            .Concat([FolderLocator.CutRootKey, FolderLocator.CompositionRootKey]).ToList();
        return tree.All
            .Where(folder => rootOrder.Contains(folder.RootKey))
            .OrderBy(folder => rootOrder.IndexOf(folder.RootKey))
            .ThenBy(folder => folder.RelativePath, FolderPathComparer.Instance);
    }

    /// <summary>Orders paths so a parent precedes its children and siblings sort by name.</summary>
    private sealed class FolderPathComparer : IComparer<string>
    {
        public static readonly FolderPathComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var left = (x ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
            var right = (y ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
            {
                var result = string.Compare(left[index], right[index], StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;
            }

            return left.Length.CompareTo(right.Length);
        }
    }
}
