using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfmpegVideoConversionProgressTests
{
    [Fact]
    public void ParseProgress_reads_processed_media_time_and_speed()
    {
        var progress = FfmpegVideoConversionGenerator.ParseProgress(new Dictionary<string, string>
        {
            ["out_time_us"] = "45000000",
            ["speed"] = "1.25x",
            ["progress"] = "continue",
        });

        Assert.Equal(45, progress!.ProcessedDurationSeconds);
        Assert.Equal(1.25, progress.Speed);
        Assert.False(progress.IsFinalizing);
    }

    [Fact]
    public void ParseProgress_ignores_malformed_values()
    {
        Assert.Null(FfmpegVideoConversionGenerator.ParseProgress(new Dictionary<string, string> { ["out_time_us"] = "bad", ["speed"] = "slow" }));
    }

    [Fact]
    public void ParseProgress_marks_terminal_ffmpeg_record_as_finalizing()
    {
        var progress = FfmpegVideoConversionGenerator.ParseProgress(new Dictionary<string, string> { ["progress"] = "end" });

        Assert.True(progress!.IsFinalizing);
    }
}
