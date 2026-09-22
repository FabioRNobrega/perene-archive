namespace WebApp.Models;

internal sealed record AudioTrackProbeResult(int Index, string? Language, string? Codec);

internal sealed record VideoMetadata(
    TimeSpan? Duration,
    int? Width,
    int? Height,
    IReadOnlyList<AudioTrackProbeResult>? AudioTracks = null);
