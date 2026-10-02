using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class CompositionNamingServiceTests
{
    [Fact]
    public void Empty_directory_yields_first_counter_for_prefix()
    {
        using var compositions = new TemporaryDirectory();
        var service = CreateService(compositions.Path);

        Assert.Equal(Path.Combine(compositions.Path, "Jennifer White Composition 0001.mp4"),
            service.GetNextPath("Jennifer White 0001.mp4"));
    }

    [Fact]
    public async Task Existing_same_prefix_files_increment_case_insensitively()
    {
        using var compositions = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(compositions.Path, "jennifer white composition 0003.mp4"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(compositions.Path, "Maria Rodriguez Composition 0099.mp4"), [1]);
        var service = CreateService(compositions.Path);

        Assert.Equal(Path.Combine(compositions.Path, "Jennifer White Composition 0004.mp4"),
            service.GetNextPath("Jennifer White 0002.mp4"));
    }

    [Fact]
    public void A_plain_cut_file_with_the_same_prefix_does_not_affect_the_composition_counter()
    {
        using var compositions = new TemporaryDirectory();
        var service = CreateService(compositions.Path);

        Assert.Equal(Path.Combine(compositions.Path, "Jennifer White Composition 0001.mp4"),
            service.GetNextPath("Jennifer White 0007.mp4"));
    }

    private static CompositionNamingService CreateService(string path) =>
        new(Options.Create(new VideoCompositionOptions { Path = path }), JobTestDb.Shared.Counters);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-composition-naming-{Guid.NewGuid():N}");
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
