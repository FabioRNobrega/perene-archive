namespace WebApp.Configuration;

/// <summary>
/// Configures the optional, opt-in HTTPS+HTTP/2 Kestrel endpoint used for LAN request streaming.
/// An unset <see cref="Path"/> is the explicit "not opted in" state; when the
/// referenced file does not exist on disk, <see cref="IsEnabled"/> is <c>false</c> and Kestrel's
/// behavior is unchanged from today (HTTP-only, no new required configuration).
/// </summary>
public sealed class KestrelHttpsOptions
{
    public const string SectionName = "HttpsCertificate";

    public string Path { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int Port { get; set; } = 8443;

    public static bool HasConfiguredPath(KestrelHttpsOptions options) =>
        !string.IsNullOrWhiteSpace(options.Path);

    public static bool IsEnabled(KestrelHttpsOptions options) =>
        HasConfiguredPath(options) && File.Exists(options.Path);

    public static bool HasPositivePort(KestrelHttpsOptions options) => options.Port > 0;

    public static bool HasPasswordWhenPathConfigured(KestrelHttpsOptions options) =>
        !HasConfiguredPath(options) || !string.IsNullOrEmpty(options.Password);
}
