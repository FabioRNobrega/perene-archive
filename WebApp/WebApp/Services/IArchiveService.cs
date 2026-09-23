using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveService
{
    ArchiveListing List(string categoryKey, string? folderId);

    ArchiveListing ListPlaylist(string categoryKey, string? folderId);

    ArchiveListing CreateFolder(string categoryKey, string? parentId, string name);

    ArchiveListing CreateFile(string categoryKey, string? parentId, string name, string extension);

    /// <summary>
    /// Validates that <paramref name="fileName"/> could be uploaded into the given category/parent
    /// right now (category permission, safe name, supported extension, parent resolution, and final
    /// name collision) without writing anything. Used by <c>IArchiveUploadService</c> both at
    /// session creation and again at completion, immediately before publishing.
    /// </summary>
    ArchiveUploadDestination ValidateUploadDestination(string categoryKey, string? parentId, string fileName);

    /// <summary>
    /// Re-validates the upload destination and atomically moves <paramref name="sourceTempPath"/>
    /// (an already-fully-received file owned by the caller) into the final archive location.
    /// </summary>
    ArchiveListing PublishUploadedFile(string categoryKey, string? parentId, string fileName, string sourceTempPath);

    ArchiveListing Rename(string categoryKey, string itemId, string name);

    /// <summary>
    /// Validates the move exactly as before (category/item/folder resolution, name-conflict and
    /// self-move checks) but does not touch the filesystem; it returns a job descriptor for the caller
    /// to seed into <c>IArchiveMutationJobStatusStore</c> and enqueue onto <c>IArchiveMutationJobQueue</c>.
    /// </summary>
    ArchiveMutationJob Move(string categoryKey, string itemId, string destinationCategoryKey, string? destinationFolderId);

    /// <summary>
    /// Validates every requested item exactly as <see cref="Move"/> does, all-or-nothing, before
    /// returning a single job descriptor whose <c>BatchEntries</c> the caller enqueues as one
    /// combined-progress <see cref="ArchiveMutationKind.BatchMove"/> job.
    /// </summary>
    ArchiveMutationJob BatchMove(string categoryKey, IReadOnlyList<string> itemIds, string destinationCategoryKey, string? destinationFolderId);

    ArchiveMutationJob MoveToTrash(string categoryKey, string itemId);

    /// <summary>
    /// Validates every requested item exactly as <see cref="MoveToTrash"/> does, all-or-nothing, before
    /// returning a single <see cref="ArchiveMutationKind.BatchMoveToTrash"/> job descriptor whose
    /// <c>BatchEntries</c> the caller enqueues with combined progress.
    /// </summary>
    ArchiveMutationJob BatchMoveToTrash(string categoryKey, IReadOnlyList<string> itemIds);

    ArchiveMutationJob EmptyTrash(string categoryKey);

    bool TryResolveVideo(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolveDownloadableItem(string categoryKey, string itemId, out ArchiveItemEntry? item);
    bool TryResolveConvertibleVideo(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        return false;
    }

    bool TryResolveMusic(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolveImage(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolveComic(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        return false;
    }

    bool TryResolveBook(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolveTextDocument(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolvePdfDocument(string categoryKey, string itemId, out ArchiveItemEntry? item);

    bool TryResolveAlbumCover(string categoryKey, string folderId, out ArchiveAlbumCoverInfo? cover);

    /// <summary>
    /// Resolves an opaque item ID to a non-category-root folder within the given category.
    /// </summary>
    bool TryResolveFolder(string categoryKey, string itemId, out ArchiveItemEntry? folder);

    /// <summary>
    /// Computes the reserved folder-thumbnail file path physically inside <paramref name="folder"/>,
    /// regardless of whether it currently exists.
    /// </summary>
    string GetFolderThumbnailPath(ArchiveItemEntry folder);

    /// <summary>
    /// Resolves the reserved folder-thumbnail file path for <paramref name="folder"/> only if it exists.
    /// </summary>
    bool TryGetFolderThumbnailPath(ArchiveItemEntry folder, out string thumbnailPath);

    string GetCategoryRootPath(string categoryKey);

    string ComputeItemId(string categoryKey, string physicalPath);
}
