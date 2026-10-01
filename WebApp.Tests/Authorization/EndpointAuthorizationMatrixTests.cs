using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Authorization;

/// <summary>Walks every item-scoped derivative/range route: anonymous is 401, a hidden item is 404 whatever the route, and a mutation without rights is 403.</summary>
public sealed class EndpointAuthorizationMatrixTests : IDisposable
{
    private static readonly string[] ItemRoutes =
    [
        "stream", "thumbnail", "preview", "subtitle", "download", "audio", "cover", "image", "comic", "comic/progress",
        "folder-thumbnail", "playlist", "text", "pdf", "book", "book/cover", "book/highlights", "book/progress"
    ];

    private readonly TemporaryDirectory _root = new();
    private readonly AccountFactory _factory;

    public EndpointAuthorizationMatrixTests()
    {
        _factory = new AccountFactory(_root.Path);
        Directory.CreateDirectory(Path.Combine(_factory.ArchivePath, "Books", "Vault"));
        File.WriteAllText(Path.Combine(_factory.ArchivePath, "Books", "Vault", "secret.txt"), "secret");
        File.WriteAllText(Path.Combine(_factory.ArchivePath, "Books", "open.txt"), "open");
    }

    public void Dispose()
    {
        _factory.Dispose();
        _root.Dispose();
    }

    private async Task<HttpClient> SignedInAsync(string user, bool admin = false)
    {
        await CreateMemberAsync(_factory, user, "password1", admin: admin);
        var client = _factory.CreateClient(NoRedirect);
        await SignInAsync(client, user, "password1");
        return client;
    }

    private static async Task<ArchiveListingDto> ListAsync(HttpClient client, string? folderId = null) =>
        (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items" + (folderId is null ? "" : $"?folderId={folderId}")))!;

    private async Task HideVaultAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var books = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "");
        db.Folders.Add(new WebApp.Data.Entities.Folder
        {
            RootKey = "books", RelativePath = "Vault", Label = "Vault", ParentId = books.Id, AccessMode = WebApp.Data.Entities.FolderAccessMode.Private
        });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<AuthzVersionStore>().BumpGlobalAsync();
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_item_route()
    {
        using var anonymous = _factory.CreateClient(NoRedirect);
        foreach (var route in ItemRoutes)
        {
            using var response = await anonymous.GetAsync($"/api/archive/books/items/any/{route}");
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{route} returned {response.StatusCode}");
        }

        foreach (var path in new[] { "/api/videos/any/stream", "/api/videos/any/thumbnail", "/api/cuts/any/stream", "/api/compositions/any/stream", "/api/compositions/any/thumbnail" })
        {
            using var response = await anonymous.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path} returned {response.StatusCode}");
        }
    }

    [Fact]
    public async Task A_hidden_item_is_404_on_every_route_never_403()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var vaultId = (await ListAsync(alice)).Items.Single(item => item.Name == "Vault").Id;
        var secretId = (await ListAsync(alice, vaultId)).Items.Single(item => item.Name == "secret.txt").Id;
        await HideVaultAsync();

        foreach (var route in ItemRoutes)
        {
            using var response = await alice.GetAsync($"/api/archive/books/items/{secretId}/{route}");
            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{route} returned {response.StatusCode} for a hidden item");
        }

        using var asAdmin = await admin.GetAsync($"/api/archive/books/items/{secretId}/text");
        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
    }

    [Fact]
    public async Task Readable_items_without_the_operation_are_403_for_mutations()
    {
        using var alice = await SignedInAsync("alice");
        var openId = (await ListAsync(alice)).Items.Single(item => item.Name == "open.txt").Id;
        var token = await GetApiTokenAsync(alice);

        foreach (var (method, path, body) in new (HttpMethod, string, object)[]
        {
            (HttpMethod.Put, $"/api/archive/books/items/{openId}/text", new { content = "changed" }),
            (HttpMethod.Patch, $"/api/archive/books/items/{openId}/name", new RenameArchiveItemRequest("x.txt")),
            (HttpMethod.Delete, $"/api/archive/books/items/{openId}", new { }),
            (HttpMethod.Post, "/api/archive/books/folders", new CreateFolderRequest(null, "New"))
        })
        {
            using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using var response = await alice.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{method} {path} returned {response.StatusCode}");
        }

        Assert.Equal("open", await File.ReadAllTextAsync(Path.Combine(_factory.ArchivePath, "Books", "open.txt")));
    }

    [Fact]
    public async Task Admin_routes_are_401_for_anonymous_and_403_for_members()
    {
        using var anonymous = _factory.CreateClient(NoRedirect);
        using var alice = await SignedInAsync("alice");

        using var anonymousResponse = await anonymous.GetAsync("/api/account/users/alice/access");
        using var memberResponse = await alice.GetAsync("/api/account/users/alice/access");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberResponse.StatusCode);
    }
}
