using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfmpegCutGeneratorTests
{
    [Fact]
    public void Arguments_keep_source_and_destination_as_separate_list_entries_and_stream_copy()
    {
        const string sourceWithSpacesAndMetacharacters = "/videos/my clip; rm -rf ~ && echo $(whoami).mp4";
        const string destination = "/videos-cuts/output.tmp.mp4";

        var arguments = FfmpegCutGenerator.BuildArguments(
            sourceWithSpacesAndMetacharacters, destination, TimeSpan.FromSeconds(2.5), TimeSpan.FromSeconds(8));

        Assert.Contains(sourceWithSpacesAndMetacharacters, arguments);
        Assert.Contains(destination, arguments);
        Assert.Contains("-c", arguments);
        Assert.Contains("copy", arguments);
        Assert.Equal("-i", arguments[Array.IndexOf(arguments.ToArray(), sourceWithSpacesAndMetacharacters) - 1]);
        Assert.Equal("-ss", arguments[Array.IndexOf(arguments.ToArray(), "2.5") - 1]);
        Assert.Equal("-to", arguments[Array.IndexOf(arguments.ToArray(), "8") - 1]);
    }

    [Fact]
    public async Task Generate_rejects_a_source_that_changed_before_generation()
    {
        using var videos = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        var sourcePath = Path.Combine(videos.Path, "Jennifer White - Clip One.mp4");
        await File.WriteAllBytesAsync(sourcePath, [1, 2, 3, 4]);
        var entry = CreateEntry(sourcePath) with { SizeBytes = 999 };
        var generator = CreateGenerator(videos.Path, cuts.Path);

        var result = await generator.GenerateAsync(
            new CutJob(Guid.NewGuid().ToString("N"), entry, TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        Assert.Equal(CutGenerationStatus.Failed, result.Status);
        Assert.Empty(Directory.EnumerateFiles(cuts.Path));
    }

    [Fact]
    public void Temporary_path_is_unique_and_derived_from_destination()
    {
        const string destination = "/videos-cuts/Jennifer White 0001.mp4";

        var first = FfmpegCutGenerator.BuildTemporaryPath(destination);
        var second = FfmpegCutGenerator.BuildTemporaryPath(destination);

        Assert.NotEqual(first, second);
        Assert.StartsWith("/videos-cuts/Jennifer White 0001.", first);
        Assert.EndsWith(".tmp.mp4", first);
    }

    private static FfmpegCutGenerator CreateGenerator(string videoRoot, string cutRoot) =>
        new(
            Options.Create(new VideoLibraryOptions { Path = videoRoot }),
            Options.Create(new VideoCutOptions { Path = cutRoot }),
            new CutNamingService(Options.Create(new VideoCutOptions { Path = cutRoot }), JobTestDb.Shared.Counters));

    private static VideoFileEntry CreateEntry(string physicalPath)
    {
        var file = new FileInfo(physicalPath);
        return new VideoFileEntry(
            Guid.NewGuid().ToString("N"),
            physicalPath,
            Path.GetFileName(physicalPath),
            Path.GetFileName(physicalPath),
            Path.GetExtension(physicalPath),
            file.Exists ? file.Length : 0,
            file.Exists ? file.LastWriteTimeUtc : DateTime.UtcNow);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-cut-generator-{Guid.NewGuid():N}");
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
