using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Identity;

namespace WebApp.Authorization;

public sealed record AccessActor(string UserId, bool IsAdmin, long AuthzVersion);

/// <summary>
/// Owns access-decision freshness. The readable-folder set is cached per user and stamped with the pair
/// (global policy version, user <see cref="ApplicationUser.AuthzVersion"/>); destructive, rename/move/replace, permission,
/// Admin, and job-execution checks always re-evaluate against the user's rows in the database.
/// </summary>
public sealed class FolderAccessService(AppDbContext db, AccessCaches caches)
{
    /// <summary>The active account with its Admin membership read from the database, or null for an unknown/deactivated account.</summary>
    public async Task<AccessActor?> GetActorAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        var row = await db.Users.AsNoTracking().Where(user => user.Id == userId).Select(user => new
        {
            user.IsActive,
            user.AuthzVersion,
            IsAdmin = (from userRole in db.UserRoles
                       join role in db.Roles on userRole.RoleId equals role.Id
                       where userRole.UserId == user.Id && role.Name == AccountLifecycleService.AdminRole
                       select userRole.UserId).Any()
        }).FirstOrDefaultAsync(cancellationToken);
        return row is { IsActive: true } ? new AccessActor(userId, row.IsAdmin, row.AuthzVersion) : null;
    }

    public async Task<FolderTreeSnapshot> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        var policy = await db.AccessPolicies.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var version = policy?.Version ?? 0;
        if (caches.Tree is { } cached && cached.Version == version) return cached;

        var folders = await db.Folders.AsNoTracking().Where(folder => folder.Status == FolderStatus.Active)
            .Select(folder => new FolderRecord(folder.Id, folder.RootKey, folder.RelativePath, folder.Label, folder.ParentId, folder.AccessMode, folder.OwnerUserId))
            .ToListAsync(cancellationToken);
        var defaults = policy is null
            ? PolicyDefaults.Shared
            : new PolicyDefaults(policy.DefaultRead, policy.DefaultWrite, policy.DefaultCreate, policy.DefaultDelete);
        var snapshot = new FolderTreeSnapshot(version, defaults, folders);
        caches.Tree = snapshot;
        return snapshot;
    }

    public async Task<IReadOnlyDictionary<long, PermissionGrant>> LoadGrantsAsync(string userId, CancellationToken cancellationToken = default) =>
        (await db.FolderPermissions.AsNoTracking().Where(permission => permission.UserId == userId).ToListAsync(cancellationToken))
        .ToDictionary(permission => permission.FolderId, PermissionGrant.From);

    /// <summary>A fresh decision for one operation on one folder; never uses the readable-set cache.</summary>
    public async Task<bool> CheckAsync(string? userId, FolderOperation operation, FolderLocation location, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(userId, cancellationToken);
        if (actor is null) return false;
        if (actor.IsAdmin) return true;

        var tree = await GetTreeAsync(cancellationToken);
        var chain = tree.Chain(location);
        var grants = await LoadGrantsAsync(actor.UserId, cancellationToken);
        return FolderPermissionResolver.Resolve(operation, actor.UserId, false, chain.Select(folder => folder.Node).ToList(), grants, tree.Defaults);
    }

    /// <summary>The cached readable set for a user; null for an unknown or deactivated account.</summary>
    public async Task<ReadableFolders?> GetReadableAsync(string? userId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(userId, cancellationToken);
        if (actor is null) return null;

        var tree = await GetTreeAsync(cancellationToken);
        if (caches.Readable.TryGetValue(actor.UserId, out var cached) && cached.Policy == tree.Version && cached.User == actor.AuthzVersion)
            return cached.Readable;

        HashSet<long> readable = [];
        HashSet<long> passThrough = [];
        if (!actor.IsAdmin)
        {
            var grants = await LoadGrantsAsync(actor.UserId, cancellationToken);
            foreach (var folder in tree.All)
            {
                var chain = tree.Chain(folder.Location).Select(item => item.Node).ToList();
                if (FolderPermissionResolver.Resolve(FolderOperation.Read, actor.UserId, false, chain, grants, tree.Defaults))
                    readable.Add(folder.Id);
            }

            foreach (var folder in tree.All.Where(item => readable.Contains(item.Id)))
            {
                foreach (var ancestor in tree.Chain(folder.Location))
                {
                    if (!readable.Contains(ancestor.Id)) passThrough.Add(ancestor.Id);
                }
            }
        }

        var result = new ReadableFolders(actor.IsAdmin, tree, readable, passThrough);
        caches.Readable[actor.UserId] = (tree.Version, actor.AuthzVersion, result);
        return result;
    }

    public async Task<bool> CanReadAsync(string? userId, FolderLocation location, CancellationToken cancellationToken = default) =>
        await GetReadableAsync(userId, cancellationToken) is { } readable && readable.IsReadable(location);
}
