namespace WebApp.Models;

internal sealed record VideoConversionSelection(string Mode, int? OutputHeight = null, string? QualityPreset = null, long? TargetSizeBytes = null, int? SelectedSubtitleStreamIndex = null, bool BurnClosedCaptions = false);
internal sealed record ResolvedVideoConversionProfile(string Label, MediaAction Action, int OutputWidth, int OutputHeight, int VideoBitrate, int AudioBitrate, long EstimatedSizeBytes, long? EstimatedSavingsBytes, VideoConversionSelection Selection, VideoConversionSubtitleStream? SelectedSubtitle = null, bool BurnClosedCaptions = false);
