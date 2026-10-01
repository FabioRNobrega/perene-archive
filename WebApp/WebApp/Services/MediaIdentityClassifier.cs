using WebApp.Data.Entities;

namespace WebApp.Services;

/// <summary>What a same-path change means for the identity of the item at that path.</summary>
public enum SamePathChange
{
    /// <summary>Same size and time: nothing changed.</summary>
    Unchanged,

    /// <summary>The category carries no annotations (movie/music/photo): same path is the same item; metadata is refreshed.</summary>
    SameItem,

    /// <summary>The same work with changed bytes: keep the identity and bump the content revision.</summary>
    ConfirmedUpdate,

    /// <summary>A different work at the same path: new identity, the old one is superseded.</summary>
    Replacement,

    /// <summary>The identity of either side cannot be established: new identity, the old one goes to review.</summary>
    Uncertain
}

/// <summary>Pure classification and transition rules for media identity; no I/O.</summary>
internal static class MediaIdentityClassifier
{
    public const string BookCategory = "book";
    public const string ComicCategory = "comic";
    public const string OtherCategory = "other";

    /// <summary>Only books and comics carry annotations or progress, so only they need an identity key.</summary>
    public static bool IsAnnotatable(string category) => category is BookCategory or ComicCategory;

    public static SamePathChange Classify(
        string category, string? oldIdentityKey, string? newIdentityKey, long oldSize, DateTime oldTimeUtc, long newSize, DateTime newTimeUtc)
    {
        if (oldSize == newSize && oldTimeUtc == newTimeUtc) return SamePathChange.Unchanged;
        if (!IsAnnotatable(category)) return SamePathChange.SameItem;
        if (oldIdentityKey is null || newIdentityKey is null) return SamePathChange.Uncertain;
        return string.Equals(oldIdentityKey, newIdentityKey, StringComparison.Ordinal) ? SamePathChange.ConfirmedUpdate : SamePathChange.Replacement;
    }

    /// <summary>The approved status transitions; anything else is rejected. Superseded and NeedsReview never return to Active.</summary>
    public static bool IsAllowedTransition(MediaStatus from, MediaStatus to) => (from, to) switch
    {
        (MediaStatus.Active, MediaStatus.Active) => true,
        (MediaStatus.Active, MediaStatus.Superseded) => true,
        (MediaStatus.Active, MediaStatus.NeedsReview) => true,
        (MediaStatus.Active, MediaStatus.Missing) => true,
        (MediaStatus.Missing, MediaStatus.Active) => true,
        (MediaStatus.Missing, MediaStatus.NeedsReview) => true,
        (MediaStatus.Missing, MediaStatus.Superseded) => true,
        (MediaStatus.NeedsReview, MediaStatus.Superseded) => true,
        _ => false
    };
}
