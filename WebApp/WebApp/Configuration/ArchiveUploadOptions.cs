namespace WebApp.Configuration;

/// <summary>
/// Configures the server-owned resumable upload workspace used by <c>IArchiveUploadService</c>.
/// The workspace itself lives at <c>&lt;ArchiveRootOptions.Path&gt;/.uploads</c> and is never a
/// browseable category folder or static-file root.
/// </summary>
public sealed class ArchiveUploadOptions
{
    public const string SectionName = "ArchiveUpload";

    /// <summary>How long an upload session may remain inactive before cleanup removes it.</summary>
    public int SessionTtlHours { get; set; } = 24;

    /// <summary>How often the cleanup background worker scans for expired sessions.</summary>
    public int CleanupIntervalMinutes { get; set; } = 30;

    /// <summary>The largest declared total upload size accepted at session creation.</summary>
    public long MaxDeclaredSizeBytes { get; set; } = 12L * 1024 * 1024 * 1024;

    /// <summary>Chunk size used for declared totals at or below <see cref="MediumThresholdBytes"/>.</summary>
    public long SmallChunkSizeBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>Chunk size used for declared totals above <see cref="MediumThresholdBytes"/> and at or below <see cref="LargeThresholdBytes"/>.</summary>
    public long MediumChunkSizeBytes { get; set; } = 20L * 1024 * 1024;

    /// <summary>Chunk size used for declared totals above <see cref="LargeThresholdBytes"/> and at or below <see cref="ExtraLargeThresholdBytes"/>.</summary>
    public long LargeChunkSizeBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>Chunk size used for declared totals above <see cref="ExtraLargeThresholdBytes"/>.</summary>
    public long ExtraLargeChunkSizeBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>Declared-size boundary (~100 MB) between the small and medium chunk tiers.</summary>
    public long MediumThresholdBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>Declared-size boundary (~1 GB) between the medium and large chunk tiers.</summary>
    public long LargeThresholdBytes { get; set; } = 1024L * 1024 * 1024;

    /// <summary>Declared-size boundary (~10 GB) between the large and extra-large chunk tiers.</summary>
    public long ExtraLargeThresholdBytes { get; set; } = 10L * 1024 * 1024 * 1024;

    /// <summary>Deterministically selects the server-owned chunk size for a declared total upload size.</summary>
    public long SelectChunkSize(long declaredTotalBytes)
    {
        if (declaredTotalBytes <= MediumThresholdBytes)
        {
            return SmallChunkSizeBytes;
        }

        if (declaredTotalBytes <= LargeThresholdBytes)
        {
            return MediumChunkSizeBytes;
        }

        if (declaredTotalBytes <= ExtraLargeThresholdBytes)
        {
            return LargeChunkSizeBytes;
        }

        return ExtraLargeChunkSizeBytes;
    }

    public static bool HasPositiveTtl(ArchiveUploadOptions options) => options.SessionTtlHours > 0;

    public static bool HasPositiveCleanupInterval(ArchiveUploadOptions options) => options.CleanupIntervalMinutes > 0;

    public static bool HasValidMaxSize(ArchiveUploadOptions options) =>
        options.MaxDeclaredSizeBytes >= 10L * 1024 * 1024 * 1024;

    public static bool HasPositiveChunkSizes(ArchiveUploadOptions options) =>
        options.SmallChunkSizeBytes > 0 &&
        options.MediumChunkSizeBytes > options.SmallChunkSizeBytes &&
        options.LargeChunkSizeBytes > options.MediumChunkSizeBytes &&
        options.ExtraLargeChunkSizeBytes > options.LargeChunkSizeBytes;

    public static bool HasIncreasingThresholds(ArchiveUploadOptions options) =>
        options.MediumThresholdBytes > 0 &&
        options.LargeThresholdBytes > options.MediumThresholdBytes &&
        options.ExtraLargeThresholdBytes > options.LargeThresholdBytes &&
        options.ExtraLargeThresholdBytes <= options.MaxDeclaredSizeBytes;
}
