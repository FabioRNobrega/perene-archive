using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class MediaPlayerStateTests
{
    [Fact]
    public void Subtitles_default_to_enabled()
    {
        var state = new MediaPlayerState();

        Assert.True(state.IsSubtitlesEnabled);
    }

    [Fact]
    public void Select_resets_subtitles_to_enabled_for_new_selection()
    {
        var state = new MediaPlayerState();
        state.Select("one");
        state.SetSubtitlesEnabled(false);

        state.Select("two");

        Assert.True(state.IsSubtitlesEnabled);
    }

    [Fact]
    public void Set_subtitles_enabled_does_not_change_unrelated_state()
    {
        var state = new MediaPlayerState();
        state.Synchronize(new MediaSnapshot(12, 60, 0.4, true, 1.25, false, true, false));

        state.SetSubtitlesEnabled(false);

        Assert.False(state.IsSubtitlesEnabled);
        Assert.True(state.IsMuted);
        Assert.Equal(0.4, state.Volume);
        Assert.Equal(1.25, state.PlaybackRate);
        Assert.Equal(12, state.CurrentTime);
    }

    [Fact]
    public void Restore_playback_preferences_keeps_selection_specific_state_reset()
    {
        var state = new MediaPlayerState();
        state.Select("one");
        state.Synchronize(new MediaSnapshot(20, 60, .4, true, 1.25, true, false, false));
        state.SetMarkerA();
        state.Select("two");

        state.RestorePlaybackPreferences(.4, true, 1.25, false);

        Assert.Equal(.4, state.Volume);
        Assert.True(state.IsMuted);
        Assert.Equal(1.25, state.PlaybackRate);
        Assert.False(state.IsSubtitlesEnabled);
        Assert.False(state.IsStandardLoop);
        Assert.False(state.IsAbLoop);
        Assert.Null(state.MarkerA);
        Assert.Null(state.MarkerB);
        Assert.Null(state.SelectedAudioTrackIndex);
        Assert.Equal(0, state.CurrentTime);
    }
}
