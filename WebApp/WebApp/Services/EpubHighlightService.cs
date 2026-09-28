using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed class EpubHighlightService(IOptions<ArchiveRootOptions> options) : IEpubHighlightService
{
    private const string HighlightsFileName = "pereneArchiveBookHighlights.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<IReadOnlyList<BookHighlightDto>> LoadHighlightsAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, CancellationToken cancellationToken)
    {
        var key = ComputeKey(categoryKey, itemId, sizeBytes, lastWriteTimeUtc);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAllUnlockedAsync(cancellationToken);
            return entries.TryGetValue(key, out var highlights) ? highlights : [];
        }
        finally { _fileLock.Release(); }
    }

    public async Task SaveHighlightAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, BookHighlightDto highlight, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(highlight);
        var key = ComputeKey(categoryKey, itemId, sizeBytes, lastWriteTimeUtc);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAllUnlockedAsync(cancellationToken);
            if (!entries.TryGetValue(key, out var highlights))
            {
                highlights = [];
                entries[key] = highlights;
            }

            highlights.Add(highlight);
            await WriteAllUnlockedAsync(entries, cancellationToken);
        }
        finally { _fileLock.Release(); }
    }

    public async Task<bool> RemoveHighlightAsync(
        string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, string highlightId, CancellationToken cancellationToken)
    {
        var key = ComputeKey(categoryKey, itemId, sizeBytes, lastWriteTimeUtc);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAllUnlockedAsync(cancellationToken);
            if (!entries.TryGetValue(key, out var highlights)) return false;

            var removed = highlights.RemoveAll(highlight => string.Equals(highlight.Id, highlightId, StringComparison.Ordinal)) > 0;
            if (!removed) return false;

            if (highlights.Count == 0) entries.Remove(key);
            await WriteAllUnlockedAsync(entries, cancellationToken);
            return true;
        }
        finally { _fileLock.Release(); }
    }

    private async Task<Dictionary<string, List<BookHighlightDto>>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_archiveRootPath, "Books", "Notes", HighlightsFileName);
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, List<BookHighlightDto>>>(stream, cancellationToken: cancellationToken)
                ?? new(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private async Task WriteAllUnlockedAsync(Dictionary<string, List<BookHighlightDto>> entries, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_archiveRootPath, "Books", "Notes");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, HighlightsFileName);
        var tempPath = Path.Combine(folder, $".{HighlightsFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(tempPath))
                await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static string ComputeKey(string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc)
    {
        var identity = $"{categoryKey}:{itemId}:{sizeBytes ?? 0}:{lastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
    }
}
