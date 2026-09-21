using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfprobeVideoConversionProbeTests
{
    [Fact]
    public void Parse_returns_only_safe_ordered_embedded_subtitle_options()
    {
        const string json = """{"format":{"format_name":"mpeg","duration":"12.5"},"streams":[{"index":0,"codec_type":"video","codec_name":"mpeg2video","width":720,"height":480},{"index":1,"codec_type":"audio","codec_name":"ac3"},{"index":2,"codec_type":"subtitle","codec_name":"dvd_subtitle","tags":{"language":"EN-gb!"}},{"index":3,"codec_type":"subtitle","codec_name":"dvd_subtitle"}]}""";

        var result = FfprobeVideoConversionProbe.Parse(json)!;

        Assert.Equal(2, result.SubtitleStreams!.Count);
        Assert.Collection(result.SubtitleStreams,
            first => { Assert.Equal(2, first.InputStreamIndex); Assert.Equal("en-gb", first.Language); Assert.Equal("Track 1 (en-gb)", first.Label); },
            second => { Assert.Equal(3, second.InputStreamIndex); Assert.Null(second.Language); Assert.Equal("Track 2 (language unknown)", second.Label); });
    }
}
