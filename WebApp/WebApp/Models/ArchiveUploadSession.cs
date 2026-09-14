namespace WebApp.Models;

internal enum ArchiveUploadSessionStatus
{
    Uploading,
    Completed,
    Cancelled
}

/// <summary>
/// Server-only, persisted metadata for one resumable archive upload session. Never serialized
/// directly to the browser and never carries a physical or root-relative path.
/// </summary>
internal sealed record ArchiveUploadSession(
    string Id,
    string CategoryKey,
    string? ParentId,
    string FileName,
    long TotalBytes,
    long ReceivedBytes,
    long ChunkSizeBytes,
    ArchiveUploadSessionStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);
