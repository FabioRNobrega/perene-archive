using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Endpoints;

public sealed class VideoEndpointsTests
{
    [Fact]
    public async Task Scan_returns_only_the_browser_safe_contract()
    {
        using var root = new TemporaryDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(root.Path, "secret-directory-name"));
        await File.WriteAllBytesAsync(Path.Combine(nested.FullName, "clip.MP4"), [1, 2, 3, 4]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/videos/scan", content: null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.DoesNotContain("secret-directory-name", json);
        using var document = JsonDocument.Parse(json);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ["durationSeconds", "extension", "height", "hoverPreviewState", "hoverPreviewUrl", "id", "name", "sizeBytes", "subtitleState", "subtitleUrl", "thumbnailState", "thumbnailUrl", "width"],
            item.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("clip.MP4", item.GetProperty("name").GetString());
        Assert.Equal(".mp4", item.GetProperty("extension").GetString());
        Assert.Equal(4, item.GetProperty("sizeBytes").GetInt64());
        Assert.Matches("^[0-9a-f]{32}$", item.GetProperty("id").GetString()!);
        Assert.Equal((int)ThumbnailState.Pending, item.GetProperty("thumbnailState").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("thumbnailUrl").ValueKind);
        Assert.Equal((int)HoverPreviewState.Pending, item.GetProperty("hoverPreviewState").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("hoverPreviewUrl").ValueKind);
        Assert.Equal((int)SubtitleState.Unavailable, item.GetProperty("subtitleState").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("subtitleUrl").ValueKind);
    }

    [Fact]
    public async Task Subtitle_endpoint_serves_ready_vtt_and_scan_reports_state()
    {
        using var root = new TemporaryDirectory();
        var videoPath = Path.Combine(root.Path, "clip.mp4");
        var subtitlePath = Path.Combine(root.Path, "clip.srt");
        await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        var pending = await ScanSingleAsync(client);

        Assert.Equal(SubtitleState.Pending, pending.SubtitleState);
        Assert.Null(pending.SubtitleUrl);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{pending.Id}/subtitle")).StatusCode);

        var cache = new SubtitleCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var entry = new WebApp.Models.VideoFileEntry(
            pending.Id,
            videoPath,
            "clip.mp4",
            "clip.mp4",
            ".mp4",
            3,
            File.GetLastWriteTimeUtc(videoPath));
        var subtitle = new WebApp.Models.SubtitleFileInfo(
            subtitlePath,
            new FileInfo(subtitlePath).Length,
            File.GetLastWriteTimeUtc(subtitlePath));
        await File.WriteAllTextAsync(cache.GetFinalPath(cache.ComputeKey(entry, subtitle)), "WEBVTT\n\n");

        var ready = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);
        Assert.Equal(SubtitleState.Ready, ready.SubtitleState);
        Assert.NotNull(ready.SubtitleUrl);

        using var subtitleResponse = await client.GetAsync(ready.SubtitleUrl);
        Assert.Equal(HttpStatusCode.OK, subtitleResponse.StatusCode);
        Assert.Equal("text/vtt", subtitleResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("WEBVTT\n\n", await subtitleResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/not-an-id/subtitle")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}/subtitle")).StatusCode);
    }

    [Fact]
    public async Task Status_endpoint_returns_empty_before_scan_and_stable_ids_after()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        var beforeScan = await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos");
        Assert.Empty(beforeScan!);

        var scanned = await ScanSingleAsync(client);
        var afterScan = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);

        Assert.Equal(scanned.Id, afterScan.Id);
    }

    [Fact]
    public async Task Thumbnail_endpoint_serves_ready_jpeg_and_rejects_everything_else()
    {
        using var root = new TemporaryDirectory();
        var file = Path.Combine(root.Path, "clip.mp4");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var video = await ScanSingleAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{video.Id}/thumbnail")).StatusCode);

        var cache = new ThumbnailCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var key = cache.ComputeKey("clip.mp4", 3, File.GetLastWriteTimeUtc(file));
        await File.WriteAllBytesAsync(cache.GetFinalPath(key), [9, 9, 9]);

        var ready = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);
        Assert.Equal(ThumbnailState.Ready, ready.ThumbnailState);
        Assert.NotNull(ready.ThumbnailUrl);

        using var thumbnailResponse = await client.GetAsync(ready.ThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal("image/jpeg", thumbnailResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 9, 9, 9 }, await thumbnailResponse.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/not-an-id/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/%2Fetc%2Fpasswd/thumbnail")).StatusCode);

        var rescanned = await ScanSingleAsync(client);
        Assert.Equal(video.Id, rescanned.Id);

        File.Delete(file);
        using var deletedScan = await client.PostAsync("/api/videos/scan", null);
        deletedScan.EnsureSuccessStatusCode();
        Assert.Empty((await deletedScan.Content.ReadFromJsonAsync<List<VideoItemDto>>())!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{rescanned.Id}/thumbnail")).StatusCode);
    }

    [Fact]
    public async Task Preview_endpoint_serves_ready_mp4_with_range_support_and_rejects_everything_else()
    {
        using var root = new TemporaryDirectory();
        var file = Path.Combine(root.Path, "clip.mp4");
        byte[] fixture = [9, 9, 9, 9, 9];
        await File.WriteAllBytesAsync(file, fixture);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var video = await ScanSingleAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{video.Id}/preview")).StatusCode);

        var cache = new HoverPreviewCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var key = cache.ComputeKey("clip.mp4", fixture.Length, File.GetLastWriteTimeUtc(file));
        await File.WriteAllBytesAsync(cache.GetFinalPath(key), fixture);

        var ready = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);
        Assert.Equal(HoverPreviewState.Ready, ready.HoverPreviewState);
        Assert.NotNull(ready.HoverPreviewUrl);

        using var previewResponse = await client.GetAsync(ready.HoverPreviewUrl);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("video/mp4", previewResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await previewResponse.Content.ReadAsByteArrayAsync());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, ready.HoverPreviewUrl);
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(fixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/not-an-id/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/%2Fetc%2Fpasswd/preview")).StatusCode);

        var rescanned = await ScanSingleAsync(client);
        Assert.Equal(video.Id, rescanned.Id);

        File.Delete(file);
        using var deletedScan = await client.PostAsync("/api/videos/scan", null);
        deletedScan.EnsureSuccessStatusCode();
        Assert.Empty((await deletedScan.Content.ReadFromJsonAsync<List<VideoItemDto>>())!);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{rescanned.Id}/preview")).StatusCode);
    }

    [Fact]
    public async Task Disabling_hover_preview_hides_it_everywhere_without_affecting_thumbnails()
    {
        using var root = new TemporaryDirectory();
        var file = Path.Combine(root.Path, "clip.mp4");
        byte[] fixture = [1, 2, 3];
        await File.WriteAllBytesAsync(file, fixture);
        using var factory = new VideoManagerFactory(root.Path, hoverPreviewEnabled: false);
        using var client = factory.CreateClient();
        var video = await ScanSingleAsync(client);

        var previewCache = new HoverPreviewCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var previewKey = previewCache.ComputeKey("clip.mp4", fixture.Length, File.GetLastWriteTimeUtc(file));
        await File.WriteAllBytesAsync(previewCache.GetFinalPath(previewKey), fixture);

        var thumbnailCache = new ThumbnailCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var thumbnailKey = thumbnailCache.ComputeKey("clip.mp4", fixture.Length, File.GetLastWriteTimeUtc(file));
        await File.WriteAllBytesAsync(thumbnailCache.GetFinalPath(thumbnailKey), fixture);

        var item = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);
        Assert.Equal(HoverPreviewState.Unavailable, item.HoverPreviewState);
        Assert.Null(item.HoverPreviewUrl);
        Assert.Equal(ThumbnailState.Ready, item.ThumbnailState);
        Assert.NotNull(item.ThumbnailUrl);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{video.Id}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync(item.ThumbnailUrl)).StatusCode);
    }

    [Fact]
    public async Task Stream_supports_full_and_byte_range_requests()
    {
        using var root = new TemporaryDirectory();
        byte[] fixture = [10, 20, 30, 40, 50, 60, 70];
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), fixture);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var video = Assert.Single((await (await client.PostAsync("/api/videos/scan", null))
            .Content.ReadFromJsonAsync<List<VideoItemDto>>())!);

        using var fullResponse = await client.GetAsync($"/api/videos/{video.Id}/stream");
        Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
        Assert.Equal("video/mp4", fullResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await fullResponse.Content.ReadAsByteArrayAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/videos/{video.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(2, 5);
        using var rangeResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Contains("bytes", rangeResponse.Headers.AcceptRanges);
        Assert.Equal("bytes 2-5/7", rangeResponse.Content.Headers.ContentRange?.ToString());
        Assert.Equal(fixture[2..6], await rangeResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Get_by_id_returns_the_same_shape_as_the_collection_without_a_full_rescan()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "clip.mp4"), [1, 2, 3, 4]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var scanned = await ScanSingleAsync(client);

        using var response = await client.GetAsync($"/api/videos/{scanned.Id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var video = await response.Content.ReadFromJsonAsync<VideoItemDto>();
        Assert.Equal(scanned.Id, video!.Id);
        Assert.Equal(scanned.Name, video.Name);
        Assert.Equal(scanned.SizeBytes, video.SizeBytes);
        Assert.DoesNotContain(root.Path, json);
    }

    [Fact]
    public async Task Get_by_id_scans_on_a_cold_miss_and_resolves_a_newly_added_file()
    {
        using var root = new TemporaryDirectory();
        var file = Path.Combine(root.Path, "clip.mp4");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        string expectedId;
        using (var scanningFactory = new VideoManagerFactory(root.Path))
        using (var scanningClient = scanningFactory.CreateClient())
        {
            expectedId = (await ScanSingleAsync(scanningClient)).Id;
        }

        using var coldFactory = new VideoManagerFactory(root.Path);
        using var coldClient = coldFactory.CreateClient();

        Assert.Empty((await coldClient.GetFromJsonAsync<List<VideoItemDto>>("/api/videos"))!);

        using var response = await coldClient.GetAsync($"/api/videos/{expectedId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var video = await response.Content.ReadFromJsonAsync<VideoItemDto>();
        Assert.Equal(expectedId, video!.Id);
    }

    [Fact]
    public async Task Get_by_id_returns_not_found_for_unresolvable_ids()
    {
        using var root = new TemporaryDirectory();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/not-an-id")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}")).StatusCode);
    }

    [Fact]
    public async Task Unknown_malformed_stale_deleted_and_path_like_ids_return_not_found()
    {
        using var root = new TemporaryDirectory();
        var file = Path.Combine(root.Path, "clip.webm");
        await File.WriteAllBytesAsync(file, [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var first = await ScanSingleAsync(client);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/not-an-id/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{Guid.NewGuid():N}/stream")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/videos/%2Fetc%2Fpasswd/stream")).StatusCode);

        var second = await ScanSingleAsync(client);
        Assert.Equal(first.Id, second.Id);

        File.Delete(file);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/videos/{second.Id}/stream")).StatusCode);
    }

    [Fact]
    public async Task Scan_failure_is_path_free_and_invalidates_the_previous_snapshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-api-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path, "clip.mov"), [1]);
            using var factory = new VideoManagerFactory(path);
            using var client = factory.CreateClient();
            var oldVideo = await ScanSingleAsync(client);
            Directory.Delete(path, recursive: true);

            using var failedScan = await client.PostAsync("/api/videos/scan", null);
            var problem = await failedScan.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.InternalServerError, failedScan.StatusCode);
            Assert.DoesNotContain(path, problem);
            Assert.DoesNotContain("clip.mov", problem);
            Assert.Equal(HttpStatusCode.NotFound,
                (await client.GetAsync($"/api/videos/{oldVideo.Id}/stream")).StatusCode);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Unapproved_host_header_is_rejected()
    {
        using var root = new TemporaryDirectory();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/videos/scan");
        request.Headers.Host = "remote.example";

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Configured_lan_host_header_is_allowed()
    {
        using var root = new TemporaryDirectory();
        using var factory = new VideoManagerFactory(root.Path, allowedNetworkHosts: "192.168.0.147");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/videos/scan");
        request.Headers.Host = "192.168.0.147:8080";

        using var response = await client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<VideoItemDto> ScanSingleAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/api/videos/scan", null);
        response.EnsureSuccessStatusCode();
        return Assert.Single((await response.Content.ReadFromJsonAsync<List<VideoItemDto>>())!);
    }

    private sealed class VideoManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPath;
        private readonly bool _hoverPreviewEnabled;
        private readonly string _cutPath;
        private readonly string _compositionPath;
        private readonly string _allowedNetworkHosts;

        public VideoManagerFactory(string rootPath, bool hoverPreviewEnabled = true, string allowedNetworkHosts = "")
        {
            _rootPath = rootPath;
            _hoverPreviewEnabled = hoverPreviewEnabled;
            _allowedNetworkHosts = allowedNetworkHosts;
            PreviewPath = Path.Combine(Path.GetTempPath(), $"video-manager-api-tests-preview-{Guid.NewGuid():N}");
            _cutPath = Path.Combine(Path.GetTempPath(), $"video-manager-api-tests-cuts-{Guid.NewGuid():N}");
            _compositionPath = Path.Combine(Path.GetTempPath(), $"video-manager-api-tests-composition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(PreviewPath);
            Directory.CreateDirectory(_cutPath);
            Directory.CreateDirectory(_compositionPath);
        }

        public string PreviewPath { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = Path.GetDirectoryName(_rootPath),
                    ["VideoLibrary:Path"] = _rootPath,
                    ["ThumbnailCache:Path"] = PreviewPath,
                    ["VideoCut:Path"] = _cutPath,
                    ["VideoComposition:Path"] = _compositionPath,
                    ["HoverPreview:Enabled"] = _hoverPreviewEnabled.ToString(),
                    ["AllowedNetworkHosts:Hosts"] = _allowedNetworkHosts,
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(PreviewPath))
            {
                Directory.Delete(PreviewPath, recursive: true);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-api-tests-{Guid.NewGuid():N}");
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
