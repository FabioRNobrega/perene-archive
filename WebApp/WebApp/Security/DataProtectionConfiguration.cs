using Microsoft.AspNetCore.DataProtection;

namespace WebApp.Security;

public static class DataProtectionConfiguration
{
    public const string ApplicationName = "PereneArchive";
    public const string DefaultKeysPath = "/appdata/keys";
    public const string KeysPathSetting = "DataProtection:KeysPath";

    /// <summary>Persists Data Protection keys on the dedicated app-data volume so cookies and antiforgery tokens survive restarts.</summary>
    public static IServiceCollection AddPereneDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var keysPath = ResolveKeysPath(configuration);
        Directory.CreateDirectory(keysPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName(ApplicationName);
        return services;
    }

    public static string ResolveKeysPath(IConfiguration configuration) =>
        configuration[KeysPathSetting] is { Length: > 0 } configured ? configured : DefaultKeysPath;
}
