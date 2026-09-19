namespace WebApp.Client.Models;

public sealed class VideoConversionSelectionDto
{
    public VideoConversionSelectionDto() { }
    public VideoConversionSelectionDto(string mode, int? outputHeight = null, string? qualityPreset = null, long? targetSizeBytes = null) => (Mode, OutputHeight, QualityPreset, TargetSizeBytes) = (mode, outputHeight, qualityPreset, targetSizeBytes);
    public string Mode { get; set; } = "compress";
    public int? OutputHeight { get; set; }
    public string? QualityPreset { get; set; }
    public long? TargetSizeBytes { get; set; }
}
public sealed record VideoConversionOptionDto(string Key, string Label, bool Recommended = false);
public sealed record VideoConversionPlanDto(
    string SourceId, string SourceName, string SourceFileType, string VideoCodec, string? AudioCodec,
    int SourceWidth, int SourceHeight, double DurationSeconds, long? SourceSizeBytes,
    VideoConversionSelectionDto Selection, string ProfileLabel, int OutputWidth, int OutputHeight,
    int VideoBitrate, int AudioBitrate, long EstimatedSizeBytes, long? EstimatedSavingsBytes,
    IReadOnlyList<VideoConversionOptionDto> Modes, IReadOnlyList<VideoConversionOptionDto> Resolutions,
    IReadOnlyList<VideoConversionOptionDto> QualityPresets, long MinimumTargetSizeBytes, long MaximumTargetSizeBytes,
    string Limitation = "Embedded subtitles are not included in converted MP4 files.");
