using System.Net;
using System.Net.Http.Json;
using WebApp.Client.Models;
using WebApp.Client.Services;

namespace WebApp.Tests.Client;

public sealed class AntiforgeryDelegatingHandlerTests
{
    private sealed class FakeServer : HttpMessageHandler
    {
        private int _issued;
        public List<(HttpMethod Method, string Path, string? Token)> Requests { get; } = [];
        public Func<string?, bool> AcceptsToken { get; set; } = token => token == "token-1" || token == "token-2" || token == "token-3";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var token = request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null;
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, token));
            if (request.RequestUri.AbsolutePath == "/api/antiforgery")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AntiforgeryTokenDto($"token-{++_issued}")) });

            if (request.Method == HttpMethod.Get || AcceptsToken(token)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            var rejected = new HttpResponseMessage(HttpStatusCode.BadRequest);
            rejected.Headers.Add("X-Antiforgery-Failure", "true");
            return Task.FromResult(rejected);
        }
    }

    private static (HttpClient Client, FakeServer Server) Create(Func<string?, bool>? accepts = null)
    {
        var server = new FakeServer();
        if (accepts is not null) server.AcceptsToken = accepts;
        var client = new HttpClient(new AntiforgeryDelegatingHandler { InnerHandler = server }) { BaseAddress = new Uri("http://localhost/") };
        return (client, server);
    }

    [Fact]
    public async Task Safe_methods_pass_through_without_a_token_fetch()
    {
        var (client, server) = Create();

        using var response = await client.GetAsync("api/videos");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var request = Assert.Single(server.Requests);
        Assert.Null(request.Token);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Unsafe_methods_carry_the_request_token(string method)
    {
        var (client, server) = Create();

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "api/things") { Content = JsonContent.Create(new { }) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("/api/antiforgery", server.Requests[0].Path);
        Assert.Equal("token-1", server.Requests[1].Token);
    }

    [Fact]
    public async Task The_token_is_fetched_once_and_reused()
    {
        var (client, server) = Create();

        await client.PostAsJsonAsync("api/a", new { });
        await client.PostAsJsonAsync("api/b", new { });

        Assert.Equal(1, server.Requests.Count(request => request.Path == "/api/antiforgery"));
        Assert.All(server.Requests.Where(request => request.Path != "/api/antiforgery"), request => Assert.Equal("token-1", request.Token));
    }

    [Fact]
    public async Task A_rejected_buffered_request_is_retried_exactly_once_with_a_fresh_token()
    {
        var (client, server) = Create(token => token == "token-2");

        using var response = await client.PostAsJsonAsync("api/a", new { });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var posts = server.Requests.Where(request => request.Path == "/api/a").ToList();
        Assert.Equal(["token-1", "token-2"], posts.Select(post => post.Token));
    }

    [Fact]
    public async Task A_second_rejection_is_returned_without_a_third_attempt()
    {
        var (client, server) = Create(_ => false);

        using var response = await client.PostAsJsonAsync("api/a", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, server.Requests.Count(request => request.Path == "/api/a"));
    }

    [Fact]
    public async Task Multipart_bodies_are_never_retried()
    {
        var (client, server) = Create(token => token == "token-2");
        using var content = new MultipartFormDataContent { { new StringContent("x"), "field" } };

        using var response = await client.PostAsync("api/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, server.Requests.Count(request => request.Path == "/api/upload"));
    }

    [Fact]
    public async Task Streamed_bodies_are_never_retried()
    {
        var (client, server) = Create(token => token == "token-2");
        using var content = new StreamContent(new MemoryStream([1, 2, 3]));

        using var response = await client.PutAsync("api/chunk", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, server.Requests.Count(request => request.Path == "/api/chunk"));
    }

    [Fact]
    public async Task A_plain_400_without_the_failure_marker_is_not_retried()
    {
        var server = new FakeServer { AcceptsToken = _ => true };
        var plain = new HttpClient(new AntiforgeryDelegatingHandler { InnerHandler = new Plain400(server) }) { BaseAddress = new Uri("http://localhost/") };

        using var response = await plain.PostAsJsonAsync("api/a", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, server.Requests.Count(request => request.Path == "/api/a"));
    }

    private sealed class Plain400(FakeServer inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            return request.RequestUri!.AbsolutePath == "/api/antiforgery" ? response : new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public void Only_buffered_content_can_be_replayed()
    {
        Assert.True(AntiforgeryDelegatingHandler.CanReplay(null));
        Assert.True(AntiforgeryDelegatingHandler.CanReplay(new StringContent("x")));
        Assert.True(AntiforgeryDelegatingHandler.CanReplay(new ByteArrayContent([1])));
        Assert.True(AntiforgeryDelegatingHandler.CanReplay(new FormUrlEncodedContent([])));
        Assert.True(AntiforgeryDelegatingHandler.CanReplay(JsonContent.Create(new { })));
        Assert.False(AntiforgeryDelegatingHandler.CanReplay(new StreamContent(new MemoryStream())));
        Assert.False(AntiforgeryDelegatingHandler.CanReplay(new MultipartFormDataContent()));
    }
}
