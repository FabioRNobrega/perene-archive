using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class KestrelHttpsEndpointConfiguratorTests
{
    [Fact]
    public void TryConfigure_returns_false_and_does_not_throw_when_no_path_is_configured()
    {
        var builder = WebApplication.CreateBuilder();
        var options = new KestrelHttpsOptions();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            var result = KestrelHttpsEndpointConfigurator.TryConfigure(serverOptions, options);
            Assert.False(result);
        });

        using var app = builder.Build();
    }

    [Fact]
    public void TryConfigure_returns_true_and_does_not_throw_with_a_real_temp_certificate()
    {
        using var certificate = CreateSelfSignedCertificate();
        var pfxPath = WritePfx(certificate);
        try
        {
            var builder = WebApplication.CreateBuilder();
            var options = new KestrelHttpsOptions
            {
                Path = pfxPath,
                Password = "test-password",
                Port = GetEphemeralPort()
            };

            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                var result = KestrelHttpsEndpointConfigurator.TryConfigure(serverOptions, options);
                Assert.True(result);
            });

            using var app = builder.Build();
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }

    [Fact]
    public async Task Real_socket_negotiates_http2_over_the_configured_https_endpoint()
    {
        using var certificate = CreateSelfSignedCertificate();
        var pfxPath = WritePfx(certificate);
        try
        {
            var port = GetEphemeralPort();
            var options = new KestrelHttpsOptions
            {
                Path = pfxPath,
                Password = "test-password",
                Port = port
            };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(serverOptions =>
                KestrelHttpsEndpointConfigurator.TryConfigure(serverOptions, options));
            await using var app = builder.Build();
            app.MapGet("/", () => "ok");
            await app.StartAsync();
            try
            {
                using var handler = new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }
                };
                using var client = new HttpClient(handler);

                using var http2Request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{port}/")
                {
                    Version = HttpVersion.Version20,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var http2Response = await client.SendAsync(http2Request);
                Assert.True(http2Response.IsSuccessStatusCode);
                Assert.Equal(HttpVersion.Version20, http2Response.Version);

                using var http1Request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{port}/")
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                using var http1Response = await client.SendAsync(http1Request);
                Assert.True(http1Response.IsSuccessStatusCode);
                Assert.Equal(HttpVersion.Version11, http1Response.Version);
            }
            finally
            {
                await app.StopAsync();
            }
        }
        finally
        {
            File.Delete(pfxPath);
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static string WritePfx(X509Certificate2 certificate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"kestrel-https-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "test-password"));
        return path;
    }

    private static int GetEphemeralPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
