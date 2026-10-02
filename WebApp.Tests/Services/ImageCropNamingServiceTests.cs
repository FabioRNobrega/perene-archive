using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ImageCropNamingServiceTests
{
    [Fact]
    public void Empty_directory_yields_first_counter_for_prefix()
    {
        using var cuts = new TemporaryDirectory();
        var service = new ImageCropNamingService(JobTestDb.Shared.Counters);

        Assert.Equal(Path.Combine(cuts.Path, "Beach Sunset 0001.jpg"),
            service.GetNextPath(cuts.Path, "Beach Sunset - Original.jpg", ".jpg"));
    }

    [Fact]
    public async Task Existing_same_prefix_files_increment_case_insensitively()
    {
        using var cuts = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "beach sunset 0003.jpg"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Mountain View 0099.jpg"), [1]);
        var service = new ImageCropNamingService(JobTestDb.Shared.Counters);

        Assert.Equal(Path.Combine(cuts.Path, "Beach Sunset 0004.jpg"),
            service.GetNextPath(cuts.Path, "Beach Sunset - Clip Two.jpg", ".jpg"));
    }

    [Fact]
    public void Single_word_source_uses_the_single_word_as_prefix()
    {
        using var cuts = new TemporaryDirectory();
        var service = new ImageCropNamingService(JobTestDb.Shared.Counters);

        Assert.Equal(Path.Combine(cuts.Path, "Mononym 0001.png"), service.GetNextPath(cuts.Path, "Mononym.png", ".png"));
    }

    [Fact]
    public void Empty_name_falls_back_to_cut_prefix()
    {
        Assert.Equal("Cut", ImageCropNamingService.GetPrefix(string.Empty));
        Assert.Equal("Cut", ImageCropNamingService.GetPrefix(".jpg"));
    }

    [Fact]
    public async Task Counter_scan_is_scoped_to_the_requested_extension()
    {
        using var cuts = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Beach Sunset 0005.png"), [1]);
        var service = new ImageCropNamingService(JobTestDb.Shared.Counters);

        Assert.Equal(Path.Combine(cuts.Path, "Beach Sunset 0001.jpg"),
            service.GetNextPath(cuts.Path, "Beach Sunset.jpg", ".jpg"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-image-crop-naming-{Guid.NewGuid():N}");
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
