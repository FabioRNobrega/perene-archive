using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ImageCropServiceTests
{
    [Fact]
    public async Task Unknown_source_id_returns_not_found()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = new ImageCropService(archive, new ImageCropNamingService(), new FakeGenerator(ImageCropGenerationResult.Success()));

        var outcome = await service.CropAsync("photos", "unknown-id", 0, 0, 10, 10, CancellationToken.None);

        Assert.Equal(ImageCropOutcomeStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task Cuts_directory_is_created_when_absent_and_result_id_matches_archive_computation()
    {
        using var root = CreateArchive();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", "Beach Sunset.jpg"), [1, 2, 3]);
        var archive = CreateArchiveService(root.Path);
        var item = archive.List("photos", null).Items.Single();
        var service = new ImageCropService(archive, new ImageCropNamingService(), new FakeGenerator(ImageCropGenerationResult.Success()));
        var cutsPath = Path.Combine(root.Path, "Pictures", "cuts");

        Assert.False(Directory.Exists(cutsPath));

        var outcome = await service.CropAsync("photos", item.Id, 0, 0, 1, 1, CancellationToken.None);

        Assert.Equal(ImageCropOutcomeStatus.Success, outcome.Status);
        Assert.True(Directory.Exists(cutsPath));
        Assert.Equal("Beach Sunset 0001.jpg", outcome.Name);
        Assert.Equal(archive.ComputeItemId("photos", Path.Combine(cutsPath, "Beach Sunset 0001.jpg")), outcome.Id);
    }

    [Fact]
    public async Task Generator_out_of_bounds_result_is_surfaced_as_out_of_bounds_outcome()
    {
        using var root = CreateArchive();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", "photo.jpg"), [1, 2, 3]);
        var archive = CreateArchiveService(root.Path);
        var item = archive.List("photos", null).Items.Single();
        var service = new ImageCropService(
            archive, new ImageCropNamingService(), new FakeGenerator(ImageCropGenerationResult.OutOfBounds("bad region")));

        var outcome = await service.CropAsync("photos", item.Id, 0, 0, 999, 999, CancellationToken.None);

        Assert.Equal(ImageCropOutcomeStatus.OutOfBounds, outcome.Status);
    }

    [Theory]
    [InlineData("animation.gif")]
    [InlineData("photo.webp")]
    [InlineData("photo.avif")]
    [InlineData("photo.bmp")]
    [InlineData("favicon.ico")]
    public async Task Added_raster_image_formats_are_not_sent_to_the_crop_generator(string fileName)
    {
        using var root = CreateArchive();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", fileName), [1, 2, 3]);
        var archive = CreateArchiveService(root.Path);
        var item = Assert.Single(archive.List("photos", null).Items);
        var generator = new FakeGenerator(ImageCropGenerationResult.Success());
        var service = new ImageCropService(archive, new ImageCropNamingService(), generator);

        var outcome = await service.CropAsync("photos", item.Id, 0, 0, 1, 1, CancellationToken.None);

        Assert.Equal(ImageCropOutcomeStatus.NotFound, outcome.Status);
        Assert.False(generator.WasCalled);
    }

    private static ArchiveService CreateArchiveService(string path) =>
        new(Options.Create(new ArchiveRootOptions { Path = path }));

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        return root;
    }

    private sealed class FakeGenerator(ImageCropGenerationResult result) : IImageCropGenerator
    {
        public bool WasCalled { get; private set; }

        public Task<ImageCropGenerationResult> CropAsync(
            string sourcePath, string destinationPath, int x, int y, int width, int height, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(result);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-image-crop-service-{Guid.NewGuid():N}");
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
