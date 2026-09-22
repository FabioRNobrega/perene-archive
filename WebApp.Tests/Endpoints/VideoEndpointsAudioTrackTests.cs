using WebApp.Endpoints;
using WebApp.Models;

namespace WebApp.Tests.Endpoints;

public sealed class VideoEndpointsAudioTrackTests
{
    [Fact]
    public void BuildAudioTracks_labels_tracks_with_ordinal_fallback()
    {
        var metadata = new VideoMetadata(null, null, null,
        [
            new AudioTrackProbeResult(0, "jpn", "aac"),
            new AudioTrackProbeResult(1, null, "aac"),
        ]);

        var tracks = VideoEndpoints.BuildAudioTracks(metadata);

        Assert.NotNull(tracks);
        Assert.Equal("Track 1 (jpn)", tracks[0].Label);
        Assert.Equal("Track 2 (language unknown)", tracks[1].Label);
    }

    [Fact]
    public void BuildAudioTracks_is_null_for_single_or_missing_tracks()
    {
        Assert.Null(VideoEndpoints.BuildAudioTracks(new VideoMetadata(null, null, null)));
        Assert.Null(VideoEndpoints.BuildAudioTracks(new VideoMetadata(null, null, null, [new AudioTrackProbeResult(0, "eng", "aac")])));
    }
}
