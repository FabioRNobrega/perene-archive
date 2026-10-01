namespace WebApp.Client.Models;

public sealed record CreateFolderRequest(string? ParentId, string Name, bool IsPrivate = false);
