namespace WebApp.Authorization;

/// <summary>Server-only identity of a directory: a seeded root key plus a forward-slash relative path. Never sent to the browser.</summary>
public sealed record FolderLocation(string RootKey, string RelativePath)
{
    public static string Normalize(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".") return string.Empty;
        return string.Join('/', relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
    }

    public static FolderLocation Create(string rootKey, string? relativePath) => new(rootKey, Normalize(relativePath));

    public FolderLocation Child(string name) =>
        new(RootKey, RelativePath.Length == 0 ? name : $"{RelativePath}/{name}");

    public FolderLocation? Parent()
    {
        if (RelativePath.Length == 0) return null;
        var index = RelativePath.LastIndexOf('/');
        return new FolderLocation(RootKey, index < 0 ? string.Empty : RelativePath[..index]);
    }

    public bool IsWithin(FolderLocation ancestor) =>
        RootKey == ancestor.RootKey &&
        (ancestor.RelativePath.Length == 0 ||
         RelativePath == ancestor.RelativePath ||
         RelativePath.StartsWith(ancestor.RelativePath + "/", StringComparison.Ordinal));
}

/// <summary>The resource handed to <see cref="FolderPermissionAuthorizationHandler"/>.</summary>
public sealed record FolderOperationContext(FolderLocation Location);
