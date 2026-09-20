using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ComicProgressServiceTests
{
    private static readonly DateTime LastWriteTimeUtc = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LoadProgressAsync_returns_null_when_missing_or_malformed()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);
        Assert.Null(await service.LoadProgressAsync("books", "comic", 1, LastWriteTimeUtc, CancellationToken.None));

        var notes = Path.Combine(root.Path, "Books", "Notes");
        Directory.CreateDirectory(notes);
        await File.WriteAllTextAsync(Path.Combine(notes, "pereneArchiveComicProgress.json"), "not json");
        Assert.Null(await service.LoadProgressAsync("books", "comic", 1, LastWriteTimeUtc, CancellationToken.None));
    }

    [Fact]
    public async Task SaveProgressAsync_round_trips_and_overwrites_without_temporary_files()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);
        await service.SaveProgressAsync("books", "comic", 1, LastWriteTimeUtc, new ComicProgressDto(2), CancellationToken.None);
        await service.SaveProgressAsync("books", "comic", 1, LastWriteTimeUtc, new ComicProgressDto(7), CancellationToken.None);

        Assert.Equal(7, (await service.LoadProgressAsync("books", "comic", 1, LastWriteTimeUtc, CancellationToken.None))!.PageIndex);
        var notes = Path.Combine(root.Path, "Books", "Notes");
        Assert.Empty(Directory.GetFiles(notes, "*.tmp"));
        var json = await File.ReadAllTextAsync(Path.Combine(notes, "pereneArchiveComicProgress.json"));
        Assert.DoesNotContain(root.Path, json);
        Assert.DoesNotContain("comic", json);
    }

    [Fact]
    public async Task Different_versions_have_isolated_progress()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);
        await service.SaveProgressAsync("books", "comic", 1, LastWriteTimeUtc, new ComicProgressDto(2), CancellationToken.None);
        await service.SaveProgressAsync("books", "comic", 2, LastWriteTimeUtc, new ComicProgressDto(5), CancellationToken.None);

        Assert.Equal(2, (await service.LoadProgressAsync("books", "comic", 1, LastWriteTimeUtc, CancellationToken.None))!.PageIndex);
        Assert.Equal(5, (await service.LoadProgressAsync("books", "comic", 2, LastWriteTimeUtc, CancellationToken.None))!.PageIndex);
    }

    private static ComicProgressService CreateService(string path) => new(Options.Create(new ArchiveRootOptions { Path = path }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comic-progress-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
