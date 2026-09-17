using WebApp.Client.Models;
namespace WebApp.Models;
internal enum MediaAction { Keep, Remux, ConvertAudio, CompressVideo, FullTranscode }
internal sealed record VideoConversionProbeResult(string Container, string VideoCodec, string? AudioCodec, long? VideoBitrate, int Width, int Height, TimeSpan Duration, bool HasSubtitles = false);
internal sealed record VideoConversionJob(string JobId, ArchiveItemEntry Source, MediaAction Action, VideoConversionProbeResult Probe);
internal sealed record VideoConversionStatus(string JobId, string SourceId, string SourceName, MediaAction Action, VideoConversionJobState State, long? SourceSizeBytes, long? OutputSizeBytes = null, string? OutputItemId = null, string? Diagnostic = null, DateTimeOffset? QueuedAtUtc = null, DateTimeOffset? StartedAtUtc = null, double? SourceDurationSeconds = null, double? ProcessedDurationSeconds = null, double? Speed = null);
internal sealed record VideoConversionGenerationResult(bool Success, bool Skipped, string? DestinationPath = null, long? OutputSizeBytes = null, string? Diagnostic = null);
internal sealed record VideoConversionProgress(double? ProcessedDurationSeconds, double? Speed, bool IsFinalizing = false);
