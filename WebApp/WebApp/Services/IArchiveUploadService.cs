using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveUploadService
{
    ArchiveUploadSession Create(string categoryKey, string? parentId, string fileName, long totalBytes);

    ArchiveUploadSession GetStatus(string categoryKey, string uploadId);

    Task<ArchiveUploadSession> AppendChunkAsync(
        string categoryKey, string uploadId, long offset, long expectedLength, Stream body, CancellationToken cancellationToken);

    Task<ArchiveListing> CompleteAsync(string categoryKey, string uploadId, CancellationToken cancellationToken);

    Task CancelAsync(string categoryKey, string uploadId, CancellationToken cancellationToken);

    IReadOnlyList<ArchiveUploadSession> ListActive(string categoryKey, string? parentId);

    Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken);
}
