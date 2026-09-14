using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class ArchiveUploadStateTests
{
    [Fact]
    public void PercentComplete_reports_zero_for_a_freshly_created_item()
    {
        var state = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 100 };

        Assert.Equal(0, state.PercentComplete);
    }

    [Fact]
    public void PercentComplete_reports_a_proportional_value_and_is_clamped_to_100()
    {
        var state = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 200, AcknowledgedBytes = 50 };
        Assert.Equal(25, state.PercentComplete);

        state.AcknowledgedBytes = 500;
        Assert.Equal(100, state.PercentComplete);
    }

    [Fact]
    public void PercentComplete_does_not_divide_by_zero_for_a_zero_length_declared_total()
    {
        var state = new ArchiveUploadItemState { FileName = "empty.txt", TotalBytes = 0 };

        Assert.Equal(0, state.PercentComplete);
    }

    [Fact]
    public void MatchesForResume_requires_both_exact_name_and_exact_size()
    {
        var state = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 1000 };

        Assert.True(state.MatchesForResume("clip.mp4", 1000));
        Assert.False(state.MatchesForResume("clip.mp4", 999));
        Assert.False(state.MatchesForResume("other.mp4", 1000));
        Assert.False(state.MatchesForResume("CLIP.mp4", 1000));
    }

    [Fact]
    public void ComputeBytesPerSecond_is_null_before_the_upload_has_started_or_before_any_bytes_are_acknowledged()
    {
        var notStarted = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 1000 };
        Assert.Null(notStarted.ComputeBytesPerSecond(DateTimeOffset.UtcNow));

        var started = new ArchiveUploadItemState
        {
            FileName = "clip.mp4",
            TotalBytes = 1000,
            StartedAt = DateTimeOffset.UtcNow,
            AcknowledgedBytes = 0
        };
        Assert.Null(started.ComputeBytesPerSecond(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ComputeBytesPerSecond_computes_the_average_rate_since_start()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = new ArchiveUploadItemState
        {
            FileName = "clip.mp4",
            TotalBytes = 1_000_000,
            StartedAt = start,
            AcknowledgedBytes = 200_000
        };

        var rate = state.ComputeBytesPerSecond(start.AddSeconds(10));

        Assert.Equal(20_000, rate);
    }

    [Fact]
    public void EstimateRemaining_is_null_when_the_rate_cannot_be_calculated()
    {
        var state = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 1000 };

        Assert.Null(state.EstimateRemaining(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void EstimateRemaining_computes_time_left_at_the_current_rate()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = new ArchiveUploadItemState
        {
            FileName = "clip.mp4",
            TotalBytes = 1000,
            StartedAt = start,
            AcknowledgedBytes = 500
        };

        var remaining = state.EstimateRemaining(start.AddSeconds(5));

        // 500 bytes acknowledged in 5s => 100 bytes/s; 500 bytes remain => 5s left.
        Assert.Equal(TimeSpan.FromSeconds(5), remaining);
    }

    [Fact]
    public void EstimateRemaining_is_zero_once_every_byte_is_acknowledged()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var state = new ArchiveUploadItemState
        {
            FileName = "clip.mp4",
            TotalBytes = 1000,
            StartedAt = start,
            AcknowledgedBytes = 1000
        };

        var remaining = state.EstimateRemaining(start.AddSeconds(5));

        Assert.Equal(TimeSpan.Zero, remaining);
    }

    [Fact]
    public void Default_status_is_pending_until_the_driver_advances_it()
    {
        var state = new ArchiveUploadItemState { FileName = "clip.mp4", TotalBytes = 1000 };

        Assert.Equal(ArchiveUploadItemStatus.Pending, state.Status);
    }
}
