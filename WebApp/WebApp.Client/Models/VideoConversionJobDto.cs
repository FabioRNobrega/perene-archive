namespace WebApp.Client.Models;
public enum VideoConversionJobState { Pending, Processing, Finalizing, Completed, Failed, Skipped }
public sealed record VideoConversionJobDto(string JobId, string SourceName, string Action, VideoConversionJobState State, long? SourceSizeBytes, long? OutputSizeBytes = null, string? OutputItemId = null, string? Diagnostic = null, DateTimeOffset? QueuedAtUtc = null, DateTimeOffset? StartedAtUtc = null, double? SourceDurationSeconds = null, double? ProcessedDurationSeconds = null, double? Speed = null);
