namespace WebApp.Client.Models;

public sealed record BatchMoveArchiveItemsRequest(IReadOnlyList<string> ItemIds, string DestinationCategory, string? DestinationFolderId);
