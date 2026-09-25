using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class VrPovStateTests
{
    [Fact]
    public void Default_is_inactive()
    {
        Assert.False(new VrPovState().IsActive);
    }

    [Fact]
    public void Toggle_flips_the_active_state()
    {
        var state = new VrPovState();

        Assert.True(state.Toggle());
        Assert.True(state.IsActive);
        Assert.False(state.Toggle());
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Selecting_a_different_video_resets_active_state()
    {
        var state = new VrPovState();
        state.Select("video-one");
        state.Toggle();

        var wasActive = state.Select("video-two");

        Assert.True(wasActive);
        Assert.False(state.IsActive);
    }

    [Fact]
    public void Selecting_the_same_video_is_a_no_op()
    {
        var state = new VrPovState();
        state.Select("video-one");
        state.Toggle();

        var wasActive = state.Select("video-one");

        Assert.False(wasActive);
        Assert.True(state.IsActive);
    }

    [Fact]
    public void Reselecting_an_earlier_video_does_not_remember_pov()
    {
        var state = new VrPovState();
        state.Select("video-one");
        state.Toggle();
        state.Select("video-two");
        state.Select("video-one");

        Assert.False(state.IsActive);
    }

    [Fact]
    public void Restore_reactivates_playlist_intent_after_a_new_video_selection()
    {
        var state = new VrPovState();
        state.Select("video-one");
        state.Toggle();

        state.Select("video-two");
        state.Restore(true);

        Assert.True(state.IsActive);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Button_is_available_only_for_non_music_items(bool isMusic, bool expected)
    {
        Assert.Equal(expected, VrPovState.IsAvailable(isMusic));
    }
}
