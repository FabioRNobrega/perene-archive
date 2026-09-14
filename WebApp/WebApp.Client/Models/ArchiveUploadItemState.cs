namespace WebApp.Client.Models;

/// <summary>UI-facing lifecycle for one file's resumable upload, distinct from the wire-level <see cref="ArchiveUploadStatus"/>.</summary>
public enum ArchiveUploadItemStatus
{
    Pending,
    Uploading,
    Interrupted,
    Completing,
    Done,
    Error
}

/// <summary>
/// Pure, framework-free client-side state for one resumable archive upload. Holds only what the
/// UI needs to render acknowledged progress, rate/ETA, and status; owns no browser file handles,
/// HTTP calls, or JS interop so it can be unit tested directly.
/// </summary>
public sealed class ArchiveUploadItemState
{
    public required string FileName { get; init; }

    public required long TotalBytes { get; init; }

    public string? UploadId { get; set; }

    public long AcknowledgedBytes { get; set; }

    public long? ChunkSizeBytes { get; set; }

    public ArchiveUploadItemStatus Status { get; set; } = ArchiveUploadItemStatus.Pending;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Percentage of <see cref="TotalBytes"/> the server has acknowledged, clamped to [0, 100].</summary>
    public double PercentComplete =>
        TotalBytes <= 0 ? 0 : Math.Clamp(AcknowledgedBytes * 100.0 / TotalBytes, 0, 100);

    /// <summary>Whether a reselected local file matches this session's original identity closely enough to resume.</summary>
    public bool MatchesForResume(string fileName, long fileSize) =>
        string.Equals(FileName, fileName, StringComparison.Ordinal) && TotalBytes == fileSize;

    /// <summary>Average acknowledged bytes/second since <see cref="StartedAt"/>, or null when it cannot yet be calculated.</summary>
    public double? ComputeBytesPerSecond(DateTimeOffset now)
    {
        if (StartedAt is not { } startedAt || AcknowledgedBytes <= 0)
        {
            return null;
        }

        var elapsedSeconds = (now - startedAt).TotalSeconds;
        return elapsedSeconds > 0 ? AcknowledgedBytes / elapsedSeconds : null;
    }

    /// <summary>Estimated remaining time at the current average rate, or null when it cannot yet be calculated.</summary>
    public TimeSpan? EstimateRemaining(DateTimeOffset now)
    {
        var rate = ComputeBytesPerSecond(now);
        if (rate is not > 0)
        {
            return null;
        }

        var remainingBytes = TotalBytes - AcknowledgedBytes;
        return remainingBytes <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(remainingBytes / rate.Value);
    }
}
