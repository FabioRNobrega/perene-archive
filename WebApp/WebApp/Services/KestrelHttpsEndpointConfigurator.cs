using Microsoft.AspNetCore.Server.Kestrel.Core;
using WebApp.Configuration;

namespace WebApp.Services;

/// <summary>
/// Adds the optional HTTPS+HTTP/2 Kestrel endpoint when a certificate is configured. See
/// https://learn.microsoft.com/aspnet/core/fundamentals/servers/kestrel/endpoints for the
/// documented <c>Listen</c>/<c>UseHttps</c>/<c>Http1AndHttp2</c> pattern this follows.
/// </summary>
public static class KestrelHttpsEndpointConfigurator
{
    /// <summary>
    /// The container-internal HTTP port, fixed to match <c>ASPNETCORE_URLS</c> in the
    /// Dockerfile (only the host-published port varies, via <c>WEBAPP_PORT</c>). Kestrel's
    /// documented behavior is that any code-based <c>Listen</c> call replaces endpoints
    /// otherwise discovered from <c>ASPNETCORE_URLS</c>/<c>--urls</c> entirely, so this
    /// endpoint must be re-declared explicitly whenever the HTTPS endpoint below is added,
    /// or the existing HTTP endpoint silently disappears.
    /// </summary>
    private const int HttpPort = 8080;

    public static bool TryConfigure(KestrelServerOptions serverOptions, KestrelHttpsOptions options)
    {
        if (!KestrelHttpsOptions.IsEnabled(options))
        {
            return false;
        }

        serverOptions.Listen(System.Net.IPAddress.Any, HttpPort, listenOptions =>
        {
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        });

        serverOptions.Listen(System.Net.IPAddress.Any, options.Port, listenOptions =>
        {
            listenOptions.UseHttps(options.Path, options.Password);
            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
        });

        return true;
    }
}
