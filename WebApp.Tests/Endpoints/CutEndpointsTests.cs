using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Services;

namespace WebApp.Tests.Endpoints;

public sealed class CutEndpointsTests
{
    [Fact]
    public async Task Cuts_endpoint_returns_browser_safe_contract_and_streams_ranges()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        byte[] fixture = [10, 20, 30, 40, 50];
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Jennifer White 0001.mp4"), fixture);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/cuts");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.DoesNotContain(cuts.Path, json);
        using var document = JsonDocument.Parse(json);
        var item = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            ["audioTracks", "durationSeconds", "extension", "height", "hoverPreviewState", "hoverPreviewUrl", "id", "name", "sizeBytes", "subtitleState", "subtitleUrl", "thumbnailState", "thumbnailUrl", "width"],
            item.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("Jennifer White 0001.mp4", item.GetProperty("name").GetString());
        Assert.Equal((int)ThumbnailState.Pending, item.GetProperty("thumbnailState").GetInt32());
        Assert.Equal((int)HoverPreviewState.Pending, item.GetProperty("hoverPreviewState").GetInt32());
        Assert.Equal((int)SubtitleState.Unavailable, item.GetProperty("subtitleState").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("subtitleUrl").ValueKind);

        var cut = Assert.Single((await response.Content.ReadFromJsonAsync<List<VideoItemDto>>())!);
        using var fullResponse = await client.GetAsync($"/api/cuts/{cut.Id}/stream");
        Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
        Assert.Equal(fixture, await fullResponse.Content.ReadAsByteArrayAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/cuts/{cut.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Contains("bytes", rangeResponse.Headers.AcceptRanges);
        Assert.Equal(fixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/cuts/{Guid.NewGuid():N}/stream")).StatusCode);
    }

    [Fact]
    public async Task Cut_thumbnail_and_preview_endpoints_serve_ready_generated_assets()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        var cutPath = Path.Combine(cuts.Path, "Jennifer White 0001.mp4");
        byte[] fixture = [10, 20, 30, 40, 50];
        await File.WriteAllBytesAsync(cutPath, fixture);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path);
        using var client = factory.CreateClient();
        var pending = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/cuts"))!);

        var thumbnailCache = new ThumbnailCache(Microsoft.Extensions.Options.Options.Create(
            new WebApp.Configuration.ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var hoverCache = new HoverPreviewCache(Microsoft.Extensions.Options.Options.Create(
            new WebApp.Configuration.ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var timestamp = File.GetLastWriteTimeUtc(cutPath);
        await File.WriteAllBytesAsync(thumbnailCache.GetFinalPath(thumbnailCache.ComputeKey("Jennifer White 0001.mp4", fixture.Length, timestamp)), [1, 2, 3]);
        await File.WriteAllBytesAsync(hoverCache.GetFinalPath(hoverCache.ComputeKey("Jennifer White 0001.mp4", fixture.Length, timestamp)), fixture);

        var ready = Assert.Single((await client.GetFromJsonAsync<List<VideoItemDto>>("/api/cuts"))!);

        Assert.Equal(ThumbnailState.Ready, ready.ThumbnailState);
        Assert.Equal(HoverPreviewState.Ready, ready.HoverPreviewState);
        Assert.NotNull(ready.ThumbnailUrl);
        Assert.NotNull(ready.HoverPreviewUrl);
        Assert.StartsWith("/api/cuts/", ready.ThumbnailUrl);
        Assert.StartsWith("/api/cuts/", ready.HoverPreviewUrl);

        using var thumbnailResponse = await client.GetAsync(ready.ThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal("image/jpeg", thumbnailResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await thumbnailResponse.Content.ReadAsByteArrayAsync());

        using var previewResponse = await client.GetAsync(ready.HoverPreviewUrl);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("video/mp4", previewResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await previewResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Create_cut_validates_id_and_range_before_enqueueing()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Source Clip.mp4"), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path, TimeSpan.FromSeconds(10));
        using var client = factory.CreateClient();
        var source = Assert.Single((await (await client.PostAsync("/api/videos/scan", null))
            .Content.ReadFromJsonAsync<List<VideoItemDto>>())!);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/videos/{source.Id}/cuts", new { start = 5, end = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/videos/{source.Id}/cuts", new { start = -1, end = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync($"/api/videos/{source.Id}/cuts", new { start = 1, end = 20 })).StatusCode);

        using var accepted = await client.PostAsJsonAsync($"/api/videos/{source.Id}/cuts", new { start = 1, end = 2 });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Contains("jobId", await accepted.Content.ReadAsStringAsync());
        var unknownId = Guid.NewGuid().ToString("N");
        using var unknown = await client.PostAsync(
            $"/api/videos/{unknownId}/cuts",
            new StringContent("""{"start":1,"end":2}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    private sealed class VideoManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _rootPath;
        private readonly string _cutPath;
        private readonly string _previewPath;
        private readonly string _compositionPath;
        private readonly TimeSpan? _duration;
        private readonly bool _hoverPreviewEnabled;

        public VideoManagerFactory(string rootPath, string cutPath, TimeSpan? duration = null, bool hoverPreviewEnabled = true)
        {
            _rootPath = rootPath;
            _cutPath = cutPath;
            _duration = duration;
            _hoverPreviewEnabled = hoverPreviewEnabled;
            _previewPath = Path.Combine(Path.GetTempPath(), $"video-manager-cut-api-preview-{Guid.NewGuid():N}");
            _compositionPath = Path.Combine(Path.GetTempPath(), $"video-manager-cut-api-composition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_previewPath);
            Directory.CreateDirectory(_compositionPath);
        }

        public string PreviewPath => _previewPath;

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
                    ["HoverPreview:Enabled"] = _hoverPreviewEnabled.ToString(),
                }));

            if (_duration is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IVideoDurationProbe>(new FixedDurationProbe(_duration));
                });
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_previewPath))
            {
                Directory.Delete(_previewPath, recursive: true);
            }
            if (disposing && Directory.Exists(_compositionPath))
            {
                Directory.Delete(_compositionPath, recursive: true);
            }
        }
    }

    private sealed class FixedDurationProbe(TimeSpan? duration) : IVideoDurationProbe
    {
        public Task<TimeSpan?> GetDurationAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult(duration);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-cut-api-{Guid.NewGuid():N}");
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
