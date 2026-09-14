namespace WebApp.Client.Models;

public sealed record ArchiveUploadCreateRequest(string? ParentId, string FileName, long TotalBytes);
