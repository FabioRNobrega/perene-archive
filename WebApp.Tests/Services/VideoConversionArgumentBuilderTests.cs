using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class VideoConversionArgumentBuilderTests
{
    [Fact]
    public void Build_burns_only_the_server_resolved_subtitle_stream()
    {
        var profile = new ResolvedVideoConversionProfile("Compress · 480p", MediaAction.FullTranscode, 720, 480, 800_000, 128_000, 1, null, new("compress", 480, "balanced", null, 4), new(4, "dvd_subtitle", "eng", "Track 1 (eng)"));
        var job = new VideoConversionJob("job", null!, MediaAction.FullTranscode, new("mpeg", "mpeg2video", "ac3", null, 720, 480, TimeSpan.FromSeconds(10)), profile);

        var arguments = new VideoConversionArgumentBuilder().Build("/server-only/a:b's.vob", "/server-only/output.mp4", job, 23);

        Assert.Contains("subtitles=filename='/server-only/a\\:b\\'s.vob':si=4,yadif,scale=-2:480:force_original_aspect_ratio=decrease", arguments);
        Assert.DoesNotContain("0:s", arguments);
        Assert.Contains("0:v:0", arguments);
        Assert.Contains("0:a?", arguments);
    }
}
