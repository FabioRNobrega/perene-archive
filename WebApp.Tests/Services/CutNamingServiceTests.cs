using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class CutNamingServiceTests
{
    [Fact]
    public void Empty_directory_yields_first_counter_for_prefix()
    {
        using var cuts = new TemporaryDirectory();
        var service = CreateService(cuts.Path);

        Assert.Equal(Path.Combine(cuts.Path, "Jennifer White 0001.mp4"),
            service.GetNextPath("Jennifer White - Clip One.mp4"));
    }

    [Fact]
    public async Task Existing_same_prefix_files_increment_case_insensitively()
    {
        using var cuts = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "jennifer white 0003.mp4"), [1]);
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Maria Rodriguez 0099.mp4"), [1]);
        var service = CreateService(cuts.Path);

        Assert.Equal(Path.Combine(cuts.Path, "Jennifer White 0004.mp4"),
            service.GetNextPath("Jennifer White - Clip Two.mp4"));
    }

    [Fact]
    public void Single_word_source_uses_the_single_word_as_prefix()
    {
        using var cuts = new TemporaryDirectory();
        var service = CreateService(cuts.Path);

        Assert.Equal(Path.Combine(cuts.Path, "Mononym 0001.mp4"), service.GetNextPath("Mononym.mp4"));
    }

    private static CutNamingService CreateService(string path) =>
        new(Options.Create(new VideoCutOptions { Path = path }), JobTestDb.Shared.Counters);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-cut-naming-{Guid.NewGuid():N}");
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
