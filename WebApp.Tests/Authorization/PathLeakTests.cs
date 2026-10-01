using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Authorization;

/// <summary>No physical path, root-relative path, or database/Identity ID may reach a browser payload, for members or admins.</summary>
public sealed class PathLeakTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly AccountFactory _factory;

    public PathLeakTests()
    {
        _factory = new AccountFactory(_root.Path);
        Directory.CreateDirectory(Path.Combine(_factory.ArchivePath, "Books", "Deep", "Nested"));
        File.WriteAllText(Path.Combine(_factory.ArchivePath, "Books", "Deep", "Nested", "leaf.txt"), "x");
    }

    public void Dispose()
    {
        _factory.Dispose();
        _root.Dispose();
    }

    [Fact]
    public async Task Listings_and_the_access_editor_expose_neither_paths_nor_ids()
    {
        await CreateMemberAsync(_factory, "boss", "password1", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        var aliceId = (await FindUserAsync(_factory, "alice"))!.Id;
        var bossId = (await FindUserAsync(_factory, "boss"))!.Id;
        using var admin = _factory.CreateClient(NoRedirect);
        using var alice = _factory.CreateClient(NoRedirect);
        await SignInAsync(admin, "boss", "password1");
        await SignInAsync(alice, "alice", "password1");

        var bodies = new List<string>();
        var books = await alice.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items");
        var deep = books!.Items.Single(item => item.Name == "Deep");
        bodies.Add(await alice.GetStringAsync("/api/archive/books/items"));
        bodies.Add(await alice.GetStringAsync($"/api/archive/books/items?folderId={deep.Id}"));
        bodies.Add(await alice.GetStringAsync("/api/videos"));
        bodies.Add(await alice.GetStringAsync("/api/cuts"));
        bodies.Add(await alice.GetStringAsync("/api/compositions"));
        bodies.Add(await admin.GetStringAsync("/api/account/users/alice/access"));
        bodies.Add(await admin.GetStringAsync("/api/account/users"));

        foreach (var body in bodies)
        {
            Assert.DoesNotContain(_factory.ArchivePath, body);
            Assert.DoesNotContain(_root.Path, body);
            Assert.DoesNotContain("Deep/Nested", body);
            Assert.DoesNotContain(aliceId, body);
            Assert.DoesNotContain(bossId, body);
        }
    }

    [Fact]
    public async Task Denied_and_missing_responses_do_not_echo_paths()
    {
        await CreateMemberAsync(_factory, "alice", "password1");
        using var alice = _factory.CreateClient(NoRedirect);
        await SignInAsync(alice, "alice", "password1");

        foreach (var path in new[] { "/api/archive/books/items/nope/stream", "/api/archive/books/items/nope/text", "/api/videos/nope/stream", "/api/cuts/nope/stream" })
        {
            using var response = await alice.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(_factory.ArchivePath, body);
            Assert.DoesNotContain("Books", body);
        }
    }
}
