using WebApp.Client.Models;

namespace WebApp.Services;

internal interface ICustomStorageViewService
{
    Task<IReadOnlyList<CustomStorageViewDto>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Validates and persists a new custom storage view. Throws <see cref="ArchiveValidationException"/>,
    /// <see cref="ArchiveNotFoundException"/>, or <see cref="ArchiveForbiddenException"/> (propagated from
    /// <see cref="IArchiveService.List"/>) when the selected folder cannot be resolved, and nothing is persisted.
    /// </summary>
    Task<IReadOnlyList<CustomStorageViewDto>> AddAsync(
        string? categoryKey, string? folderId, bool isWholeArchive, long maxSizeBytes, CancellationToken cancellationToken);

    /// <summary>Returns null when <paramref name="viewId"/> does not match any persisted view.</summary>
    Task<IReadOnlyList<CustomStorageViewDto>?> RemoveAsync(string viewId, CancellationToken cancellationToken);

    /// <summary>Returns null when <paramref name="viewId"/> does not match any persisted view.</summary>
    Task<IReadOnlyList<CustomStorageViewDto>?> UpdateMaxSizeAsync(
        string viewId, long maxSizeBytes, CancellationToken cancellationToken);
}
