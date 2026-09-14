using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebApp.Services;

namespace WebApp.Tests.Endpoints;

public sealed class KestrelHttpsRedirectionTests
{
    [Fact]
    public async Task No_certificate_configured_serves_the_route_directly_with_no_redirect()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path, certificatePath: null);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/api/archive/photos/items");

        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task Certificate_configured_redirects_a_non_upload_route_to_https()
    {
        using var root = CreateArchive();
        var certificatePath = Path.Combine(Path.GetTempPath(), $"kestrel-https-redirect-test-{Guid.NewGuid():N}.pfx");
        await File.WriteAllBytesAsync(certificatePath, [1, 2, 3]);
        try
        {
            using var factory = new VideoManagerFactory(root.Path, certificatePath: certificatePath);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            using var response = await client.GetAsync("/api/archive/photos/items");

            Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
            Assert.Equal("https", response.Headers.Location?.Scheme);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public async Task Https_request_with_disallowed_host_is_still_rejected_by_the_host_allowlist()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path, certificatePath: null);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "not-allowed.example";

        using var response = await client.GetAsync("/api/archive/photos/items");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Cuts"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "VideoComposition"));
        return root;
    }

    private sealed class VideoManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _archiveRoot;
        private readonly string? _certificatePath;
        private readonly string _previewPath = CreateDirectory();

        public VideoManagerFactory(string archiveRoot, string? certificatePath)
        {
            _archiveRoot = archiveRoot;
            _certificatePath = certificatePath;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = _archiveRoot,
                    ["VideoLibrary:Path"] = Path.Combine(_archiveRoot, "Videos"),
                    ["ThumbnailCache:Path"] = _previewPath,
                    ["VideoCut:Path"] = Path.Combine(_archiveRoot, "Videos", "Cuts"),
                    ["VideoComposition:Path"] = Path.Combine(_archiveRoot, "Videos", "VideoComposition"),
                    ["HttpsCertificate:Path"] = _certificatePath,
                    ["HttpsCertificate:Password"] = _certificatePath is null ? null : "test-password",
                    ["HttpsCertificate:Port"] = "8443"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVideoDurationProbe>();
                services.RemoveAll<IVideoResolutionProbe>();
                services.AddSingleton<IVideoDurationProbe>(new FixedDurationProbe(TimeSpan.FromSeconds(305)));
                services.AddSingleton<IVideoResolutionProbe>(new FixedResolutionProbe(1920, 1080));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_previewPath))
            {
                Directory.Delete(_previewPath, recursive: true);
            }
        }
    }

    private sealed class FixedDurationProbe(TimeSpan? duration) : IVideoDurationProbe
    {
        public Task<TimeSpan?> GetDurationAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult(duration);
    }

    private sealed class FixedResolutionProbe(int? width, int? height) : IVideoResolutionProbe
    {
        public Task<(int? Width, int? Height)> GetResolutionAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult((width, height));
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kestrel-https-redirect-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"kestrel-https-redirect-archive-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
