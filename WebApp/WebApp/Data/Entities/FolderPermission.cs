using WebApp.Authorization;
using WebApp.Identity;

namespace WebApp.Data.Entities;

/// <summary>Tri-state flag for one operation on one folder: unset defers to inheritance and defaults.</summary>
public enum PermissionState { Unset = 0, Allow = 1, Deny = 2 }

/// <summary>A per-folder/per-user permission row; unique on (<see cref="FolderId"/>, <see cref="UserId"/>).</summary>
public sealed class FolderPermission
{
    public long Id { get; set; }
    public long FolderId { get; set; }
    public Folder? Folder { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public PermissionState Read { get; set; }
    public PermissionState Write { get; set; }
    public PermissionState Create { get; set; }
    public PermissionState Delete { get; set; }
    public PermissionState Manage { get; set; }

    /// <summary>When set, this row's Deny flags bind every descendant and cannot be overridden below.</summary>
    public bool Enforced { get; set; }

    /// <summary>Operations whose cells only an Admin may change.</summary>
    public FolderOperation LockMask { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public string? GrantedByUserId { get; set; }
    public ApplicationUser? GrantedBy { get; set; }

    public PermissionState Get(FolderOperation operation) => operation switch
    {
        FolderOperation.Read => Read,
        FolderOperation.Write => Write,
        FolderOperation.Create => Create,
        FolderOperation.Delete => Delete,
        FolderOperation.Manage => Manage,
        _ => PermissionState.Unset
    };

    public void Set(FolderOperation operation, PermissionState state)
    {
        switch (operation)
        {
            case FolderOperation.Read: Read = state; break;
            case FolderOperation.Write: Write = state; break;
            case FolderOperation.Create: Create = state; break;
            case FolderOperation.Delete: Delete = state; break;
            case FolderOperation.Manage: Manage = state; break;
        }
    }

    public bool IsEmpty =>
        Read == PermissionState.Unset && Write == PermissionState.Unset && Create == PermissionState.Unset &&
        Delete == PermissionState.Unset && Manage == PermissionState.Unset && !Enforced && LockMask == 0;
}
