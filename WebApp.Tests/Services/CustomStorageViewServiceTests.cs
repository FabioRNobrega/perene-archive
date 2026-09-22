using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class CustomStorageViewServiceTests
{
    [Fact]
    public async Task AddAsync_persists_a_record_with_a_generated_id_and_the_given_max_bytes()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var folderId = CreateFolder(archive, "videos", "Clips");

        var views = await service.AddAsync("videos", folderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None);

        var view = Assert.Single(views);
        Assert.Equal("Clips", view.Title);
        Assert.True(view.IsAvailable);
        Assert.Equal(1024, view.MaxBytes);

        var persistedPath = Path.Combine(root.Path, "Dashboard", "pereneArchiveCustomStorageViews.json");
        Assert.True(File.Exists(persistedPath));
        var json = await File.ReadAllTextAsync(persistedPath);
        using var document = JsonDocument.Parse(json);
        var record = Assert.Single(document.RootElement.EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("Id").GetString()));
        Assert.Equal("videos", record.GetProperty("CategoryKey").GetString());
        Assert.Equal(folderId, record.GetProperty("FolderId").GetString());
        Assert.Equal(1024, record.GetProperty("MaxSizeBytes").GetInt64());
    }

    [Fact]
    public async Task AddAsync_with_an_unresolvable_folder_throws_and_persists_nothing()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(
            () => service.AddAsync("videos", "not-a-real-id", isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None));

        var persistedPath = Path.Combine(root.Path, "Dashboard", "pereneArchiveCustomStorageViews.json");
        Assert.False(File.Exists(persistedPath));
    }

    [Fact]
    public async Task GetAllAsync_computes_used_bytes_as_the_recursive_sum_excluding_symlinks()
    {
        using var root = CreateArchive();
        using var outside = new TemporaryDirectory();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var folderId = CreateFolder(archive, "videos", "Clips");

        var clipsPath = Path.Combine(root.Path, "Videos", "Clips");
        Directory.CreateDirectory(Path.Combine(clipsPath, "Nested"));
        await File.WriteAllBytesAsync(Path.Combine(clipsPath, "a.mp4"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(clipsPath, "Nested", "b.mp4"), new byte[50]);
        var outsideFile = Path.Combine(outside.Path, "outside.mp4");
        await File.WriteAllBytesAsync(outsideFile, new byte[999]);
        File.CreateSymbolicLink(Path.Combine(clipsPath, "linked.mp4"), outsideFile);

        await service.AddAsync("videos", folderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None);
        var views = await service.GetAllAsync(CancellationToken.None);

        var view = Assert.Single(views);
        Assert.True(view.IsAvailable);
        Assert.Equal(150, view.UsedBytes);
    }

    [Fact]
    public async Task GetAllAsync_marks_a_view_unavailable_when_its_folder_no_longer_resolves()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var keepFolderId = CreateFolder(archive, "videos", "Keep");
        var removeFolderId = CreateFolder(archive, "videos", "Remove");

        await service.AddAsync("videos", keepFolderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None);
        await service.AddAsync("videos", removeFolderId, isWholeArchive: false, maxSizeBytes: 2048, CancellationToken.None);
        Directory.Delete(Path.Combine(root.Path, "Videos", "Remove"), recursive: true);

        var views = await service.GetAllAsync(CancellationToken.None);

        Assert.Equal(2, views.Count);
        var keepView = views.Single(view => view.Title == "Keep");
        var removeView = views.Single(view => view.Title == "Remove");
        Assert.True(keepView.IsAvailable);
        Assert.False(removeView.IsAvailable);
    }

    [Fact]
    public async Task RemoveAsync_removes_only_the_targeted_view()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var firstFolderId = CreateFolder(archive, "videos", "First");
        var secondFolderId = CreateFolder(archive, "videos", "Second");
        await service.AddAsync("videos", firstFolderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None);
        var afterAdd = await service.AddAsync("videos", secondFolderId, isWholeArchive: false, maxSizeBytes: 2048, CancellationToken.None);
        var firstViewId = afterAdd.Single(view => view.Title == "First").Id;

        var remaining = await service.RemoveAsync(firstViewId, CancellationToken.None);

        Assert.NotNull(remaining);
        var view = Assert.Single(remaining!);
        Assert.Equal("Second", view.Title);
    }

    [Fact]
    public async Task RemoveAsync_returns_null_for_an_unknown_view_id()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path, CreateArchiveService(root.Path));

        var result = await service.RemoveAsync("does-not-exist", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateMaxSizeAsync_changes_only_the_max_size_of_the_targeted_view()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var folderId = CreateFolder(archive, "videos", "Clips");
        var added = await service.AddAsync("videos", folderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None);
        var viewId = added.Single().Id;

        var updated = await service.UpdateMaxSizeAsync(viewId, 4096, CancellationToken.None);

        Assert.NotNull(updated);
        var view = Assert.Single(updated!);
        Assert.Equal(4096, view.MaxBytes);
        Assert.Equal("Clips", view.Title);
    }

    [Fact]
    public async Task UpdateMaxSizeAsync_returns_null_for_an_unknown_view_id()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path, CreateArchiveService(root.Path));

        var result = await service.UpdateMaxSizeAsync("does-not-exist", 4096, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Concurrent_writes_never_corrupt_the_persisted_file()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateService(root.Path, archive);
        var folderIds = Enumerable.Range(0, 10)
            .Select(index => CreateFolder(archive, "videos", $"Folder{index}"))
            .ToList();

        await Task.WhenAll(folderIds.Select(folderId =>
            service.AddAsync("videos", folderId, isWholeArchive: false, maxSizeBytes: 1024, CancellationToken.None)));

        var views = await service.GetAllAsync(CancellationToken.None);
        Assert.Equal(10, views.Count);

        var persistedPath = Path.Combine(root.Path, "Dashboard", "pereneArchiveCustomStorageViews.json");
        var json = await File.ReadAllTextAsync(persistedPath);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(10, document.RootElement.GetArrayLength());
    }

    private static ArchiveService CreateArchiveService(string path) =>
        new(Options.Create(new ArchiveRootOptions { Path = path }));

    private static CustomStorageViewService CreateService(string path, ArchiveService archive) =>
        new(archive, Options.Create(new ArchiveRootOptions { Path = path }));

    private static string CreateFolder(ArchiveService archive, string categoryKey, string name)
    {
        var listing = archive.CreateFolder(categoryKey, null, name);
        return listing.Items.Single(item => item.Name == name).Id;
    }

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        return root;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"custom-storage-view-{Guid.NewGuid():N}");
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
