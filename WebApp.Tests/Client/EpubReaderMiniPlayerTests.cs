namespace WebApp.Tests.Client;

public sealed class EpubReaderMiniPlayerTests
{
    [Fact]
    public void Mini_player_exposes_accessible_controls_and_uses_persistent_commands()
    {
        var componentPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/EpubReaderMiniPlayer.razor"));
        var component = File.ReadAllText(componentPath);

        Assert.Contains("aria-label=\"Previous track\"", component);
        Assert.Contains("aria-label=\"Stop\"", component);
        Assert.Contains("aria-label=\"Next track\"", component);
        Assert.Contains("PersistentPlayerCommand.TogglePlayback", component);
        Assert.Contains("PersistentPlayerCommand.Stop", component);
        Assert.Contains("PersistentPlayerCommand.SelectPreviousTrack", component);
        Assert.Contains("PersistentPlayerCommand.SelectNextTrack", component);
    }
}
