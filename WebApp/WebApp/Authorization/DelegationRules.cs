using WebApp.Data.Entities;

namespace WebApp.Authorization;

/// <summary>
/// The pure delegation rules for editing another account's folder permissions. An Admin may change anything. Anyone else needs
/// Manage on the folder, cannot edit their own row, cannot grant Manage, enforcement, locks or a folder mode, cannot touch a
/// locked cell, and cannot grant an operation they do not hold themselves (no escalation).
/// </summary>
public static class DelegationRules
{
    /// <returns>A message naming the violated rule, or null when the change is allowed.</returns>
    public static string? Validate(
        bool actorIsAdmin,
        bool actorIsTarget,
        bool actorHasManage,
        Func<FolderOperation, bool> actorHas,
        FolderPermission? existing,
        IReadOnlyDictionary<FolderOperation, PermissionState> cells,
        bool changesMode,
        bool changesEnforcement,
        bool changesLockMask,
        bool clearsRow)
    {
        if (actorIsAdmin) return null;
        if (!actorHasManage) return "You do not manage this folder.";
        if (actorIsTarget) return "You cannot change your own access.";
        if (changesMode) return "Only an administrator can change a folder's mode.";
        if (changesEnforcement) return "Only an administrator can enforce permissions.";
        if (changesLockMask) return "Only an administrator can lock permissions.";
        if (cells.ContainsKey(FolderOperation.Manage)) return "Only an administrator can grant management.";

        var locked = existing?.LockMask ?? FolderOperation.None;
        foreach (var (operation, state) in cells)
        {
            if ((locked & operation) != 0) return $"{operation} is locked on this folder.";
            if (state == PermissionState.Allow && existing?.Get(operation) != PermissionState.Allow && !actorHas(operation))
                return $"You cannot grant {operation} because you do not have it.";
        }

        if (clearsRow && existing is not null && (existing.LockMask != FolderOperation.None || existing.Enforced))
            return "This row is locked and cannot be cleared.";
        return null;
    }
}
