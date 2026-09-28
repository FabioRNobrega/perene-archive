namespace WebApp.Tests.Client;

public sealed class EpubReaderPaginationInteropTests
{
    [Fact]
    public void Content_resize_is_wired_to_pagination_reflow_and_observer_cleanup()
    {
        var componentPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/EpubReader.razor"));
        var component = File.ReadAllText(componentPath);

        Assert.Contains("registerPaginationResizeObserver", component);
        Assert.Contains("unregisterPaginationResizeObserver", component);
        Assert.Contains("OnReaderContentResizedAsync()", component);
        Assert.Contains("RequestPaginationReflow();", component);
    }

    [Fact]
    public void Mini_player_is_wired_to_reader_state_and_dismissal()
    {
        var componentPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/EpubReader.razor"));
        var component = File.ReadAllText(componentPath);

        Assert.Contains("@inject PersistentPlayerState PlayerState", component);
        Assert.Contains("<EpubReaderMiniPlayer Expanded=\"_isMiniPlayerExpanded\" />", component);
        Assert.Contains("@onpointerdown=\"DismissMiniPlayer\"", component);
        Assert.Contains("PlayerState.PlaybackStateChanged += HandlePlayerStateChanged", component);
        Assert.Contains("PlayerState.PlaybackStateChanged -= HandlePlayerStateChanged", component);
    }
}
