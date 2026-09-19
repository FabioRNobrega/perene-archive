using WebApp.Configuration;
using WebApp.Models;
using Microsoft.Extensions.Options;

namespace WebApp.Services;

internal sealed class ConversionProfileCatalog(IOptions<VideoConversionOptions> options)
{
    public VideoConversionSelection DefaultFor(VideoConversionProbeResult source) => source.Height <= 720
        ? new("compress", source.Height, "balanced") : new("compress", 720, "balanced");
    public IReadOnlyList<int> HeightsFor(VideoConversionProbeResult source) => [source.Height, .. new[] { 720, 480 }.Where(x => x < source.Height)];
    public bool IsAllowedHeight(VideoConversionProbeResult source, int height) => HeightsFor(source).Contains(height);
    public int BitrateFor(string preset) => preset switch { "high" => options.Value.HighQualityVideoBitrate, "compact" => options.Value.CompactVideoBitrate, "balanced" => options.Value.BalancedVideoBitrate, _ => 0 };
}
