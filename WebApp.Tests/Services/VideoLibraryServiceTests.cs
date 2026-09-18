using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class VideoLibraryServiceTests
{
    [Fact]
    public async Task Scan_discovers_supported_extensions_recursively_and_ignores_others()
    {
        using var root = new TemporaryDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(root.Path, "private-subfolder"));
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "first.MP4"), [1, 2]);
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "second.webm"), [3]);
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "third.MOV"), [4, 5, 6]);
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "fourth.m4v"), [7]);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "ignore.txt"), "not a video");

        var entries = await CreateService(root.Path).ScanAsync();

        Assert.Equal(4, entries.Count);
        Assert.Equal([".m4v", ".mov", ".mp4", ".webm"],
            entries.Select(entry => entry.Extension).OrderBy(value => value));
        Assert.All(entries, entry =>
        {
            Assert.Matches("^[0-9a-f]{32}$", entry.Id);
            Assert.DoesNotContain("private-subfolder", entry.Name);
            Assert.Equal(Path.GetFileName(entry.PhysicalPath), entry.Name);
            Assert.True(entry.SizeBytes > 0);
            Assert.DoesNotContain('\\', entry.RelativePath);
            Assert.Equal(File.GetLastWriteTimeUtc(entry.PhysicalPath), entry.LastWriteTimeUtc);
        });
    }

    [Fact]
    public async Task Identity_metadata_reflects_normalized_relative_path()
    {
        using var root = new TemporaryDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(root.Path, "nested"));
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "clip.mp4"), [1, 2, 3]);

        var entry = Assert.Single(await CreateService(root.Path).ScanAsync());

        Assert.Equal("nested/clip.mp4", entry.RelativePath);
    }

    [Fact]
    public async Task Rescan_preserves_deterministic_ids_for_archive_selection()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var service = CreateService(root.Path);

        var first = Assert.Single(await service.ScanAsync());
        var second = Assert.Single(await service.ScanAsync());

        Assert.Equal(first.Id, second.Id);
        Assert.True(service.TryResolve(second.Id, out var resolved));
        Assert.Equal(second, resolved);
    }

    [Fact]
    public async Task Empty_directory_produces_empty_snapshot()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        Assert.Empty(await service.ScanAsync());
        Assert.False(service.TryResolve(Guid.NewGuid().ToString("N"), out _));
    }

    [Fact]
    public async Task Failed_scan_clears_the_previous_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path, "clip.mp4"), [1]);
            var service = CreateService(path);
            var oldEntry = Assert.Single(await service.ScanAsync());
            Directory.Delete(path, recursive: true);

            await Assert.ThrowsAnyAsync<IOException>(() => service.ScanAsync());
            Assert.False(service.TryResolve(oldEntry.Id, out _));
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Precancelled_scan_clears_the_previous_snapshot()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var service = CreateService(root.Path);
        var oldEntry = Assert.Single(await service.ScanAsync());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ScanAsync(cancellation.Token));
        Assert.False(service.TryResolve(oldEntry.Id, out _));
    }

    [Fact]
    public async Task File_and_directory_symlinks_are_skipped()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var outsideFile = Path.Combine(outside.Path, "outside.mp4");
        await File.WriteAllBytesAsync(outsideFile, [1, 2, 3]);
        File.CreateSymbolicLink(Path.Combine(root.Path, "linked-file.mp4"), outsideFile);
        Directory.CreateSymbolicLink(Path.Combine(root.Path, "linked-directory"), outside.Path);
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "inside.mp4"), [4]);

        var entries = await CreateService(root.Path).ScanAsync();

        Assert.Equal("inside.mp4", Assert.Single(entries).Name);
    }

    [Fact]
    public void Containment_is_path_separator_aware_for_sibling_prefixes()
    {
        using var parent = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(parent.Path, "root")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(parent.Path, "root-other")).FullName;

        Assert.True(VideoLibraryService.IsWithinRoot(root, Path.Combine(root, "clip.mp4")));
        Assert.False(VideoLibraryService.IsWithinRoot(root, Path.Combine(sibling, "clip.mp4")));
    }

    [Fact]
    public async Task Resolve_remains_safe_during_concurrent_rescans()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var service = CreateService(root.Path);
        var first = Assert.Single(await service.ScanAsync());

        var scans = Enumerable.Range(0, 8).Select(_ => service.ScanAsync()).ToArray();
        for (var index = 0; index < 100; index++)
        {
            _ = service.TryResolve(first.Id, out _);
        }

        await Task.WhenAll(scans);
        Assert.True(service.TryResolve(first.Id, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/videos")]
    public void Configuration_rejects_missing_or_relative_paths(string path)
    {
        var options = new VideoLibraryOptions { Path = path };

        Assert.False(VideoLibraryOptions.HasConfiguredPath(options) && VideoLibraryOptions.HasAbsolutePath(options));
    }

    [Fact]
    public async Task GetCurrentSnapshot_returns_scanned_entries_without_rescanning()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var service = CreateService(root.Path);

        Assert.Empty(service.GetCurrentSnapshot());

        var scanned = await service.ScanAsync();
        File.Delete(Path.Combine(root.Path, "clip.mp4"));

        Assert.Equal(scanned, service.GetCurrentSnapshot());
    }

    [Fact]
    public async Task ResolveAsync_returns_snapshot_entry_without_rescanning()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var service = CreateService(root.Path);
        var scanned = Assert.Single(await service.ScanAsync());

        File.Delete(Path.Combine(root.Path, "clip.mp4"));
        var resolved = await service.ResolveAsync(scanned.Id);

        Assert.Equal(scanned, resolved);
    }

    [Fact]
    public async Task ResolveAsync_scans_once_when_id_is_not_yet_known()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1]);
        var expectedId = Assert.Single(await CreateService(root.Path).ScanAsync()).Id;
        var service = CreateService(root.Path);

        Assert.Empty(service.GetCurrentSnapshot());
        var resolved = await service.ResolveAsync(expectedId);

        Assert.NotNull(resolved);
        Assert.Equal(expectedId, resolved!.Id);
        Assert.NotEmpty(service.GetCurrentSnapshot());
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_unknown_id_after_single_scan_attempt()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        var resolved = await service.ResolveAsync(Guid.NewGuid().ToString("N"));

        Assert.Null(resolved);
    }

    private static readonly string SharedPreviewPath = CreateSharedPreviewDirectory();

    private static string CreateSharedPreviewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-tests-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static VideoLibraryService CreateService(string path) =>
        new(
            Options.Create(new VideoLibraryOptions { Path = path }),
            CreateThumbnailCoordinator(),
            CreateHoverPreviewCoordinator());

    private static ThumbnailCoordinator CreateThumbnailCoordinator()
    {
        var options = Options.Create(new ThumbnailCacheOptions { Path = SharedPreviewPath, QueueCapacity = 64 });
        return new ThumbnailCoordinator(new ThumbnailCache(options), new ThumbnailJobQueue(options));
    }

    private static HoverPreviewCoordinator CreateHoverPreviewCoordinator()
    {
        var thumbnailOptions = Options.Create(new ThumbnailCacheOptions { Path = SharedPreviewPath, QueueCapacity = 64 });
        var thumbnailCoordinator = new ThumbnailCoordinator(new ThumbnailCache(thumbnailOptions), new ThumbnailJobQueue(thumbnailOptions));
        var hoverOptions = Options.Create(new HoverPreviewOptions { QueueCapacity = 64 });
        return new HoverPreviewCoordinator(
            new HoverPreviewCache(thumbnailOptions), new HoverPreviewJobQueue(hoverOptions), thumbnailCoordinator, hoverOptions);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
