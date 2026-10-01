namespace WebApp.Data.Entities;

/// <summary>Lifecycle of a media identity. Only <see cref="Active"/> occupies its path and presents user data.</summary>
public enum MediaStatus { Active = 0, Missing = 1, NeedsReview = 2, Superseded = 3 }

/// <summary>
/// Stable server-side identity for a media file. User notes, progress and favorites reference <see cref="Id"/>, never a path,
/// so renames keep them attached. Every column here is server-only; nothing is sent to the browser.
/// </summary>
public sealed class MediaItem
{
    public long Id { get; set; }

    /// <summary>The seeded root <see cref="Folder"/> row the item lives under (root rows are never removed).</summary>
    public long FolderId { get; set; }
    public Folder? Folder { get; set; }

    /// <summary>"book", "comic", or "other" (movie/music/photo/document; these carry only favorites).</summary>
    public required string Category { get; set; }

    /// <summary>Forward-slash path relative to the root folder; the file name is its last segment.</summary>
    public required string RelativePath { get; set; }
    public long Size { get; set; }
    public DateTime LastWriteTimeUtc { get; set; }
    public string? ContentFingerprint { get; set; }

    /// <summary>Identity of the work (not the bytes); null where it cannot be computed or the category carries no annotations.</summary>
    public string? IdentityKey { get; set; }
    public int ContentRevision { get; set; } = 1;
    public long? SupersededByMediaItemId { get; set; }
    public MediaItem? SupersededBy { get; set; }
    public MediaStatus Status { get; set; } = MediaStatus.Active;
    public DateTimeOffset? MissingSince { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
