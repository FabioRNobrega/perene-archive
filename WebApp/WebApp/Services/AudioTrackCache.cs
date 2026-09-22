using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class AudioTrackCache
{
    private const string VersionMarker = "audiotrackv1";

    private readonly string _rootPath;

    public AudioTrackCache(IOptions<ThumbnailCacheOptions> thumbnailCacheOptions)
    {
        _rootPath = Path.GetFullPath(Path.Combine(thumbnailCacheOptions.Value.Path, "audio-tracks"));
        Directory.CreateDirectory(_rootPath);
    }

    public string ComputeKey(VideoFileEntry entry, int trackIndex)
    {
        var identity = string.Join(
            '|',
            VersionMarker,
            entry.RelativePath.Replace('\\', '/'),
            entry.SizeBytes,
            entry.LastWriteTimeUtc.Ticks,
            trackIndex);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    public string GetFinalPath(string key) => ResolveContained($"{key}.mp4");

    public string GetTemporaryPath(string key) => ResolveContained($"{key}.{Guid.NewGuid():N}.tmp.mp4");

    public bool IsReady(string key)
    {
        try
        {
            var file = new FileInfo(GetFinalPath(key));
            return file.Exists && file.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string ResolveContained(string fileName)
    {
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, fileName));
        if (!VideoLibraryService.IsWithinRoot(_rootPath, candidate))
        {
            throw new InvalidOperationException("Resolved audio-track cache path escaped the configured preview root.");
        }

        return candidate;
    }
}
