using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class EpubHighlightServiceTests
{
    private static readonly DateTime LastWriteTimeUtc = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Save_then_load_is_version_scoped_and_atomic()
    {
        using var root = new TemporaryDirectory();
        var service = new EpubHighlightService(Options.Create(new ArchiveRootOptions { Path = root.Path }));
        var saved = new BookHighlightDto("highlight-1", "0", 4, 12, "selected", "before", "after", DateTimeOffset.UtcNow);
        await service.SaveHighlightAsync("books", "item-1", 1024, LastWriteTimeUtc, saved, CancellationToken.None);
        Assert.Equal(saved, Assert.Single(await service.LoadHighlightsAsync("books", "item-1", 1024, LastWriteTimeUtc, CancellationToken.None)));
        Assert.Empty(await service.LoadHighlightsAsync("books", "item-1", 2048, LastWriteTimeUtc, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(Path.Combine(root.Path, "Books", "Notes"), "*.tmp"));
    }

    [Fact]
    public async Task Malformed_data_fails_closed_and_concurrent_writes_are_retained()
    {
        using var root = new TemporaryDirectory();
        var folder = Path.Combine(root.Path, "Books", "Notes");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "pereneArchiveBookHighlights.json"), "not json");
        var service = new EpubHighlightService(Options.Create(new ArchiveRootOptions { Path = root.Path }));
        Assert.Empty(await service.LoadHighlightsAsync("books", "item-1", 1, LastWriteTimeUtc, CancellationToken.None));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(index => service.SaveHighlightAsync("books", "item-1", 1, LastWriteTimeUtc,
            new BookHighlightDto(index.ToString(), "0", index, index + 1, "x", "", "", DateTimeOffset.UtcNow), CancellationToken.None)));
        Assert.Equal(10, (await service.LoadHighlightsAsync("books", "item-1", 1, LastWriteTimeUtc, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task RemoveHighlightAsync_removes_only_the_requested_highlight()
    {
        using var root = new TemporaryDirectory();
        var service = new EpubHighlightService(Options.Create(new ArchiveRootOptions { Path = root.Path }));
        var first = new BookHighlightDto("highlight-1", "0", 4, 12, "first", "", "", DateTimeOffset.UtcNow);
        var second = new BookHighlightDto("highlight-2", "0", 20, 28, "second", "", "", DateTimeOffset.UtcNow);
        await service.SaveHighlightAsync("books", "item-1", 1024, LastWriteTimeUtc, first, CancellationToken.None);
        await service.SaveHighlightAsync("books", "item-1", 1024, LastWriteTimeUtc, second, CancellationToken.None);

        Assert.True(await service.RemoveHighlightAsync("books", "item-1", 1024, LastWriteTimeUtc, first.Id, CancellationToken.None));
        Assert.False(await service.RemoveHighlightAsync("books", "item-1", 1024, LastWriteTimeUtc, first.Id, CancellationToken.None));
        Assert.Equal(second, Assert.Single(await service.LoadHighlightsAsync("books", "item-1", 1024, LastWriteTimeUtc, CancellationToken.None)));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-epub-highlight-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
