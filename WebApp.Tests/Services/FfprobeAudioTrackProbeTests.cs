using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfprobeAudioTrackProbeTests
{
    [Fact]
    public void ParseOutput_returns_one_entry_per_stream_with_language_and_codec()
    {
        var json = """{"streams":[{"index":1,"codec_name":"aac","tags":{"language":"jpn"}},{"index":2,"codec_name":"ac3","tags":{"language":"eng"}}]}""";

        var tracks = FfprobeAudioTrackProbe.ParseOutput(json);

        Assert.Equal(2, tracks.Count);
        Assert.Equal((0, "jpn", "aac"), (tracks[0].Index, tracks[0].Language, tracks[0].Codec));
        Assert.Equal((1, "eng", "ac3"), (tracks[1].Index, tracks[1].Language, tracks[1].Codec));
    }

    [Fact]
    public void ParseOutput_treats_missing_or_undetermined_language_as_null()
    {
        var json = """{"streams":[{"index":1,"codec_name":"aac"},{"index":2,"codec_name":"aac","tags":{"language":"und"}}]}""";

        var tracks = FfprobeAudioTrackProbe.ParseOutput(json);

        Assert.All(tracks, track => Assert.Null(track.Language));
    }

    [Theory]
    [InlineData("""{"streams":[]}""")]
    [InlineData("""{"streams":[{"index":1,"codec_name":"aac"}]}""")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("")]
    public void ParseOutput_returns_empty_for_zero_one_or_malformed(string json) =>
        Assert.Empty(FfprobeAudioTrackProbe.ParseOutput(json));

    [Fact]
    public async Task GetAudioTracksAsync_returns_empty_for_missing_file()
    {
        var tracks = await new FfprobeAudioTrackProbe().GetAudioTracksAsync("/nonexistent/file.mp4", CancellationToken.None);

        Assert.Empty(tracks);
    }
}
