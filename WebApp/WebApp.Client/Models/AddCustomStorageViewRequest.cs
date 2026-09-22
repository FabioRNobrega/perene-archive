namespace WebApp.Client.Models;

public sealed record AddCustomStorageViewRequest(string? CategoryKey, string? FolderId, bool IsWholeArchive, long MaxSizeBytes);
