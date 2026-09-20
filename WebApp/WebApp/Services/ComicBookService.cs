using System.IO.Compression;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ComicBookService(IOptions<ComicReaderOptions> options, ILogger<ComicBookService> logger) : IComicBookService
{
    private static readonly IReadOnlyDictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { [".png"]="image/png", [".jpg"]="image/jpeg", [".jpeg"]="image/jpeg", [".gif"]="image/gif", [".webp"]="image/webp", [".avif"]="image/avif", [".bmp"]="image/bmp", [".ico"]="image/x-icon" };

    public bool TryGetMetadata(ArchiveItemEntry item, out ComicBookMetadata? metadata)
    {
        metadata = null;
        if (!TryGetPages(item, out var pages)) return false;
        metadata = new(pages.Count); return true;
    }

    public bool TryGetPage(ArchiveItemEntry item, int index, out ComicBookPage? page)
    {
        page = null;
        if (!TryGetPages(item, out var pages) || index < 0 || index >= pages.Count) return false;
        try
        {
            using var file = new FileStream(item.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            var entry = zip.GetEntry(pages[index]);
            if (entry is null || !Types.TryGetValue(Path.GetExtension(entry.Name), out var contentType)) return false;
            using var input = entry.Open();
            using var output = new MemoryStream((int)entry.Length);
            input.CopyTo(output);
            page = new(output.ToArray(), contentType); return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Comic page could not be read for opaque archive item {ItemId}", item.Id);
            return false;
        }
    }

    private bool TryGetPages(ArchiveItemEntry item, out List<string> pages)
    {
        pages = [];
        try
        {
            using var file = new FileStream(item.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
            if (zip.Entries.Count > options.Value.MaximumEntryCount)
            {
                logger.LogWarning("Comic rejected for opaque archive item {ItemId}: entry-count policy", item.Id);
                return false;
            }
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                // A CBZ commonly keeps pages below one top-level directory. Directories have no
                // bytes to serve, so ignore them rather than treating a normal archive layout as
                // unsafe input.
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (entry.Length > options.Value.MaximumPageBytes)
                {
                    logger.LogWarning("Comic rejected for opaque archive item {ItemId}: entry policy", item.Id);
                    return false;
                }
                total = checked(total + entry.Length);
                if (total > options.Value.MaximumTotalBytes)
                {
                    logger.LogWarning("Comic rejected for opaque archive item {ItemId}: aggregate-size policy", item.Id);
                    return false;
                }
                if (Types.ContainsKey(Path.GetExtension(entry.Name))) pages.Add(entry.FullName);
            }
            pages.Sort(ComparePageNames);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or OverflowException)
        {
            logger.LogWarning(exception, "Comic archive could not be opened for opaque archive item {ItemId}", item.Id);
            return false;
        }
    }

    // CBZ page names are commonly numeric. Ordinal sorting would put 10.webp before 2.webp,
    // while this comparison keeps numeric runs in their expected reading order.
    private static int ComparePageNames(string left, string right)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftChar = left[leftIndex];
            var rightChar = right[rightIndex];
            if (char.IsAsciiDigit(leftChar) && char.IsAsciiDigit(rightChar))
            {
                var leftStart = leftIndex;
                var rightStart = rightIndex;
                while (leftIndex < left.Length && left[leftIndex] == '0') leftIndex++;
                while (rightIndex < right.Length && right[rightIndex] == '0') rightIndex++;
                var leftDigitsStart = leftIndex;
                var rightDigitsStart = rightIndex;
                while (leftIndex < left.Length && char.IsAsciiDigit(left[leftIndex])) leftIndex++;
                while (rightIndex < right.Length && char.IsAsciiDigit(right[rightIndex])) rightIndex++;
                var lengthComparison = (leftIndex - leftDigitsStart).CompareTo(rightIndex - rightDigitsStart);
                if (lengthComparison != 0) return lengthComparison;
                var digitComparison = string.CompareOrdinal(left, leftDigitsStart, right, rightDigitsStart, leftIndex - leftDigitsStart);
                if (digitComparison != 0) return digitComparison;
                var zeroComparison = (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
                if (zeroComparison != 0) return zeroComparison;
                continue;
            }

            if (leftChar != rightChar) return leftChar.CompareTo(rightChar);
            leftIndex++;
            rightIndex++;
        }

        return left.Length.CompareTo(right.Length);
    }

}
