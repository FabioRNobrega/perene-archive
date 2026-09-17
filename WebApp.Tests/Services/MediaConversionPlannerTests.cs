using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class MediaConversionPlannerTests
{
    private const long OneGiB = 1L * 1024 * 1024 * 1024;

    [Fact]
    public void Defaults_define_the_one_gibibyte_and_one_hour_forty_five_minute_policy()
    {
        var options = new VideoConversionOptions();

        Assert.Equal(OneGiB, options.LargeSourceBytes);
        Assert.Equal(TimeSpan.FromHours(1.75), options.ShortDurationMaximum);
        Assert.Equal(15, options.MinimumSavingsPercent);
        Assert.True(VideoConversionOptions.IsValid(options));
    }

    [Fact]
    public void Compatible_large_short_mp4_is_compressed_regardless_of_bitrate()
    {
        var planner = CreatePlanner();

        Assert.Equal(MediaAction.CompressVideo, planner.Plan(CreateCompatibleMp4(4_000_000, TimeSpan.FromMinutes(78)), 2_822L * 1024 * 1024));
    }

    [Fact]
    public void Compatible_mp4_at_size_or_duration_boundary_is_kept()
    {
        var planner = CreatePlanner();
        var bitrate = 4_000_000L;

        Assert.Equal(MediaAction.Keep, planner.Plan(CreateCompatibleMp4(bitrate, TimeSpan.FromMinutes(59)), OneGiB));
        Assert.Equal(MediaAction.Keep, planner.Plan(CreateCompatibleMp4(bitrate, TimeSpan.FromMinutes(105)), OneGiB + 1));
    }

    [Fact]
    public void Incompatible_streams_retain_their_existing_conversion_actions()
    {
        var planner = CreatePlanner();
        var sourceSize = OneGiB + 1;

        Assert.Equal(MediaAction.Remux, planner.Plan(new("matroska", "h264", "aac", 4_000_000, 1920, 1080, TimeSpan.FromMinutes(78)), sourceSize));
        Assert.Equal(MediaAction.ConvertAudio, planner.Plan(new("mov,mp4", "h264", "mp3", 4_000_000, 1920, 1080, TimeSpan.FromMinutes(78)), sourceSize));
        Assert.Equal(MediaAction.FullTranscode, planner.Plan(new("mov,mp4", "hevc", "aac", 4_000_000, 1920, 1080, TimeSpan.FromMinutes(78)), sourceSize));
    }

    private static MediaConversionPlanner CreatePlanner() => new(Options.Create(new VideoConversionOptions()));

    private static VideoConversionProbeResult CreateCompatibleMp4(long bitrate, TimeSpan duration) =>
        new("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac", bitrate, 1920, 1080, duration);
}
