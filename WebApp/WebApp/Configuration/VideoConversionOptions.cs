namespace WebApp.Configuration;

public sealed class VideoConversionOptions
{
    public const string SectionName = "VideoConversion";
    public int QueueCapacity { get; set; } = 4;
    public long FreeSpaceReserveBytes { get; set; } = 2L * 1024 * 1024 * 1024;
    public int H264Crf { get; set; } = 22;
    public long SdBitrateThreshold { get; set; } = 3_000_000;
    public long HdBitrateThreshold { get; set; } = 8_000_000;
    public long LargeSourceBytes { get; set; } = 1L * 1024 * 1024 * 1024;
    public TimeSpan ShortDurationMaximum { get; set; } = TimeSpan.FromHours(1.75);
    public int MinimumSavingsPercent { get; set; } = 15;
    public static bool IsValid(VideoConversionOptions value) => value.QueueCapacity > 0 && value.FreeSpaceReserveBytes >= 0 && value.H264Crf is >= 0 and <= 51 && value.SdBitrateThreshold > 0 && value.HdBitrateThreshold > 0 && value.LargeSourceBytes > 0 && value.ShortDurationMaximum > TimeSpan.Zero && value.MinimumSavingsPercent is > 0 and < 100;
}
