using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed class CustomStorageViewService(
    IArchiveService archiveService, IOptions<ArchiveRootOptions> options) : ICustomStorageViewService
{
    private const string FileName = "pereneArchiveCustomStorageViews.json";
    private const string WholeArchiveTitle = "Archive";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public async Task<IReadOnlyList<CustomStorageViewDto>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        List<CustomStorageViewRecord> records;
        try
        {
            records = await ReadAllUnlockedAsync(cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }

        return records.Select(Resolve).ToList();
    }

    public async Task<IReadOnlyList<CustomStorageViewDto>> AddAsync(
        string? categoryKey, string? folderId, bool isWholeArchive, long maxSizeBytes, CancellationToken cancellationToken)
    {
        string title;
        if (isWholeArchive)
        {
            title = WholeArchiveTitle;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(categoryKey))
            {
                throw new ArchiveValidationException("A category is required.");
            }

            var listing = archiveService.List(categoryKey, folderId);
            title = string.IsNullOrWhiteSpace(folderId) ? listing.Category.DisplayName : listing.CurrentFolder.Name;
        }

        var record = new CustomStorageViewRecord(
            Guid.NewGuid().ToString("N"),
            isWholeArchive ? null : categoryKey,
            isWholeArchive ? null : folderId,
            isWholeArchive,
            title,
            maxSizeBytes);

        await _fileLock.WaitAsync(cancellationToken);
        List<CustomStorageViewRecord> records;
        try
        {
            records = await ReadAllUnlockedAsync(cancellationToken);
            records.Add(record);
            await WriteAllUnlockedAsync(records, cancellationToken);
        }
        finally
        {
            _fileLock.Release();
        }

        return records.Select(Resolve).ToList();
    }

    public async Task<IReadOnlyList<CustomStorageViewDto>?> RemoveAsync(string viewId, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        List<CustomStorageViewRecord> records;
        bool removed;
        try
        {
            records = await ReadAllUnlockedAsync(cancellationToken);
            removed = records.RemoveAll(record => string.Equals(record.Id, viewId, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                await WriteAllUnlockedAsync(records, cancellationToken);
            }
        }
        finally
        {
            _fileLock.Release();
        }

        return removed ? records.Select(Resolve).ToList() : null;
    }

    public async Task<IReadOnlyList<CustomStorageViewDto>?> UpdateMaxSizeAsync(
        string viewId, long maxSizeBytes, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        List<CustomStorageViewRecord> records;
        var found = false;
        try
        {
            records = await ReadAllUnlockedAsync(cancellationToken);
            var index = records.FindIndex(record => string.Equals(record.Id, viewId, StringComparison.Ordinal));
            if (index >= 0)
            {
                found = true;
                records[index] = records[index] with { MaxSizeBytes = maxSizeBytes };
                await WriteAllUnlockedAsync(records, cancellationToken);
            }
        }
        finally
        {
            _fileLock.Release();
        }

        return found ? records.Select(Resolve).ToList() : null;
    }

    private CustomStorageViewDto Resolve(CustomStorageViewRecord record)
    {
        try
        {
            var physicalPath = record.IsWholeArchive
                ? _archiveRootPath
                : archiveService.List(record.CategoryKey!, record.FolderId).CurrentFolder.PhysicalPath;
            return new CustomStorageViewDto(record.Id, record.Title, true, ComputeRecursiveSize(physicalPath), record.MaxSizeBytes);
        }
        catch (ArchiveException)
        {
            return new CustomStorageViewDto(record.Id, record.Title, false, 0, record.MaxSizeBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new CustomStorageViewDto(record.Id, record.Title, false, 0, record.MaxSizeBytes);
        }
    }

    private static long ComputeRecursiveSize(string folderPath)
    {
        long total = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
            }))
            {
                try
                {
                    total += new FileInfo(path).Length;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        return total;
    }

    private async Task<List<CustomStorageViewRecord>> ReadAllUnlockedAsync(CancellationToken cancellationToken)
    {
        var path = GetFilePath();
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var records = await JsonSerializer.DeserializeAsync<List<CustomStorageViewRecord>>(
                stream, cancellationToken: cancellationToken);
            return records ?? [];
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            return [];
        }
    }

    private async Task WriteAllUnlockedAsync(List<CustomStorageViewRecord> records, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_archiveRootPath, "Dashboard");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        var tempPath = Path.Combine(folder, $".{FileName}.{Guid.NewGuid():N}.tmp");

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private string GetFilePath() => Path.Combine(_archiveRootPath, "Dashboard", FileName);

    private sealed record CustomStorageViewRecord(
        string Id, string? CategoryKey, string? FolderId, bool IsWholeArchive, string Title, long MaxSizeBytes);
}
