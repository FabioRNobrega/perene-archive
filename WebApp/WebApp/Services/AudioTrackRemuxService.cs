using System.Collections.Concurrent;
using WebApp.Models;

namespace WebApp.Services;

internal enum AudioTrackRemuxState
{
    Pending,
    Ready,
    Failed,
}

/// <summary>
/// Prepares cached stream-copy MP4s containing a single selected audio track. Jobs are user-initiated,
/// de-duplicated by cache key, and run one at a time.
/// </summary>
internal sealed class AudioTrackRemuxService(
    AudioTrackCache cache,
    IAudioTrackRemuxer remuxer,
    ILogger<AudioTrackRemuxService> logger)
{
    private readonly ConcurrentDictionary<string, Task> _active = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _failedKeys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string GetFinalPath(VideoFileEntry entry, int trackIndex) =>
        cache.GetFinalPath(cache.ComputeKey(entry, trackIndex));

    public bool IsReady(VideoFileEntry entry, int trackIndex) =>
        cache.IsReady(cache.ComputeKey(entry, trackIndex));

    public AudioTrackRemuxState Ensure(VideoFileEntry entry, int trackIndex)
    {
        var key = cache.ComputeKey(entry, trackIndex);
        if (cache.IsReady(key))
        {
            return AudioTrackRemuxState.Ready;
        }

        if (_failedKeys.ContainsKey(key))
        {
            return AudioTrackRemuxState.Failed;
        }

        _active.GetOrAdd(key, _ => Task.Run(() => RunAsync(key, entry, trackIndex)));
        return AudioTrackRemuxState.Pending;
    }

    private async Task RunAsync(string key, VideoFileEntry entry, int trackIndex)
    {
        var keyPrefix = key[..Math.Min(12, key.Length)];
        await _gate.WaitAsync();
        try
        {
            if (cache.IsReady(key))
            {
                return;
            }

            logger.LogInformation("Audio track remux started for media {MediaId} track {Track} (key {KeyPrefix}).", entry.Id, trackIndex, keyPrefix);
            var result = await remuxer.RemuxAsync(
                entry, trackIndex, cache.GetTemporaryPath(key), cache.GetFinalPath(key), CancellationToken.None);
            if (!result.Succeeded)
            {
                _failedKeys[key] = true;
                logger.LogWarning(
                    "Audio track remux failed for media {MediaId} track {Track} (key {KeyPrefix}): {Diagnostic}",
                    entry.Id, trackIndex, keyPrefix, result.Diagnostic);
            }
        }
        catch (Exception exception)
        {
            _failedKeys[key] = true;
            logger.LogError(exception, "Audio track remux threw for media {MediaId} track {Track} (key {KeyPrefix}).", entry.Id, trackIndex, keyPrefix);
        }
        finally
        {
            _gate.Release();
            _active.TryRemove(key, out _);
        }
    }
}
