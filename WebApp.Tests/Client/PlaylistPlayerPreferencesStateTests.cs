using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class PlaylistPlayerPreferencesStateTests
{
    [Fact]
    public void Begin_creates_defaults_for_a_new_playlist()
    {
        var state = new PlaylistPlayerPreferencesState();

        state.Begin("videos", "folder-one");

        Assert.True(state.IsActive);
        Assert.Equal("videos", state.Category);
        Assert.Equal("folder-one", state.FolderId);
        Assert.False(state.IsVrPovEnabled);
        Assert.Equal(SaturationState.Default, state.Saturation);
        Assert.Equal(1, state.Volume);
        Assert.False(state.IsMuted);
        Assert.Equal(1, state.PlaybackRate);
        Assert.True(state.IsSubtitlesEnabled);
        Assert.False(state.IsFillTabActive);
    }

    [Fact]
    public void Begin_same_playlist_retains_captured_preferences()
    {
        var state = new PlaylistPlayerPreferencesState();
        state.Begin("videos", "folder-one");
        state.Capture(true, 245, .4, true, 1.25, false, true);

        state.Begin("videos", "folder-one");

        Assert.True(state.IsVrPovEnabled);
        Assert.Equal(245, state.Saturation);
        Assert.Equal(.4, state.Volume);
        Assert.True(state.IsMuted);
        Assert.Equal(1.25, state.PlaybackRate);
        Assert.False(state.IsSubtitlesEnabled);
        Assert.True(state.IsFillTabActive);
    }

    [Fact]
    public void Begin_different_playlist_resets_preferences()
    {
        var state = new PlaylistPlayerPreferencesState();
        state.Begin("videos", "folder-one");
        state.Capture(true, 245, .4, true, 1.25, false, true);

        state.Begin("videos", "folder-two");

        Assert.Equal("folder-two", state.FolderId);
        Assert.False(state.IsVrPovEnabled);
        Assert.Equal(SaturationState.Default, state.Saturation);
        Assert.Equal(1, state.Volume);
        Assert.False(state.IsMuted);
        Assert.Equal(1, state.PlaybackRate);
        Assert.True(state.IsSubtitlesEnabled);
        Assert.False(state.IsFillTabActive);
    }

    [Fact]
    public void Capture_clamps_invalid_transferable_values()
    {
        var state = new PlaylistPlayerPreferencesState();
        state.Begin("videos", "folder-one");

        state.Capture(true, 500, 2, true, -1, false, true);

        Assert.Equal(SaturationState.Max, state.Saturation);
        Assert.Equal(1, state.Volume);
        Assert.Equal(1, state.PlaybackRate);
    }

    [Fact]
    public void Capture_audio_preferences_preserves_video_only_preferences_for_music_entries()
    {
        var state = new PlaylistPlayerPreferencesState();
        state.Begin("videos", "folder-one");
        state.Capture(true, 245, .4, false, 1.25, false, true);

        state.CaptureAudioPreferences(.8, true, 1.5, false);

        Assert.True(state.IsVrPovEnabled);
        Assert.Equal(245, state.Saturation);
        Assert.False(state.IsSubtitlesEnabled);
        Assert.Equal(.8, state.Volume);
        Assert.True(state.IsMuted);
        Assert.Equal(1.5, state.PlaybackRate);
        Assert.False(state.IsFillTabActive);
    }

    [Fact]
    public void Clear_removes_context_and_preferences()
    {
        var state = new PlaylistPlayerPreferencesState();
        state.Begin("videos", "folder-one");
        state.Capture(true, 245, .4, true, 1.25, false, true);

        state.Clear();

        Assert.False(state.IsActive);
        Assert.Null(state.Category);
        Assert.Null(state.FolderId);
        Assert.False(state.IsVrPovEnabled);
        Assert.Equal(SaturationState.Default, state.Saturation);
    }
}
