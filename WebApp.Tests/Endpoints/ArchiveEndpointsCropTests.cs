using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class ArchiveEndpointsCropTests
{
    [Fact]
    public async Task Crop_creates_a_file_under_pictures_cuts_and_returns_its_item_contract()
    {
        using var root = CreateArchive();
        var sourcePath = Path.Combine(root.Path, "Pictures", "Beach Sunset.png");
        await CreateFixtureImageAsync(sourcePath, 20, 10);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/photos/items/{item.Id}/crop", new { X = 2, Y = 2, Width = 8, Height = 4 });
        var body = await response.Content.ReadFromJsonAsync<CropResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(root.Path, "Pictures", "cuts", "Beach Sunset 0001.png")));
        Assert.Equal("Beach Sunset 0001.png", body!.Name);
        Assert.StartsWith("/api/archive/photos/items/", body.ImageUrl);
        Assert.EndsWith("/image", body.ImageUrl);

        using var croppedResponse = await client.GetAsync(body.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, croppedResponse.StatusCode);
        using var cropped = await Image.LoadAsync(await croppedResponse.Content.ReadAsStreamAsync());
        Assert.Equal(8, cropped.Width);
        Assert.Equal(4, cropped.Height);
    }

    [Fact]
    public async Task Crop_rejects_out_of_bounds_region_without_writing_a_file()
    {
        using var root = CreateArchive();
        var sourcePath = Path.Combine(root.Path, "Pictures", "photo.jpg");
        await CreateFixtureImageAsync(sourcePath, 10, 10);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/photos/items/{item.Id}/crop", new { X = 5, Y = 5, Width = 20, Height = 20 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cutsPath = Path.Combine(root.Path, "Pictures", "cuts");
        Assert.True(!Directory.Exists(cutsPath) || Directory.EnumerateFiles(cutsPath).Any() is false);
    }

    [Fact]
    public async Task Crop_returns_not_found_for_an_unresolvable_id()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/photos/items/{Guid.NewGuid():N}/crop", new { X = 0, Y = 0, Width = 1, Height = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("animation.gif")]
    [InlineData("photo.webp")]
    [InlineData("photo.avif")]
    [InlineData("photo.bmp")]
    [InlineData("favicon.ico")]
    public async Task Crop_returns_not_found_for_added_raster_formats(string fileName)
    {
        using var root = CreateArchive();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", fileName), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/photos/items/{item.Id}/crop", new { X = 0, Y = 0, Width = 1, Height = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "Pictures", "cuts")));
    }

    private sealed record CropResponse(string Id, string Name, string ImageUrl);

    private static async Task CreateFixtureImageAsync(string path, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        await image.SaveAsync(path);
    }

    private sealed class VideoManagerFactory(string archiveRoot) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = archiveRoot,
                    ["VideoLibrary:Path"] = Path.Combine(archiveRoot, "Videos"),
                    ["ThumbnailCache:Path"] = CreateDirectory(),
                    ["VideoCut:Path"] = Path.Combine(archiveRoot, "Videos", "Cuts"),
                    ["VideoComposition:Path"] = Path.Combine(archiveRoot, "Videos", "VideoComposition")
                }));
        }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-image-crop-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-archive-crop-endpoints-{Guid.NewGuid():N}");
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
