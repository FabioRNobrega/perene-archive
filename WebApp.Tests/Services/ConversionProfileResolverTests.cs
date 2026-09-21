using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ConversionProfileResolverTests
{
    [Fact]
    public void Embedded_subtitle_requires_current_selection_and_forces_transcode()
    {
        var resolver = CreateResolver();
        var source = new VideoConversionProbeResult("matroska", "h264", "aac", null, 1280, 720, TimeSpan.FromSeconds(10), [new(2, "dvd_subtitle", null, "Track 1 (language unknown)")]);

        Assert.False(resolver.TryResolve(source, 1_000_000, new("compatible", 720, "balanced"), out _, out _));
        Assert.False(resolver.TryResolve(source, 1_000_000, new("compatible", 720, "balanced", null, 99), out _, out _));
        Assert.True(resolver.TryResolve(source, 1_000_000, new("compatible", 720, "balanced", null, 2), out var profile, out _));
        Assert.Equal(MediaAction.FullTranscode, profile!.Action);
        Assert.Equal(2, profile.SelectedSubtitle!.InputStreamIndex);
    }

    private static ConversionProfileResolver CreateResolver()
    {
        var options = Options.Create(new VideoConversionOptions());
        return new(new ConversionProfileCatalog(options), new ConversionEstimateCalculator(options), options);
    }
}
