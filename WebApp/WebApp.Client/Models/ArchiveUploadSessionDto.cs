namespace WebApp.Client.Models;

public sealed record ArchiveUploadSessionDto(
    string Id,
    string Category,
    string? ParentId,
    string FileName,
    long TotalBytes,
    long ReceivedBytes,
    long ChunkSizeBytes,
    ArchiveUploadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt);
