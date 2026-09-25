namespace WebApp.Client.Models;

/// <summary>Pure rules for which items a right-click/drag acts on and where they may be dropped.</summary>
public static class ArchiveDragDropRules
{
    public static IReadOnlyList<string> ResolveTargets(IReadOnlyCollection<string> selectedIds, string itemId) =>
        selectedIds.Contains(itemId, StringComparer.Ordinal)
            ? selectedIds.Distinct(StringComparer.Ordinal).ToList()
            : [itemId];

    public static bool CanDrop(IReadOnlyCollection<string> draggedIds, string? targetFolderId, string? currentFolderId)
    {
        if (draggedIds.Count == 0)
        {
            return false;
        }

        if (string.Equals(targetFolderId ?? string.Empty, currentFolderId ?? string.Empty, StringComparison.Ordinal))
        {
            return false;
        }

        return targetFolderId is null || !draggedIds.Contains(targetFolderId, StringComparer.Ordinal);
    }
}
