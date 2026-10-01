namespace WebApp.Authorization;

/// <summary>
/// The single seam for access decisions outside the authorization handler: actor lookup, fresh per-operation checks, and the
/// version-stamped readable-folder set. Per-user services depend on this rather than on the concrete implementation.
/// </summary>
public interface IFolderAccessService
{
    /// <summary>The active account with its Admin membership, or null for an unknown/deactivated account.</summary>
    Task<AccessActor?> GetActorAsync(string? userId, CancellationToken cancellationToken = default);

    Task<FolderTreeSnapshot> GetTreeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<long, PermissionGrant>> LoadGrantsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>A fresh decision for one operation on one folder; never uses the readable-set cache.</summary>
    Task<bool> CheckAsync(string? userId, FolderOperation operation, FolderLocation location, CancellationToken cancellationToken = default);

    /// <summary>The cached readable set for a user; null for an unknown or deactivated account.</summary>
    Task<ReadableFolders?> GetReadableAsync(string? userId, CancellationToken cancellationToken = default);

    Task<bool> CanReadAsync(string? userId, FolderLocation location, CancellationToken cancellationToken = default);
}
