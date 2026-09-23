namespace WebApp.Client.Models;

public sealed record BatchMoveToTrashArchiveItemsRequest(IReadOnlyList<string> ItemIds);
