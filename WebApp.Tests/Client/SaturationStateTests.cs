using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class SaturationStateTests
{
    [Fact]
    public void Starts_at_default_value()
    {
        var state = new SaturationState();

        Assert.Equal(100, state.Value);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(500, 300)]
    [InlineData(220, 220)]
    public void SetValue_is_clamped_between_zero_and_max(double value, double expected)
    {
        var state = new SaturationState();

        state.SetValue(value);

        Assert.Equal(expected, state.Value);
    }

    [Fact]
    public void Changing_selection_resets_value_to_default()
    {
        var state = new SaturationState();
        state.Select("first");
        state.SetValue(250);
        Assert.Equal(250, state.Value);

        state.Select("second");

        Assert.Equal(100, state.Value);
    }

    [Fact]
    public void Reselecting_the_same_id_does_not_reset_value()
    {
        var state = new SaturationState();
        state.Select("second");
        state.SetValue(275);

        state.Select("second");

        Assert.Equal(275, state.Value);
    }

    [Fact]
    public void Reset_returns_value_to_default()
    {
        var state = new SaturationState();
        state.SetValue(280);

        state.Reset();

        Assert.Equal(100, state.Value);
    }

    [Fact]
    public void Restore_applies_a_clamped_playlist_value_after_selection_changes()
    {
        var state = new SaturationState();
        state.Select("first");
        state.Select("second");

        state.Restore(350);

        Assert.Equal(SaturationState.Max, state.Value);
    }
}
