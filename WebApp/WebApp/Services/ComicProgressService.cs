using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed class ComicProgressService(IOptions<ArchiveRootOptions> options) : IComicProgressService
{
    private const string ProgressFileName = "pereneArchiveComicProgress.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<ComicProgressDto?> LoadProgressAsync(string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, CancellationToken cancellationToken)
    {
        var key = ComputeKey(categoryKey, itemId, sizeBytes, lastWriteTimeUtc);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAllUnlockedAsync(cancellationToken);
            return entries.TryGetValue(key, out var record) ? new ComicProgressDto(Math.Max(0, record.PageIndex)) : null;
        }
        finally { _fileLock.Release(); }
    }

    public async Task SaveProgressAsync(string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc, ComicProgressDto progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var key = ComputeKey(categoryKey, itemId, sizeBytes, lastWriteTimeUtc);
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var entries = await ReadAllUnlockedAsync(cancellationToken);
            entries[key] = new ProgressRecord(Math.Max(0, progress.PageIndex));
            await WriteAllUnlockedAsync(entries, cancellationToken);
        }
        finally { _fileLock.Release(); }
    }

    private async Task<Dictionary<string, ProgressRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = GetProgressFilePath();
        if (!File.Exists(path)) return new(StringComparer.Ordinal);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, ProgressRecord>>(stream, cancellationToken: cancellationToken)
                ?? new(StringComparer.Ordinal);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private async Task WriteAllUnlockedAsync(Dictionary<string, ProgressRecord> entries, CancellationToken cancellationToken)
    {
        var notesFolder = Path.Combine(_archiveRootPath, "Books", "Notes");
        Directory.CreateDirectory(notesFolder);
        var path = GetProgressFilePath();
        var tempPath = Path.Combine(notesFolder, $".{ProgressFileName}.{Guid.NewGuid():N}.tmp");
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

    private string GetProgressFilePath() => Path.Combine(_archiveRootPath, "Books", "Notes", ProgressFileName);

    private static string ComputeKey(string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc)
    {
        var identity = $"{categoryKey}:{itemId}:{sizeBytes ?? 0}:{lastWriteTimeUtc.Ticks}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32].ToLowerInvariant();
    }

    private sealed record ProgressRecord(int PageIndex);
}
