using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ConversionProfileCatalogTests
{
    [Theory]
    [InlineData(1280, 720, 720)]
    [InlineData(1920, 1080, 720)]
    public void Defaults_never_upscale_and_recommend_720p_for_larger_sources(int width, int height, int expected)
    {
        var catalog = new ConversionProfileCatalog(Options.Create(new VideoConversionOptions()));
        var source = new VideoConversionProbeResult("mpegts", "h264", "aac", null, width, height, TimeSpan.FromSeconds(10));

        Assert.Equal(expected, catalog.DefaultFor(source).OutputHeight);
        Assert.All(catalog.HeightsFor(source), output => Assert.True(output <= height));
    }

    [Theory]
    [InlineData(3840, 2160, new[] { 2160, 1080, 720, 480 })]
    [InlineData(2560, 1440, new[] { 1440, 1080, 720, 480 })]
    [InlineData(1920, 1080, new[] { 1080, 720, 480 })]
    public void Heights_offer_1080p_only_below_larger_sources(int width, int height, int[] expected)
    {
        var catalog = new ConversionProfileCatalog(Options.Create(new VideoConversionOptions()));
        var source = new VideoConversionProbeResult("mpegts", "h264", "aac", null, width, height, TimeSpan.FromSeconds(10));

        Assert.Equal(expected, catalog.HeightsFor(source));
    }
}
