using System.Security.Cryptography;
using System.Text;

namespace WebApp.Authorization;

/// <summary>Opaque browser-facing folder tokens: a hash of the server-only identity, never a path or database ID.</summary>
public static class FolderKeys
{
    public static string Of(string rootKey, string relativePath) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"folder:{rootKey}:{relativePath}")))[..32].ToLowerInvariant();

    public static string Of(FolderRecord folder) => Of(folder.RootKey, folder.RelativePath);
}
