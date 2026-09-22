using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class ArchiveEndpointsComicProgressTests
{
    [Fact]
    public async Task Comic_progress_routes_round_trip_only_opaque_data()
    {
        using var root = new TemporaryDirectory();
        CreateArchive(root.Path);
        CreateComic(Path.Combine(root.Path, "Books", "comic.cbz"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var comic = Assert.Single((await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!.Items);
        var url = $"/api/archive/books/items/{comic.Id}/comic/progress";

        using var initial = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Null(await initial.Content.ReadFromJsonAsync<ComicProgressDto>());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new ComicProgressDto(1))).StatusCode);
        var saved = await client.GetFromJsonAsync<ComicProgressDto>(url);
        Assert.Equal(1, saved!.PageIndex);
        var json = await File.ReadAllTextAsync(Path.Combine(root.Path, "Books", "Notes", "pereneArchiveComicProgress.json"));
        Assert.DoesNotContain(root.Path, json);
        Assert.DoesNotContain("page-01.jpg", json);
    }

    [Fact]
    public async Task Comic_progress_routes_reject_invalid_or_cross_category_ids_without_path_disclosure()
    {
        using var root = new TemporaryDirectory();
        CreateArchive(root.Path);
        CreateComic(Path.Combine(root.Path, "Books", "comic.cbz"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "note");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var comic = Assert.Single((await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!.Items);

        using var negative = await client.PutAsJsonAsync($"/api/archive/books/items/{comic.Id}/comic/progress", new ComicProgressDto(-1));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        foreach (var url in new[] { "/api/archive/books/items/unknown/comic/progress", $"/api/archive/documents/items/{comic.Id}/comic/progress" })
        {
            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(root.Path, body);
        }
    }

    private static void CreateComic(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var page in new[] { "page-01.jpg", "page-02.jpg" })
        {
            using var writer = new StreamWriter(zip.CreateEntry(page).Open());
            writer.Write("fixture");
        }
    }

    private static void CreateArchive(string root) { foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" }) Directory.CreateDirectory(Path.Combine(root, folder)); Directory.CreateDirectory(Path.Combine(root, "Videos", "Cuts")); Directory.CreateDirectory(Path.Combine(root, "Videos", "VideoComposition")); }

    private sealed class VideoManagerFactory(string archiveRoot) : WebApplicationFactory<Program>
    {
        private readonly string _previewPath = Path.Combine(Path.GetTempPath(), $"comic-progress-preview-{Guid.NewGuid():N}");
        protected override void ConfigureWebHost(IWebHostBuilder builder) { Directory.CreateDirectory(_previewPath); builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ArchiveRoot:Path"] = archiveRoot, ["VideoLibrary:Path"] = Path.Combine(archiveRoot, "Videos"), ["ThumbnailCache:Path"] = _previewPath, ["VideoCut:Path"] = Path.Combine(archiveRoot, "Videos", "Cuts"), ["VideoComposition:Path"] = Path.Combine(archiveRoot, "Videos", "VideoComposition"), ["HoverPreview:Enabled"] = "false" })); }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing && Directory.Exists(_previewPath)) Directory.Delete(_previewPath, true); }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"comic-progress-endpoints-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
