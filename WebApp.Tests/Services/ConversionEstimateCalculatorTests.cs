using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ConversionEstimateCalculatorTests
{
    [Fact]
    public void Estimate_reserves_the_configured_mp4_overhead()
    {
        var calculator = new ConversionEstimateCalculator(Options.Create(new VideoConversionOptions { Mp4OverheadPercent = 3 }));

        Assert.Equal(12_875_000, calculator.EstimateBytes(TimeSpan.FromSeconds(100), 800_000, 200_000));
    }

    [Fact]
    public void Target_size_derives_a_bounded_video_bitrate()
    {
        var options = new VideoConversionOptions { MinimumVideoBitrate = 300_000, MaximumVideoBitrate = 2_000_000, Mp4OverheadPercent = 3 };
        var calculator = new ConversionEstimateCalculator(Options.Create(options));

        Assert.InRange(calculator.VideoBitrateForTarget(TimeSpan.FromSeconds(100), 25_750_000, 200_000), 1_799_000, 1_801_000);
    }
}
