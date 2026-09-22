using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class AudioTrackRemuxServiceTests
{
    private static readonly DateTime LastWriteTimeUtc = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Ensure_runs_one_job_for_concurrent_requests_and_becomes_ready()
    {
        using var root = new TemporaryDirectory();
        var remuxer = new FakeRemuxer(succeed: true);
        var service = CreateService(root.Path, remuxer);
        var entry = CreateEntry();

        Assert.Equal(AudioTrackRemuxState.Pending, service.Ensure(entry, 1));
        Assert.Equal(AudioTrackRemuxState.Pending, service.Ensure(entry, 1));
        remuxer.Release();

        await WaitForAsync(() => service.IsReady(entry, 1));
        Assert.Equal(1, remuxer.Calls);
        Assert.Equal(AudioTrackRemuxState.Ready, service.Ensure(entry, 1));
        Assert.Equal(1, remuxer.Calls);
    }

    [Fact]
    public async Task Ensure_remembers_failures_without_retrying()
    {
        using var root = new TemporaryDirectory();
        var remuxer = new FakeRemuxer(succeed: false);
        var service = CreateService(root.Path, remuxer);
        var entry = CreateEntry();

        service.Ensure(entry, 1);
        remuxer.Release();

        await WaitForAsync(() => service.Ensure(entry, 1) == AudioTrackRemuxState.Failed);
        Assert.Equal(1, remuxer.Calls);
    }

    [Fact]
    public void BuildArguments_maps_video_and_selected_audio_with_stream_copy()
    {
        var arguments = FfmpegAudioTrackRemuxer.BuildArguments("/src/movie.mp4", 2, "/tmp/out.mp4");

        Assert.Contains("0:v", arguments);
        Assert.Contains("0:a:2", arguments);
        Assert.Equal("copy", arguments[arguments.ToList().IndexOf("-c") + 1]);
        Assert.Equal("/tmp/out.mp4", arguments[^1]);
    }

    private static VideoFileEntry CreateEntry() =>
        new("id", "/videos/movie.mp4", "movie.mp4", "movie.mp4", ".mp4", 10, LastWriteTimeUtc);

    private static AudioTrackRemuxService CreateService(string previewRoot, IAudioTrackRemuxer remuxer) =>
        new(new AudioTrackCache(Options.Create(new ThumbnailCacheOptions { Path = previewRoot })),
            remuxer,
            NullLogger<AudioTrackRemuxService>.Instance);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition());
    }

    private sealed class FakeRemuxer(bool succeed) : IAudioTrackRemuxer
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Release() => _gate.TrySetResult();

        public async Task<AudioTrackRemuxResult> RemuxAsync(
            VideoFileEntry source, int trackIndex, string temporaryPath, string destinationPath, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            await _gate.Task;
            if (!succeed)
            {
                return AudioTrackRemuxResult.Failed("boom");
            }

            await File.WriteAllBytesAsync(destinationPath, [1, 2, 3], cancellationToken);
            return AudioTrackRemuxResult.Success();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"audio-track-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
