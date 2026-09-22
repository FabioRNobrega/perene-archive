namespace WebApp.Client.Models;

public sealed record CustomStorageViewDto(string Id, string Title, bool IsAvailable, long UsedBytes, long MaxBytes);
