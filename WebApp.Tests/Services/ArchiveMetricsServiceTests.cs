using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveMetricsServiceTests
{
    [Fact]
    public void GetArchiveMetrics_reflects_fake_queue_counts_and_process_list()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "a.mp4"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "b.mp4"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "song.mp3"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "book.epub"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "photo.jpg"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "animation.gif"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "photo.webp"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "photo.avif"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "photo.bmp"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "favicon.ico"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "document.pdf"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "notes.md"), "x");
        File.WriteAllText(Path.Combine(directory.Path, "unknown.bin"), "x");

        var service = new ArchiveMetricsService(
            new FakeArchiveService(directory.Path),
            new FakeActiveClientTracker(3),
            new FakeProcessLister(["ffmpeg", "ffprobe", "chrome"]),
            new FakeThumbnailJobQueue(2),
            new FakeHoverPreviewJobQueue(1),
            new FakeSubtitleJobQueue(0),
            new FakeCutJobQueue(4),
            new FakeCompositionJobQueue(1),
            new FakeArchiveMutationJobQueue(5));

        var metrics = service.GetArchiveMetrics();

        Assert.Equal(3, metrics.ActiveUsers);
        Assert.Equal(2, metrics.ActiveFfmpegProcesses);
        Assert.Equal(2, metrics.ActiveMediaStreams);
        Assert.Equal(2, metrics.QueuedThumbnailJobs);
        Assert.Equal(1, metrics.QueuedHoverPreviewJobs);
        Assert.Equal(0, metrics.QueuedSubtitleJobs);
        Assert.Equal(4, metrics.QueuedCutJobs);
        Assert.Equal(1, metrics.QueuedCompositionJobs);
        Assert.Equal(5, metrics.QueuedArchiveMutationJobs);
        var categoryCount = ArchiveCategory.Defaults.Count;
        Assert.Equal(2 * categoryCount, metrics.VideoFiles);
        Assert.Equal(categoryCount, metrics.AudioFiles);
        Assert.Equal(categoryCount, metrics.EpubFiles);
        Assert.Equal(6 * categoryCount, metrics.ImageFiles);
        Assert.Equal(categoryCount, metrics.PdfFiles);
        Assert.Equal(categoryCount, metrics.TextDocumentFiles);
        Assert.Equal(categoryCount, metrics.OtherFiles);
        Assert.Equal(
            metrics.VideoFiles + metrics.AudioFiles + metrics.EpubFiles + metrics.ImageFiles +
            metrics.PdfFiles + metrics.TextDocumentFiles + metrics.OtherFiles,
            metrics.TotalFiles);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"archive-metrics-{Guid.NewGuid():N}");
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

    private sealed class FakeArchiveService(string rootForAllCategories) : IArchiveService
    {
        public string GetCategoryRootPath(string categoryKey) => rootForAllCategories;

        public ArchiveListing List(string categoryKey, string? folderId) => throw new NotSupportedException();
        public ArchiveListing ListPlaylist(string categoryKey, string? folderId) => throw new NotSupportedException();
        public ArchiveListing CreateFolder(string categoryKey, string? parentId, string name) => throw new NotSupportedException();
        public ArchiveListing CreateFile(string categoryKey, string? parentId, string name, string extension) => throw new NotSupportedException();
        public ArchiveUploadDestination ValidateUploadDestination(string categoryKey, string? parentId, string fileName) => throw new NotSupportedException();
        public ArchiveListing PublishUploadedFile(string categoryKey, string? parentId, string fileName, string sourceTempPath) => throw new NotSupportedException();
        public ArchiveListing Rename(string categoryKey, string itemId, string name) => throw new NotSupportedException();
        public ArchiveMutationJob Move(string categoryKey, string itemId, string destinationCategoryKey, string? destinationFolderId) => throw new NotSupportedException();
        public ArchiveMutationJob MoveToTrash(string categoryKey, string itemId) => throw new NotSupportedException();
        public ArchiveMutationJob EmptyTrash(string categoryKey) => throw new NotSupportedException();
        public bool TryResolveDownloadableItem(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveVideo(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveMusic(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveImage(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveBook(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveTextDocument(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolvePdfDocument(string categoryKey, string itemId, out ArchiveItemEntry? item) => throw new NotSupportedException();
        public bool TryResolveAlbumCover(string categoryKey, string folderId, out ArchiveAlbumCoverInfo? cover) => throw new NotSupportedException();
        public string ComputeItemId(string categoryKey, string physicalPath) => throw new NotSupportedException();
    }

    private sealed class FakeActiveClientTracker(int count) : IActiveClientTracker
    {
        public void Track(string clientId)
        {
        }

        public int CountActive() => count;
    }

    private sealed class FakeProcessLister(IReadOnlyList<string> names) : IProcessLister
    {
        public IReadOnlyList<string> GetRunningProcessNames() => names;
    }

    private sealed class FakeThumbnailJobQueue(int activeCount) : IThumbnailJobQueue
    {
        public bool TryEnqueue(ThumbnailJob job) => true;
        public Task<ThumbnailJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool IsActive(string cacheKey) => false;
        public void Release(string cacheKey)
        {
        }

        public int ActiveCount => activeCount;
    }

    private sealed class FakeHoverPreviewJobQueue(int activeCount) : IHoverPreviewJobQueue
    {
        public bool TryEnqueue(HoverPreviewJob job) => true;
        public Task<HoverPreviewJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool IsActive(string cacheKey) => false;
        public void Release(string cacheKey)
        {
        }

        public int ActiveCount => activeCount;
    }

    private sealed class FakeSubtitleJobQueue(int activeCount) : ISubtitleJobQueue
    {
        public bool TryEnqueue(SubtitleJob job) => true;
        public Task<SubtitleJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool IsActive(string cacheKey) => false;
        public void Release(string cacheKey)
        {
        }

        public int ActiveCount => activeCount;
    }

    private sealed class FakeCutJobQueue(int activeCount) : ICutJobQueue
    {
        public bool TryEnqueue(CutJob job) => true;
        public Task<CutJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Complete()
        {
        }

        public int ActiveCount => activeCount;
    }

    private sealed class FakeCompositionJobQueue(int activeCount) : ICompositionJobQueue
    {
        public bool TryEnqueue(CompositionJob job) => true;
        public Task<CompositionJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Complete()
        {
        }

        public int ActiveCount => activeCount;
    }

    private sealed class FakeArchiveMutationJobQueue(int activeCount) : IArchiveMutationJobQueue
    {
        public bool TryEnqueue(ArchiveMutationJob job) => true;
        public Task<ArchiveMutationJob> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Complete()
        {
        }

        public int ActiveCount => activeCount;
    }
}
