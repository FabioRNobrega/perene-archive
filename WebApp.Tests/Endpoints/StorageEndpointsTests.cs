using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class StorageEndpointsTests
{
    [Fact]
    public async Task Storage_usage_endpoint_returns_only_aggregate_byte_counts()
    {
        using var root = new TemporaryDirectory();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/storage/usage");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["totalBytes", "usedBytes"],
            System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()
                .Select(property => property.Name).OrderBy(name => name));
        Assert.DoesNotContain(root.Path, json);

        var usage = await response.Content.ReadFromJsonAsync<StorageUsageDto>();
        Assert.NotNull(usage);
        Assert.True(usage!.UsedBytes >= 0);
        Assert.True(usage.TotalBytes >= 0);
        Assert.True(usage.UsedBytes <= usage.TotalBytes);
    }

    private sealed class VideoManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPath;
        private readonly string _previewPath;
        private readonly string _cutPath;
        private readonly string _compositionPath;

        public VideoManagerFactory(string rootPath)
        {
            _rootPath = rootPath;
            _previewPath = Path.Combine(Path.GetTempPath(), $"video-manager-storage-api-preview-{Guid.NewGuid():N}");
            _cutPath = Path.Combine(Path.GetTempPath(), $"video-manager-storage-api-cuts-{Guid.NewGuid():N}");
            _compositionPath = Path.Combine(Path.GetTempPath(), $"video-manager-storage-api-composition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_previewPath);
            Directory.CreateDirectory(_cutPath);
            Directory.CreateDirectory(_compositionPath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSecurity.Apply(builder);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = _rootPath,
                    ["VideoLibrary:Path"] = _rootPath,
                    ["ThumbnailCache:Path"] = _previewPath,
                    ["VideoCut:Path"] = _cutPath,
                    ["VideoComposition:Path"] = _compositionPath,
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_previewPath))
            {
                Directory.Delete(_previewPath, recursive: true);
            }
            if (disposing && Directory.Exists(_cutPath))
            {
                Directory.Delete(_cutPath, recursive: true);
            }
            if (disposing && Directory.Exists(_compositionPath))
            {
                Directory.Delete(_compositionPath, recursive: true);
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-storage-api-{Guid.NewGuid():N}");
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
