using System.Collections.Concurrent;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class VideoMetadataCoordinator(IVideoDurationProbe durationProbe, IVideoResolutionProbe resolutionProbe,
    IVideoAudioTrackProbe audioTrackProbe)
{
    private readonly ConcurrentDictionary<string, Task<VideoMetadata>> _cache = new(StringComparer.Ordinal);

    public async Task<VideoMetadata> GetOrComputeAsync(VideoFileEntry entry, CancellationToken cancellationToken)
    {
        var key = ComputeKey(entry);
        var task = _cache.GetOrAdd(key, _ => ProbeAsync(entry, cancellationToken));
        try
        {
            return await task;
        }
        catch
        {
            _cache.TryRemove(key, out _);
            throw;
        }
    }

    private static string ComputeKey(VideoFileEntry entry) =>
        string.Join('|', entry.RelativePath.Replace('\\', '/'), entry.SizeBytes, entry.LastWriteTimeUtc.Ticks);

    private async Task<VideoMetadata> ProbeAsync(VideoFileEntry entry, CancellationToken cancellationToken)
    {
        var durationTask = durationProbe.GetDurationAsync(entry.PhysicalPath, cancellationToken);
        var resolutionTask = resolutionProbe.GetResolutionAsync(entry.PhysicalPath, cancellationToken);
        var audioTrackTask = audioTrackProbe.GetAudioTracksAsync(entry.PhysicalPath, cancellationToken);
        await Task.WhenAll(durationTask, resolutionTask, audioTrackTask);

        var (width, height) = resolutionTask.Result;
        return new VideoMetadata(durationTask.Result, width, height, audioTrackTask.Result);
    }
}
