using WebApp.Client.Models;

namespace WebApp.Models;

internal sealed record ArchiveItemEntry(
    string Id,
    ArchiveCategory Category,
    string PhysicalPath,
    string Name,
    ArchiveItemKind Kind,
    string? Extension,
    long? SizeBytes,
    DateTime LastWriteTimeUtc,
    bool IsVideo,
    bool IsMusic = false,
    bool IsImage = false,
    bool IsBook = false,
    bool IsTextDocument = false,
    bool IsPdfDocument = false,
    string? AlbumCoverId = null,
    bool HasPlayableMedia = false,
    bool IsConvertibleVideo = false);
