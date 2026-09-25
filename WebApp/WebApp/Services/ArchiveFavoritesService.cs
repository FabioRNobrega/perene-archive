using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveFavoritesService(IArchiveService archive, IOptions<ArchiveRootOptions> options) : IArchiveFavoritesService
{
    private const string FileName = "pereneArchiveFavorites.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _root = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlySet<string>> GetFavoriteIdsAsync(string category, IEnumerable<string> currentIds, CancellationToken cancellationToken)
    {
        var available = currentIds.ToHashSet(StringComparer.Ordinal);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAsync(cancellationToken);
            var stale = records.RemoveAll(x => !archive.TryResolveItem(x.Category, x.ItemId, out _)) > 0;
            if (stale) await WriteAsync(records, cancellationToken);
            return records.Where(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && available.Contains(x.ItemId))
                .Select(x => x.ItemId).ToHashSet(StringComparer.Ordinal);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> ToggleAsync(string category, string itemId, CancellationToken cancellationToken)
    {
        if (!archive.TryResolveItem(category, itemId, out _)) throw new ArchiveNotFoundException("The archive item does not exist.");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var records = await ReadAsync(cancellationToken);
            var removed = records.RemoveAll(x => x.Category.Equals(category, StringComparison.OrdinalIgnoreCase) && x.ItemId == itemId) > 0;
            if (!removed) records.Add(new FavoriteRecord(category, itemId));
            await WriteAsync(records, cancellationToken);
            return !removed;
        }
        finally { _lock.Release(); }
    }

    private async Task<List<FavoriteRecord>> ReadAsync(CancellationToken token)
    {
        var path = Path.Combine(_root, "Dashboard", FileName);
        if (!File.Exists(path)) return [];
        try { await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<List<FavoriteRecord>>(stream, cancellationToken: token) ?? []; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or FormatException) { return []; }
    }

    private async Task WriteAsync(List<FavoriteRecord> records, CancellationToken token)
    {
        var folder = Path.Combine(_root, "Dashboard"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName); var temp = Path.Combine(folder, $".{FileName}.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, token);
        File.Move(temp, path, true);
    }

    private sealed record FavoriteRecord(string Category, string ItemId);
}
