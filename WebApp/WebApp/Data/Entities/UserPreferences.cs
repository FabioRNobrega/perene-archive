using WebApp.Identity;

namespace WebApp.Data.Entities;

/// <summary>
/// A reader theme, shared by everyone. <see cref="CreatedByUserId"/> is the only account that may edit or delete it; it becomes null if
/// that account is deleted, after which the theme stays available but nobody can change it.
/// </summary>
public sealed class ReaderTheme
{
    public long Id { get; set; }

    /// <summary>The opaque browser-facing theme ID.</summary>
    public required string PublicId { get; set; }
    public string? CreatedByUserId { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
    public required string Name { get; set; }
    public required string FontFamily { get; set; }
    public int FontSizePx { get; set; }
    public double LineHeight { get; set; }
    public string? ForegroundColor { get; set; }
    public string? BackgroundColor { get; set; }
    public int ContentPaddingPercent { get; set; } = 5;
    public DateTimeOffset CreatedUtc { get; set; }
}

/// <summary>A user's currently active reader settings and selected theme (one row per user).</summary>
public sealed class ReaderThemePreference
{
    public required string UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public required string FontFamily { get; set; }
    public int FontSizePx { get; set; }
    public double LineHeight { get; set; }
    public string? ForegroundColor { get; set; }
    public string? BackgroundColor { get; set; }
    public int ContentPaddingPercent { get; set; } = 5;
    public string? SelectedThemePublicId { get; set; }
}

/// <summary>A custom storage-usage view. A null <see cref="UserId"/> is a global view.</summary>
public sealed class CustomStorageView
{
    public long Id { get; set; }
    public required string PublicId { get; set; }
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? CategoryKey { get; set; }
    public string? FolderId { get; set; }
    public bool IsWholeArchive { get; set; }
    public required string Title { get; set; }
    public long MaxSizeBytes { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
