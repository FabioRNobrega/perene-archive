using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class ArchiveEndpointsFolderThumbnailTests
{
    [Fact]
    public async Task Upload_publishes_the_reserved_file_inside_the_folder_and_reports_it_in_the_listing()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");
        Assert.Null(folder.FolderThumbnailUrl);

        using var content = BuildImageUpload(1920, 1080);
        using var response = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content);
        var updated = await response.Content.ReadFromJsonAsync<ArchiveItemDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(updated!.FolderThumbnailUrl);
        Assert.True(File.Exists(Path.Combine(root.Path, "Pictures", "Album", ".pereneFolderThumbnail.jpg")));

        var refreshedListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var refreshedFolder = refreshedListing.Items.Single(item => item.Name == "Album");
        Assert.NotNull(refreshedFolder.FolderThumbnailUrl);

        using var thumbnailResponse = await client.GetAsync(refreshedFolder.FolderThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        using var image = await Image.LoadAsync(await thumbnailResponse.Content.ReadAsStreamAsync());
        Assert.Equal(640, image.Width);
        Assert.Equal(360, image.Height);
    }

    [Fact]
    public async Task Upload_rejects_a_non_image_file_and_writes_nothing()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");

        using var content = new MultipartFormDataContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes("not actually an image");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "not-an-image.txt");

        using var response = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(root.Path, "Pictures", "Album", ".pereneFolderThumbnail.jpg")));
    }

    [Fact]
    public async Task Upload_rejects_a_file_over_the_size_cap()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");

        using var content = new MultipartFormDataContent();
        var bytes = new byte[10 * 1024 * 1024 + 1];
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "big.jpg");

        using var response = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(root.Path, "Pictures", "Album", ".pereneFolderThumbnail.jpg")));
    }

    [Fact]
    public async Task Upload_returns_not_found_for_an_unresolvable_id()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var content = BuildImageUpload(100, 100);
        using var response = await client.PostAsync(
            $"/api/archive/photos/items/{Guid.NewGuid():N}/folder-thumbnail", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_the_reserved_file_and_clears_the_dto_field()
    {
        using var root = CreateArchive();
        var albumPath = Path.Combine(root.Path, "Pictures", "Album");
        Directory.CreateDirectory(albumPath);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");
        using (var content = BuildImageUpload(640, 360))
        using (var uploadResponse = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content))
        {
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        using var deleteResponse = await client.DeleteAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail");
        var updated = await deleteResponse.Content.ReadFromJsonAsync<ArchiveItemDto>();

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Null(updated!.FolderThumbnailUrl);
        Assert.False(File.Exists(Path.Combine(albumPath, ".pereneFolderThumbnail.jpg")));

        using var getResponse = await client.GetAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_on_a_folder_without_a_thumbnail_returns_not_found()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");

        using var response = await client.DeleteAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reserved_thumbnail_file_never_appears_in_the_folders_own_listing()
    {
        using var root = CreateArchive();
        var albumPath = Path.Combine(root.Path, "Pictures", "Album");
        Directory.CreateDirectory(albumPath);
        await File.WriteAllTextAsync(Path.Combine(albumPath, "photo.jpg"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var rootListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = rootListing.Items.Single(item => item.Name == "Album");
        using (var content = BuildImageUpload(640, 360))
        using (var uploadResponse = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content))
        {
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        var childListing = (await client.GetFromJsonAsync<ArchiveListingDto>($"/api/archive/photos/items?folderId={folder.Id}"))!;

        Assert.Single(childListing.Items);
        Assert.DoesNotContain(childListing.Items, item => item.Name == ".pereneFolderThumbnail.jpg");
    }

    [Fact]
    public async Task Renaming_a_folder_with_a_thumbnail_keeps_it_resolvable_at_the_new_id()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");
        using (var content = BuildImageUpload(640, 360))
        using (var uploadResponse = await client.PostAsync($"/api/archive/photos/items/{folder.Id}/folder-thumbnail", content))
        {
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        using var renameResponse = await client.PatchAsJsonAsync(
            $"/api/archive/photos/items/{folder.Id}/name", new RenameArchiveItemRequest("Renamed Album"));
        var renamedListing = await renameResponse.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var renamedFolder = renamedListing!.Items.Single(item => item.Name == "Renamed Album");

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        Assert.NotNull(renamedFolder.FolderThumbnailUrl);

        using var thumbnailResponse = await client.GetAsync(renamedFolder.FolderThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.True(File.Exists(Path.Combine(root.Path, "Pictures", "Renamed Album", ".pereneFolderThumbnail.jpg")));
    }

    [Fact]
    public async Task Moving_a_folder_with_a_thumbnail_keeps_it_resolvable_at_the_destination()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Album"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");
        using (var content = BuildImageUpload(640, 360))
        using (var uploadResponse = await client.PostAsync($"/api/archive/downloads/items/{folder.Id}/folder-thumbnail", content))
        {
            Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        }

        using var moveResponse = await client.PatchAsJsonAsync(
            $"/api/archive/downloads/items/{folder.Id}/location", new MoveArchiveItemRequest("videos", null));
        var job = await moveResponse.Content.ReadFromJsonAsync<ArchiveMutationJobDto>();
        Assert.Equal(HttpStatusCode.Accepted, moveResponse.StatusCode);
        var completed = await PollUntilTerminalAsync(client, job!.JobId);
        Assert.Equal(ArchiveMutationJobState.Completed, completed.State);

        var destinationListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/videos/items"))!;
        var movedFolder = destinationListing.Items.Single(item => item.Name == "Album");

        Assert.NotNull(movedFolder.FolderThumbnailUrl);
        Assert.True(File.Exists(Path.Combine(root.Path, "Videos", "Album", ".pereneFolderThumbnail.jpg")));

        using var thumbnailResponse = await client.GetAsync(movedFolder.FolderThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
    }

    private static async Task<ArchiveMutationJobDto> PollUntilTerminalAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var jobs = await client.GetFromJsonAsync<List<ArchiveMutationJobDto>>("/api/archive/jobs");
            var job = jobs!.SingleOrDefault(candidate => candidate.JobId == jobId);
            if (job is { State: ArchiveMutationJobState.Completed or ArchiveMutationJobState.Failed })
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Archive mutation job {jobId} did not reach a terminal state in time.");
    }

    private static MultipartFormDataContent BuildImageUpload(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(stream.ToArray());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "thumbnail.png");
        return content;
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
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-folder-thumbnail-preview-{Guid.NewGuid():N}");
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-archive-folder-thumbnail-endpoints-{Guid.NewGuid():N}");
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
