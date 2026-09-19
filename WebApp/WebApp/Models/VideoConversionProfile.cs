namespace WebApp.Models;

internal sealed record VideoConversionSelection(string Mode, int? OutputHeight = null, string? QualityPreset = null, long? TargetSizeBytes = null);
internal sealed record ResolvedVideoConversionProfile(string Label, MediaAction Action, int OutputWidth, int OutputHeight, int VideoBitrate, int AudioBitrate, long EstimatedSizeBytes, long? EstimatedSavingsBytes, VideoConversionSelection Selection);
