using System.Globalization;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Builds a fixed FFmpeg argument list from a server-resolved profile only.</summary>
internal sealed class VideoConversionArgumentBuilder
{
    public IReadOnlyList<string> Build(string source, string output, VideoConversionJob job, int fallbackCrf)
    {
        if (job.Profile is null) return FfmpegVideoConversionGenerator.BuildArguments(source, output, job.Action, fallbackCrf);
        var profile = job.Profile;
        if (profile.Action == MediaAction.Remux) return FfmpegVideoConversionGenerator.BuildArguments(source, output, MediaAction.Remux, fallbackCrf);
        return ["-nostdin", "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-i", source, "-map", "0:v:0", "-map", "0:a?", "-map_metadata", "0", "-map_chapters", "0", "-vf", $"yadif,scale=-2:{profile.OutputHeight}:force_original_aspect_ratio=decrease", "-c:v", "libx264", "-b:v", profile.VideoBitrate.ToString(CultureInfo.InvariantCulture), "-maxrate", profile.VideoBitrate.ToString(CultureInfo.InvariantCulture), "-bufsize", (profile.VideoBitrate * 2).ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-profile:a", "aac_low", "-b:a", profile.AudioBitrate.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-y", output];
    }
}
