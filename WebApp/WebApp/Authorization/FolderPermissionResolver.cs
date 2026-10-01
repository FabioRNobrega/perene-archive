using WebApp.Data.Entities;

namespace WebApp.Authorization;

/// <summary>One folder on the path from a root to a target, reduced to what the resolver needs.</summary>
public sealed record FolderNode(long Id, FolderAccessMode Mode, string? OwnerUserId);

/// <summary>One user's permission row on one folder, reduced to what the resolver needs.</summary>
public sealed record PermissionGrant(
    PermissionState Read, PermissionState Write, PermissionState Create, PermissionState Delete, PermissionState Manage, bool Enforced)
{
    public PermissionState Get(FolderOperation operation) => operation switch
    {
        FolderOperation.Read => Read,
        FolderOperation.Write => Write,
        FolderOperation.Create => Create,
        FolderOperation.Delete => Delete,
        FolderOperation.Manage => Manage,
        _ => PermissionState.Unset
    };

    public static PermissionGrant From(FolderPermission row) =>
        new(row.Read, row.Write, row.Create, row.Delete, row.Manage, row.Enforced);
}

/// <summary>The Shared-mode defaults from <see cref="AccessPolicy"/>. Manage has no default.</summary>
public sealed record PolicyDefaults(bool Read, bool Write, bool Create, bool Delete)
{
    public static readonly PolicyDefaults Shared = new(true, false, false, false);

    public bool Get(FolderOperation operation) => operation switch
    {
        FolderOperation.Read => Read,
        FolderOperation.Write => Write,
        FolderOperation.Create => Create,
        FolderOperation.Delete => Delete,
        _ => false
    };
}

/// <summary>
/// The pure authorization decision. Order: Admin override, enforced deny, Private-ancestor gate, explicit row on the
/// target, ownership, inherited rows (nearest first), Shared default; every operation except Read additionally needs Read.
/// Anything ambiguous (no folder chain) is denied.
/// </summary>
public static class FolderPermissionResolver
{
    /// <param name="chain">Folders from the root to the target, root first; the last element is the target.</param>
    /// <param name="grants">The caller's permission rows keyed by folder ID.</param>
    public static bool Resolve(
        FolderOperation operation,
        string userId,
        bool isAdmin,
        IReadOnlyList<FolderNode> chain,
        IReadOnlyDictionary<long, PermissionGrant> grants,
        PolicyDefaults defaults)
    {
        if (isAdmin) return true;
        if (chain.Count == 0 || operation == FolderOperation.None) return false;
        if (!ResolveSingle(operation, userId, chain, grants, defaults)) return false;
        return operation == FolderOperation.Read || ResolveSingle(FolderOperation.Read, userId, chain, grants, defaults);
    }

    private static bool ResolveSingle(
        FolderOperation operation, string userId, IReadOnlyList<FolderNode> chain,
        IReadOnlyDictionary<long, PermissionGrant> grants, PolicyDefaults defaults)
    {
        foreach (var node in chain)
        {
            // Enforced deny: no row below the enforcing folder can override it.
            if (grants.TryGetValue(node.Id, out var enforcing) && enforcing.Enforced && enforcing.Get(operation) == PermissionState.Deny)
                return false;
        }

        foreach (var node in chain)
        {
            // Private-ancestor gate: an owner or an explicit Read allow is required on every Private folder in the path.
            if (node.Mode != FolderAccessMode.Private) continue;
            var owns = node.OwnerUserId == userId;
            var allowed = grants.TryGetValue(node.Id, out var gate) && gate.Read == PermissionState.Allow;
            if (!owns && !allowed) return false;
        }

        var target = chain[^1];
        if (grants.TryGetValue(target.Id, out var explicitRow))
        {
            switch (explicitRow.Get(operation))
            {
                case PermissionState.Allow: return true;
                case PermissionState.Deny: return false;
            }
        }

        // Ownership never carries Manage.
        if (target.OwnerUserId == userId && operation != FolderOperation.Manage) return true;

        for (var index = chain.Count - 2; index >= 0; index--)
        {
            if (!grants.TryGetValue(chain[index].Id, out var inherited)) continue;
            switch (inherited.Get(operation))
            {
                case PermissionState.Allow: return true;
                case PermissionState.Deny: return false;
            }
        }

        return defaults.Get(operation);
    }

    /// <summary>
    /// Why <see cref="Resolve"/> reached its answer for one operation, for editor tooltips:
    /// the provenance label and the folder ID that decided it (null for defaults).
    /// </summary>
    public static (bool Allowed, string Provenance, long? SourceFolderId) Explain(
        FolderOperation operation, string userId, IReadOnlyList<FolderNode> chain,
        IReadOnlyDictionary<long, PermissionGrant> grants, PolicyDefaults defaults)
    {
        if (chain.Count == 0) return (false, "No access", null);
        foreach (var node in chain)
        {
            if (grants.TryGetValue(node.Id, out var enforcing) && enforcing.Enforced && enforcing.Get(operation) == PermissionState.Deny)
                return (false, "Enforced deny", node.Id);
        }

        foreach (var node in chain)
        {
            if (node.Mode != FolderAccessMode.Private) continue;
            if (node.OwnerUserId != userId && !(grants.TryGetValue(node.Id, out var gate) && gate.Read == PermissionState.Allow))
                return (false, "Private: no access", node.Id);
        }

        var target = chain[^1];
        if (grants.TryGetValue(target.Id, out var explicitRow) && explicitRow.Get(operation) != PermissionState.Unset)
            return (explicitRow.Get(operation) == PermissionState.Allow, "Explicit", target.Id);
        if (target.OwnerUserId == userId && operation != FolderOperation.Manage) return (true, "Owner", target.Id);
        for (var index = chain.Count - 2; index >= 0; index--)
        {
            if (grants.TryGetValue(chain[index].Id, out var inherited) && inherited.Get(operation) != PermissionState.Unset)
                return (inherited.Get(operation) == PermissionState.Allow, "Inherited", chain[index].Id);
        }

        return (defaults.Get(operation), "Shared default", null);
    }
}
