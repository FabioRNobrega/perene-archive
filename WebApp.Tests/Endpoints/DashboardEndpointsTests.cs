using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class DashboardEndpointsTests
{
    [Theory]
    [InlineData("/api/dashboard/system")]
    [InlineData("/api/dashboard/memory")]
    [InlineData("/api/dashboard/storage")]
    [InlineData("/api/dashboard/network")]
    [InlineData("/api/dashboard/archive")]
    [InlineData("/api/dashboard/docker")]
    [InlineData("/api/dashboard/health")]
    [InlineData("/api/dashboard/history")]
    [InlineData("/api/dashboard/alerts")]
    [InlineData("/api/dashboard/storage/custom")]
    public async Task Dashboard_endpoint_returns_ok_and_never_500(string path)
    {
        using var root = new TemporaryDirectory();
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_custom_storage_view_with_a_valid_folder_returns_the_new_view()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Clips"));
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();

        var listing = await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/videos/items");
        var folderId = listing!.Items.Single(item => item.Name == "Clips").Id;

        using var response = await client.PostAsJsonAsync(
            "/api/dashboard/storage/custom", new AddCustomStorageViewRequest("videos", folderId, false, 1024));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var views = await response.Content.ReadFromJsonAsync<List<CustomStorageViewDto>>();
        var view = Assert.Single(views!);
        Assert.Equal("Clips", view.Title);
        Assert.Equal(1024, view.MaxBytes);
    }

    [Fact]
    public async Task Post_custom_storage_view_with_an_invalid_folder_id_returns_not_found_and_writes_nothing()
    {
        using var root = new TemporaryDirectory();
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/dashboard/storage/custom", new AddCustomStorageViewRequest("videos", "not-a-real-id", false, 1024));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(root.Path, "Dashboard", "pereneArchiveCustomStorageViews.json")));
    }

    [Fact]
    public async Task Delete_custom_storage_view_for_an_unknown_id_returns_not_found()
    {
        using var root = new TemporaryDirectory();
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/dashboard/storage/custom/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Patch_custom_storage_view_updates_and_returns_the_new_max_size()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Clips"));
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/videos/items");
        var folderId = listing!.Items.Single(item => item.Name == "Clips").Id;
        var addResponse = await client.PostAsJsonAsync(
            "/api/dashboard/storage/custom", new AddCustomStorageViewRequest("videos", folderId, false, 1024));
        var addedViews = await addResponse.Content.ReadFromJsonAsync<List<CustomStorageViewDto>>();
        var viewId = addedViews!.Single().Id;

        using var response = await client.PatchAsJsonAsync(
            $"/api/dashboard/storage/custom/{viewId}", new UpdateCustomStorageViewMaxSizeRequest(4096));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var views = await response.Content.ReadFromJsonAsync<List<CustomStorageViewDto>>();
        Assert.Equal(4096, Assert.Single(views!).MaxBytes);
    }

    [Fact]
    public async Task Get_custom_storage_views_after_deleting_the_backing_folder_marks_it_unavailable()
    {
        using var root = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Clips"));
        using var factory = new DashboardFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/videos/items");
        var folderId = listing!.Items.Single(item => item.Name == "Clips").Id;
        await client.PostAsJsonAsync("/api/dashboard/storage/custom", new AddCustomStorageViewRequest("videos", folderId, false, 1024));
        Directory.Delete(Path.Combine(root.Path, "Videos", "Clips"), recursive: true);

        using var response = await client.GetAsync("/api/dashboard/storage/custom");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var views = await response.Content.ReadFromJsonAsync<List<CustomStorageViewDto>>();
        Assert.False(Assert.Single(views!).IsAvailable);
    }

    private sealed class DashboardFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPath;
        private readonly string _previewPath;
        private readonly string _cutPath;
        private readonly string _compositionPath;

        public DashboardFactory(string rootPath)
        {
            _rootPath = rootPath;
            _previewPath = Path.Combine(Path.GetTempPath(), $"dashboard-api-preview-{Guid.NewGuid():N}");
            _cutPath = Path.Combine(Path.GetTempPath(), $"dashboard-api-cuts-{Guid.NewGuid():N}");
            _compositionPath = Path.Combine(Path.GetTempPath(), $"dashboard-api-composition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_previewPath);
            Directory.CreateDirectory(_cutPath);
            Directory.CreateDirectory(_compositionPath);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
            if (!disposing)
            {
                return;
            }

            foreach (var path in new[] { _previewPath, _cutPath, _compositionPath })
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dashboard-api-root-{Guid.NewGuid():N}");
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
