namespace WebApp.Client.Models;

public sealed record VideoItemDto(
    string Id,
    string Name,
    string Extension,
    long SizeBytes,
    ThumbnailState ThumbnailState,
    string? ThumbnailUrl,
    HoverPreviewState HoverPreviewState,
    string? HoverPreviewUrl,
    SubtitleState SubtitleState,
    string? SubtitleUrl,
    double? DurationSeconds,
    int? Width,
    int? Height,
    IReadOnlyList<AudioTrackDto>? AudioTracks = null);
