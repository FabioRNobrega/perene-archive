using WebApp.Identity;

namespace WebApp.Data.Entities;

/// <summary>A user's note on a book passage. <see cref="NoteKey"/> is the opaque browser-facing ID; <see cref="Content"/> keeps arbitrary multiline text.</summary>
public sealed class BookNote
{
    public long Id { get; set; }
    public required string NoteKey { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public required string BookTitle { get; set; }
    public string? BookAuthor { get; set; }
    public int ChapterIndex { get; set; }
    public int? TextOffsetStart { get; set; }
    public int? TextOffsetEnd { get; set; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class BookHighlight
{
    public long Id { get; set; }
    public required string HighlightKey { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public required string ChapterId { get; set; }
    public int TextOffsetStart { get; set; }
    public int TextOffsetEnd { get; set; }
    public required string SelectedText { get; set; }
    public string ContextBefore { get; set; } = string.Empty;
    public string ContextAfter { get; set; } = string.Empty;

    /// <summary>The item revision the highlight was made against; older than the item's current revision means it needs re-anchoring.</summary>
    public int ContentRevision { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class ReadingProgress
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public required string ChapterId { get; set; }
    public int WordOffset { get; set; }
    public double? ScrollFraction { get; set; }
    public int ContentRevision { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class ComicProgress
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public int PageIndex { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class Favorite
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public long MediaItemId { get; set; }
    public MediaItem? MediaItem { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
