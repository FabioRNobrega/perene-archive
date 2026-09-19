using WebApp.Configuration;
using WebApp.Models;
using Microsoft.Extensions.Options;

namespace WebApp.Services;

internal sealed class ConversionProfileResolver(ConversionProfileCatalog catalog, ConversionEstimateCalculator estimates, IOptions<VideoConversionOptions> options)
{
    public bool TryResolve(VideoConversionProbeResult source, long? sourceBytes, VideoConversionSelection selection, out ResolvedVideoConversionProfile? profile, out string? error)
    {
        profile = null; error = null;
        if (source.HasSubtitles) { error = "Files with embedded subtitles cannot be converted by this release."; return false; }
        if (selection.Mode is not ("compatible" or "compress")) { error = "The selected conversion mode is not supported."; return false; }
        var height = selection.OutputHeight ?? catalog.DefaultFor(source).OutputHeight ?? source.Height;
        if (!catalog.IsAllowedHeight(source, height)) { error = "The selected resolution would upscale or is not supported."; return false; }
        if (selection.Mode == "compatible" && height != source.Height) { error = "Make compatible preserves the original resolution."; return false; }
        var width = Math.Max(2, (int)Math.Floor(source.Width * (height / (double)source.Height) / 2) * 2);
        var audio = options.Value.AudioBitrate;
        var preset = selection.QualityPreset ?? "balanced";
        var video = selection.TargetSizeBytes is { } target
            ? target < options.Value.MinimumTargetSizeBytes || target > options.Value.MaximumTargetSizeBytes ? 0 : estimates.VideoBitrateForTarget(source.Duration, target, audio)
            : catalog.BitrateFor(preset);
        if (video is < 1 || video < options.Value.MinimumVideoBitrate || video > options.Value.MaximumVideoBitrate) { error = "The selected quality or target size is not supported."; return false; }
        var action = selection.Mode == "compatible" && source.VideoCodec.Equals("h264", StringComparison.OrdinalIgnoreCase) && (source.AudioCodec is null || source.AudioCodec.Equals("aac", StringComparison.OrdinalIgnoreCase)) ? MediaAction.Remux : MediaAction.FullTranscode;
        var estimated = action == MediaAction.Remux ? sourceBytes ?? estimates.EstimateBytes(source.Duration, video, audio) : estimates.EstimateBytes(source.Duration, video, audio);
        profile = new($"{(selection.Mode == "compatible" ? "Make compatible" : "Compress")} · {height}p", action, width, height, video, audio, estimated, sourceBytes is > 0 ? sourceBytes - estimated : null, selection with { OutputHeight = height, QualityPreset = preset });
        return true;
    }
}
