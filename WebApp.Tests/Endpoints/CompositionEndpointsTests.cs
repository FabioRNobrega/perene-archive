using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using WebApp.Client.Models;

namespace WebApp.Tests.Endpoints;

public sealed class CompositionEndpointsTests
{
    [Fact]
    public async Task Compositions_endpoint_returns_browser_safe_contract_and_streams_ranges()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        byte[] fixture = [10, 20, 30, 40, 50];
        await File.WriteAllBytesAsync(Path.Combine(composition.Path, "Jennifer White Composition 0001.mp4"), fixture);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path, composition.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/compositions");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.DoesNotContain(cuts.Path, json);
        Assert.DoesNotContain(composition.Path, json);

        var item = Assert.Single((await response.Content.ReadFromJsonAsync<List<VideoItemDto>>())!);
        using var fullResponse = await client.GetAsync($"/api/compositions/{item.Id}/stream");
        Assert.Equal(HttpStatusCode.OK, fullResponse.StatusCode);
        Assert.Equal(fixture, await fullResponse.Content.ReadAsByteArrayAsync());

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/compositions/{item.Id}/stream");
        request.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(fixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/compositions/{Guid.NewGuid():N}/stream")).StatusCode);
    }

    [Fact]
    public async Task Create_composition_requires_at_least_two_resolvable_cut_ids()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Jennifer White 0001.mp4"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Jennifer White 0002.mp4"), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path, composition.Path);
        using var client = factory.CreateClient();

        var available = await client.GetFromJsonAsync<List<VideoItemDto>>("/api/cuts");
        Assert.Equal(2, available!.Count);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/compositions", new CreateCompositionRequest([available[0].Id]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/compositions", new CreateCompositionRequest([]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(
                "/api/compositions", new CreateCompositionRequest([available[0].Id, Guid.NewGuid().ToString("N")]))).StatusCode);

        using var accepted = await client.PostAsJsonAsync(
            "/api/compositions", new CreateCompositionRequest([available[0].Id, available[1].Id]));

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Contains("jobId", await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Enqueued_composition_job_is_immediately_visible_as_pending()
    {
        using var root = new TemporaryDirectory();
        using var cuts = new TemporaryDirectory();
        using var composition = new TemporaryDirectory();
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Jennifer White 0001.mp4"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(cuts.Path, "Jennifer White 0002.mp4"), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path, cuts.Path, composition.Path);
        using var client = factory.CreateClient();
        var available = await client.GetFromJsonAsync<List<VideoItemDto>>("/api/cuts");

        using var accepted = await client.PostAsJsonAsync(
            "/api/compositions", new CreateCompositionRequest([available![0].Id, available[1].Id]));
        using var acceptedDocument = JsonDocument.Parse(await accepted.Content.ReadAsStringAsync());
        var jobId = acceptedDocument.RootElement.GetProperty("jobId").GetString();

        var jobs = await client.GetFromJsonAsync<List<CompositionJobDto>>("/api/compositions/jobs");

        Assert.Contains(jobs!, job => job.JobId == jobId);
    }

    private sealed class VideoManagerFactory(string rootPath, string cutPath, string compositionPath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSecurity.Apply(builder);
            var previewPath = Path.Combine(Path.GetTempPath(), $"video-manager-composition-api-preview-{Guid.NewGuid():N}");
            Directory.CreateDirectory(previewPath);

            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = rootPath,
                    ["VideoLibrary:Path"] = rootPath,
                    ["ThumbnailCache:Path"] = previewPath,
                    ["VideoCut:Path"] = cutPath,
                    ["VideoComposition:Path"] = compositionPath,
                    ["HoverPreview:Enabled"] = "false",
                }));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-composition-api-{Guid.NewGuid():N}");
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
