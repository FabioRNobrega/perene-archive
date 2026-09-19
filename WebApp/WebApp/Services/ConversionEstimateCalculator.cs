using WebApp.Configuration;
using Microsoft.Extensions.Options;

namespace WebApp.Services;

internal sealed class ConversionEstimateCalculator(IOptions<VideoConversionOptions> options)
{
    public long EstimateBytes(TimeSpan duration, int videoBitrate, int audioBitrate) =>
        (long)Math.Ceiling(duration.TotalSeconds * (videoBitrate + audioBitrate) / 8d * (1 + options.Value.Mp4OverheadPercent / 100d));
    public int VideoBitrateForTarget(TimeSpan duration, long targetBytes, int audioBitrate) =>
        (int)Math.Clamp(Math.Floor(targetBytes * 8d / duration.TotalSeconds / (1 + options.Value.Mp4OverheadPercent / 100d) - audioBitrate), options.Value.MinimumVideoBitrate, options.Value.MaximumVideoBitrate);
}
