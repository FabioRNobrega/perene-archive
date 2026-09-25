using System.Globalization;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Builds a fixed FFmpeg argument list from a server-resolved profile only.</summary>
internal sealed class VideoConversionArgumentBuilder
{
    public IReadOnlyList<string> Build(string source, string output, VideoConversionJob job, int fallbackCrf, string? closedCaptionPath = null)
    {
        if (job.Profile is null) return FfmpegVideoConversionGenerator.BuildArguments(source, output, job.Action, fallbackCrf);
        var profile = job.Profile;
        if (profile.Action == MediaAction.Remux) return FfmpegVideoConversionGenerator.BuildArguments(source, output, MediaAction.Remux, fallbackCrf);
        var isBitmapSubtitle = profile.SelectedSubtitle?.Codec is "dvd_subtitle" or "hdmv_pgs_subtitle";
        var subtitleOrdinal = profile.SelectedSubtitle?.InputStreamIndex ?? -1;
        if (profile.SelectedSubtitle is not null && job.Probe.SubtitleStreams is { } subtitleStreams)
        {
            subtitleOrdinal = subtitleStreams
                .Select((stream, index) => (stream, index))
                .Single(x => x.stream.InputStreamIndex == profile.SelectedSubtitle.InputStreamIndex)
                .index;
        }
        var subtitleFilter = profile.SelectedSubtitle is null || isBitmapSubtitle ? "" : $"subtitles=filename='{EscapeFilterValue(source)}':si={subtitleOrdinal},";
        if (profile.BurnClosedCaptions && !string.IsNullOrWhiteSpace(closedCaptionPath)) subtitleFilter += $"subtitles=filename='{EscapeFilterValue(closedCaptionPath)}',";
        var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-progress", "pipe:1", "-nostats", "-i", source };
        if (isBitmapSubtitle)
        {
            args.AddRange(["-filter_complex", $"[0:v:0][0:s:{subtitleOrdinal}]overlay,yadif,scale=-2:{profile.OutputHeight}:force_original_aspect_ratio=decrease[video]", "-map", "[video]"]);
        }
        else args.AddRange(["-map", "0:v:0", "-vf", $"{subtitleFilter}yadif,scale=-2:{profile.OutputHeight}:force_original_aspect_ratio=decrease"]);
        args.AddRange(["-map", "0:a?", "-map_metadata", "0", "-map_chapters", "0", "-c:v", "libx264", "-b:v", profile.VideoBitrate.ToString(CultureInfo.InvariantCulture), "-maxrate", profile.VideoBitrate.ToString(CultureInfo.InvariantCulture), "-bufsize", (profile.VideoBitrate * 2).ToString(CultureInfo.InvariantCulture), "-c:a", "aac", "-profile:a", "aac_low", "-b:a", profile.AudioBitrate.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-y", output]);
        return args;
    }
    internal static string EscapeFilterValue(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace(":", "\\:", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
}
