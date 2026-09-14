namespace WebApp.Client.Models;

public sealed record MoveArchiveItemRequest(string DestinationCategory, string? DestinationFolderId);
