namespace WebApp.Configuration;

public sealed class VideoConversionOptions
{
    public const string SectionName = "VideoConversion";
    public int QueueCapacity { get; set; } = 4;
    public long FreeSpaceReserveBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int H264Crf { get; set; } = 22;
    public int AudioBitrate { get; set; } = 128_000;
    public int BalancedVideoBitrate { get; set; } = 2_500_000;
    public int HighQualityVideoBitrate { get; set; } = 4_000_000;
    public int CompactVideoBitrate { get; set; } = 1_200_000;
    public int MinimumVideoBitrate { get; set; } = 300_000;
    public int MaximumVideoBitrate { get; set; } = 12_000_000;
    public long MinimumTargetSizeBytes { get; set; } = 25L * 1024 * 1024;
    public long MaximumTargetSizeBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public int Mp4OverheadPercent { get; set; } = 3;
    public long SdBitrateThreshold { get; set; } = 3_000_000;
    public long HdBitrateThreshold { get; set; } = 8_000_000;
    public long LargeSourceBytes { get; set; } = 1L * 1024 * 1024 * 1024;
    public TimeSpan ShortDurationMaximum { get; set; } = TimeSpan.FromHours(1.75);
    public int MinimumSavingsPercent { get; set; } = 15;
    public static bool IsValid(VideoConversionOptions value) => value.QueueCapacity > 0 && value.FreeSpaceReserveBytes >= 0 && value.H264Crf is >= 0 and <= 51 && value.AudioBitrate > 0 && value.MinimumVideoBitrate > 0 && value.MaximumVideoBitrate >= value.MinimumVideoBitrate && value.MinimumTargetSizeBytes > 0 && value.MaximumTargetSizeBytes >= value.MinimumTargetSizeBytes && value.Mp4OverheadPercent is >= 0 and <= 20 && value.SdBitrateThreshold > 0 && value.HdBitrateThreshold > 0 && value.LargeSourceBytes > 0 && value.ShortDurationMaximum > TimeSpan.Zero && value.MinimumSavingsPercent is > 0 and < 100;
}
