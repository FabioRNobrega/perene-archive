using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfmpegVideoConversionGeneratorTests
{
    [Fact]
    public void Output_at_the_minimum_savings_boundary_is_published()
    {
        Assert.True(FfmpegVideoConversionGenerator.MeetsMinimumSavings(1_000, 850, 15));
    }

    [Fact]
    public void Output_below_the_minimum_savings_boundary_is_skipped()
    {
        Assert.False(FfmpegVideoConversionGenerator.MeetsMinimumSavings(1_000, 851, 15));
    }

    [Fact]
    public void Missing_source_size_cannot_meet_the_savings_requirement()
    {
        Assert.False(FfmpegVideoConversionGenerator.MeetsMinimumSavings(null, 850, 15));
    }
}
