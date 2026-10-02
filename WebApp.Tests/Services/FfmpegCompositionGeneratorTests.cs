using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FfmpegCompositionGeneratorTests
{
    [Fact]
    public void Normalize_arguments_keep_source_and_temp_paths_as_separate_list_entries()
    {
        const string sourceWithSpacesAndMetacharacters = "/videos-cuts/my clip; rm -rf ~ && echo $(whoami).mp4";
        const string temporaryPath = "/videos-composition/output.part0000.tmp.mp4";
        var plan = new CompositionFormatPlan(640, 360, 30, 48000, 2);
        var fade = new CompositionFadePlan(TimeSpan.Zero, TimeSpan.FromSeconds(5), null, null);

        var arguments = FfmpegCompositionGenerator.BuildNormalizeArguments(
            sourceWithSpacesAndMetacharacters, temporaryPath, plan, fade);

        Assert.Contains(sourceWithSpacesAndMetacharacters, arguments);
        Assert.Contains(temporaryPath, arguments);
        Assert.Contains("-c:v", arguments);
        Assert.Contains("libx264", arguments);
        Assert.Contains("-c:a", arguments);
        Assert.Contains("aac", arguments);
        var videoFilterIndex = arguments.ToList().IndexOf("-vf") + 1;
        Assert.Contains("scale=640:360", arguments[videoFilterIndex]);
        Assert.Contains("fade=t=in:st=0:d=5", arguments[videoFilterIndex]);
        var audioFilterIndex = arguments.ToList().IndexOf("-af") + 1;
        Assert.Contains("afade=t=in:st=0:d=5", arguments[audioFilterIndex]);
    }

    [Fact]
    public void Normalize_arguments_never_scale_a_dimension_above_the_target_canvas()
    {
        var plan = new CompositionFormatPlan(320, 180, 24, 48000, 2);
        var fade = new CompositionFadePlan(null, null, null, null);

        var arguments = FfmpegCompositionGenerator.BuildNormalizeArguments("/in.mp4", "/out.mp4", plan, fade);

        var videoFilterIndex = arguments.ToList().IndexOf("-vf") + 1;
        Assert.Contains("scale=320:180:force_original_aspect_ratio=decrease", arguments[videoFilterIndex]);
        Assert.Contains("pad=320:180", arguments[videoFilterIndex]);
    }

    [Fact]
    public void Concat_arguments_stream_copy_the_normalized_file_list()
    {
        var arguments = FfmpegCompositionGenerator.BuildConcatArguments("/videos-composition/list.txt", "/videos-composition/final.mp4");

        Assert.Contains("-f", arguments);
        Assert.Contains("concat", arguments);
        Assert.Contains("-c", arguments);
        Assert.Contains("copy", arguments);
        Assert.Contains("/videos-composition/list.txt", arguments);
        Assert.Contains("/videos-composition/final.mp4", arguments);
    }

    [Fact]
    public void Concat_file_list_escapes_single_quotes_in_paths()
    {
        var list = FfmpegCompositionGenerator.BuildConcatFileList(["/videos-composition/a's clip.mp4", "/videos-composition/b.mp4"]);

        Assert.Contains("a'\\''s clip.mp4", list);
        Assert.Contains("file '/videos-composition/b.mp4'", list);
    }

    [Fact]
    public void Temporary_paths_are_unique_and_derived_from_destination()
    {
        const string destination = "/videos-composition/Jennifer White Composition 0001.mp4";

        var firstNormalized = FfmpegCompositionGenerator.BuildTemporaryNormalizedPath(destination, 0);
        var secondNormalized = FfmpegCompositionGenerator.BuildTemporaryNormalizedPath(destination, 0);
        var finalTemp = FfmpegCompositionGenerator.BuildTemporaryPath(destination);
        var fileList = FfmpegCompositionGenerator.BuildTemporaryFileListPath(destination);

        Assert.NotEqual(firstNormalized, secondNormalized);
        Assert.EndsWith(".tmp.mp4", finalTemp);
        Assert.EndsWith(".filelist.txt", fileList);
        Assert.StartsWith("/videos-composition/Jennifer White Composition 0001.", firstNormalized);
    }

    [Fact]
    public async Task Generate_fails_at_probe_stage_when_a_source_changed_before_generation()
    {
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        var sourceA = Path.Combine(cuts.Path, "Jennifer White 0001.mp4");
        var sourceB = Path.Combine(cuts.Path, "Jennifer White 0002.mp4");
        await File.WriteAllBytesAsync(sourceA, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(sourceB, [1, 2, 3, 4]);

        var entryA = CreateEntry(sourceA) with { SizeBytes = 999 };
        var entryB = CreateEntry(sourceB);
        var generator = CreateGenerator(cuts.Path, composition.Path, new FixedProbe(null));

        var result = await generator.GenerateAsync(
            new CompositionJob(Guid.NewGuid().ToString("N"), [entryA, entryB]), CancellationToken.None);

        Assert.Equal(CompositionGenerationStatus.Failed, result.Status);
        Assert.Equal(CompositionStage.Probe, result.Stage);
        Assert.Empty(Directory.EnumerateFiles(composition.Path));
    }

    [Fact]
    public async Task Generate_fails_at_probe_stage_when_ffprobe_cannot_read_a_source()
    {
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        var sourceA = Path.Combine(cuts.Path, "Jennifer White 0001.mp4");
        var sourceB = Path.Combine(cuts.Path, "Jennifer White 0002.mp4");
        await File.WriteAllBytesAsync(sourceA, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(sourceB, [1, 2, 3, 4]);

        var generator = CreateGenerator(cuts.Path, composition.Path, new FixedProbe(null));

        var result = await generator.GenerateAsync(
            new CompositionJob(Guid.NewGuid().ToString("N"), [CreateEntry(sourceA), CreateEntry(sourceB)]), CancellationToken.None);

        Assert.Equal(CompositionGenerationStatus.Failed, result.Status);
        Assert.Equal(CompositionStage.Probe, result.Stage);
        Assert.DoesNotContain(cuts.Path, result.Diagnostic);
    }

    [Fact]
    public async Task Generate_fails_at_probe_stage_when_a_source_has_no_audio_stream()
    {
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        var sourceA = Path.Combine(cuts.Path, "Jennifer White 0001.mp4");
        var sourceB = Path.Combine(cuts.Path, "Jennifer White 0002.mp4");
        await File.WriteAllBytesAsync(sourceA, [1, 2, 3, 4]);
        await File.WriteAllBytesAsync(sourceB, [1, 2, 3, 4]);

        var silentProbe = new CompositionInputProbe(640, 360, TimeSpan.FromSeconds(10), 30, "h264", null, null, null);
        var generator = CreateGenerator(cuts.Path, composition.Path, new FixedProbe(silentProbe));

        var result = await generator.GenerateAsync(
            new CompositionJob(Guid.NewGuid().ToString("N"), [CreateEntry(sourceA), CreateEntry(sourceB)]), CancellationToken.None);

        Assert.Equal(CompositionGenerationStatus.Failed, result.Status);
        Assert.Equal(CompositionStage.Probe, result.Stage);
        Assert.Contains("no audio stream", result.Diagnostic);
    }

    private static FfmpegCompositionGenerator CreateGenerator(string cutRoot, string compositionRoot, IVideoCompositionProbe probe) =>
        new(
            Options.Create(new VideoCutOptions { Path = cutRoot }),
            Options.Create(new VideoCompositionOptions { Path = compositionRoot, TransitionDurationSeconds = 5 }),
            new CompositionNamingService(Options.Create(new VideoCompositionOptions { Path = compositionRoot }), JobTestDb.Shared.Counters),
            probe);

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

    private sealed class FixedProbe(CompositionInputProbe? probe) : IVideoCompositionProbe
    {
        public Task<CompositionInputProbe?> ProbeAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult(probe);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-composition-generator-{Guid.NewGuid():N}");
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
