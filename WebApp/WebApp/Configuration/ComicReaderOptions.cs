namespace WebApp.Configuration;

/// <summary>Limits applied before any untrusted CBZ content is made available.</summary>
public sealed class ComicReaderOptions
{
    public const string SectionName = "ComicReader";
    public int MaximumEntryCount { get; set; } = 500;
    public long MaximumPageBytes { get; set; } = 50L * 1024 * 1024;
    public long MaximumTotalBytes { get; set; } = 500L * 1024 * 1024;
    public static bool IsValid(ComicReaderOptions value) =>
        value.MaximumEntryCount > 0 && value.MaximumPageBytes > 0 && value.MaximumTotalBytes >= value.MaximumPageBytes;
}
