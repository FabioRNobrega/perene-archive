namespace WebApp.Client.Models;

public static class ArchiveItemSorter
{
    public static IReadOnlyList<ArchiveItemDto> Sort(IEnumerable<ArchiveItemDto> items, ArchiveSortOption option)
    {
        var grouped = items.OrderBy(item => item.Kind == ArchiveItemKind.File);
        var ordered = option == ArchiveSortOption.NameDescending
            ? grouped.ThenByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
            : grouped.ThenBy(item => item.Kind == ArchiveItemKind.Folder ? item.Name : string.Empty, StringComparer.OrdinalIgnoreCase);

        ordered = option switch
        {
            ArchiveSortOption.Size => ordered
                .ThenByDescending(item => item.Kind == ArchiveItemKind.File ? item.SizeBytes ?? 0 : 0)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            ArchiveSortOption.Date => ordered
                .ThenByDescending(item => item.Kind == ArchiveItemKind.File ? item.LastWriteTimeUtc : DateTime.MinValue)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            ArchiveSortOption.NameDescending => ordered,
            _ => ordered.ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
        };

        return ordered.ToList();
    }
}
