using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Services;
using WebApp.Tests.Services;
using WebApp.Data.Entities;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Endpoints;

/// <summary>Per-user reader data through the real HTTP pipeline: authentication, folder Read, cross-user isolation, and the Admin-only import routes.</summary>
public sealed class PerUserDataEndpointTests : IDisposable
{
    private readonly MediaTestHost _host;
    private readonly string _backups = Path.Combine(Path.GetTempPath(), $"endpoint-backups-{Guid.NewGuid():N}");

    public PerUserDataEndpointTests()
    {
        Directory.CreateDirectory(_backups);
        _host = new MediaTestHost(new Dictionary<string, string?> { ["Backup:Path"] = _backups });
        Directory.CreateDirectory(_host.Factory.KeysPath);
        EpubTestFixture.CreateMinimalEpub(Path.Combine(_host.BooksPath, "novel.epub"), "My Book", "My Author");
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_backups)) Directory.Delete(_backups, recursive: true);
    }

    private async Task<HttpClient> SignedInAsync(string user, bool admin = false)
    {
        await CreateMemberAsync(_host.Factory, user, "password1", admin: admin);
        var client = _host.Factory.CreateClient(NoRedirect);
        await SignInAsync(client, user, "password1");
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        request.Headers.Add("X-CSRF-TOKEN", await GetApiTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> BookIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!.Items.Single(item => item.Name == "novel.epub").Id;

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_per_user_route()
    {
        using var anonymous = _host.Factory.CreateClient(NoRedirect);
        foreach (var path in new[]
        {
            "/api/archive/books/items/x/book/progress", "/api/archive/books/items/x/book/highlights", "/api/archive/books/items/x/comic/progress",
            "/api/books/reader-themes", "/api/dashboard/storage/custom", "/api/admin/legacy-import"
        })
        {
            using var response = await anonymous.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{path} returned {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Two_users_reading_the_same_book_keep_separate_progress_and_notes_and_share_themes()
    {
        using var alice = await SignedInAsync("alice");
        using var bob = await SignedInAsync("bob");
        var id = await BookIdAsync(alice);
        var progressUrl = $"/api/archive/books/items/{id}/book/progress";
        var chapter = (await alice.GetFromJsonAsync<BookChapterDto>($"/api/archive/books/items/{id}/book/chapters/0"))!;
        const string selected = "This is the first chapter";
        var start = EpubChapterText.Normalize(chapter.ContentHtml).IndexOf(selected, StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Put, progressUrl, new BookProgressDto("1", 750))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Post, $"/api/archive/books/items/{id}/book/notes", new BookNoteRequest("0", selected, start, start + selected.Length))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Post, "/api/books/reader-themes",
            new CreateBookReaderThemeRequest("Night", new BookReaderThemeSettingsDto("Arial", 24, 2.0, "#112233", "#AABBCC")))).StatusCode);

        Assert.Equal(750, (await alice.GetFromJsonAsync<BookProgressDto>(progressUrl))!.WordOffset);
        Assert.Single((await alice.GetFromJsonAsync<List<BookHighlightDto>>($"/api/archive/books/items/{id}/book/highlights"))!);

        Assert.Null(await bob.GetFromJsonAsync<BookProgressDto>(progressUrl));
        var bobHighlights = (await bob.GetFromJsonAsync<List<BookHighlightDto>>($"/api/archive/books/items/{id}/book/highlights"))!;
        Assert.Empty(bobHighlights);
        // Reader themes are the shared exception: Bob sees Alice's theme, but his active settings and the right to edit it are his own.
        var bobThemes = (await bob.GetFromJsonAsync<BookReaderThemeLibraryDto>("/api/books/reader-themes"))!;
        var shared = Assert.Single(bobThemes.Themes);
        Assert.Equal("Montserrat", bobThemes.ActiveSettings.FontFamily);
        Assert.Null(bobThemes.SelectedThemeId);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(bob, HttpMethod.Delete, $"/api/books/reader-themes/{shared.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Delete, $"/api/books/reader-themes/{shared.Id}")).StatusCode);
        var aliceHighlightId = (await alice.GetFromJsonAsync<List<BookHighlightDto>>($"/api/archive/books/items/{id}/book/highlights"))![0].Id;
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(bob, HttpMethod.Delete, $"/api/archive/books/items/{id}/book/notes/{aliceHighlightId}")).StatusCode);
        Assert.Single((await alice.GetFromJsonAsync<List<BookHighlightDto>>($"/api/archive/books/items/{id}/book/highlights"))!);
    }

    [Fact]
    public async Task Favorites_are_per_user_in_listings()
    {
        using var alice = await SignedInAsync("alice");
        using var bob = await SignedInAsync("bob");
        var id = await BookIdAsync(alice);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Put, $"/api/archive/books/items/{id}/favorite")).StatusCode);

        Assert.True((await alice.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!.Items.Single(item => item.Id == id).IsFavorite);
        Assert.False((await bob.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!.Items.Single(item => item.Id == id).IsFavorite);
    }

    [Fact]
    public async Task A_private_folder_makes_per_user_routes_404_without_losing_data()
    {
        using var alice = await SignedInAsync("alice");
        var id = await BookIdAsync(alice);
        var progressUrl = $"/api/archive/books/items/{id}/book/progress";
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(alice, HttpMethod.Put, progressUrl, new BookProgressDto("1", 12))).StatusCode);

        await _host.SetBooksPrivateAsync(true);
        using (var denied = await alice.GetAsync(progressUrl)) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using (var deniedHighlights = await alice.GetAsync($"/api/archive/books/items/{id}/book/highlights")) Assert.Equal(HttpStatusCode.NotFound, deniedHighlights.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(alice, HttpMethod.Put, progressUrl, new BookProgressDto("1", 99))).StatusCode);

        await _host.SetBooksPrivateAsync(false);
        Assert.Equal(12, (await alice.GetFromJsonAsync<BookProgressDto>(progressUrl))!.WordOffset);
    }

    [Fact]
    public async Task The_import_and_reconcile_routes_are_admin_only_and_return_counts_only()
    {
        using var anonymous = _host.Factory.CreateClient(NoRedirect);
        using var member = await SignedInAsync("alice");
        using var admin = await SignedInAsync("boss", admin: true);

        using (var response = await anonymous.PostAsync("/api/admin/legacy-import", null)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Post, "/api/admin/legacy-import")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(member, HttpMethod.Post, "/api/admin/media/reconcile")).StatusCode);
        using (var forbiddenRead = await member.GetAsync("/api/admin/legacy-import")) Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        using (var noToken = await admin.PostAsync("/api/admin/legacy-import", null)) Assert.True(noToken.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);

        using (var none = await admin.GetAsync("/api/admin/legacy-import")) Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);
        using var run = await SendAsync(admin, HttpMethod.Post, "/api/admin/legacy-import");
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var body = await run.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_host.ArchivePath, body);
        Assert.True((await run.Content.ReadFromJsonAsync<LegacyImportReportDto>())!.BackupVerified);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/legacy-import")).StatusCode);

        using var reconcile = await SendAsync(admin, HttpMethod.Post, "/api/admin/media/reconcile");
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);
        Assert.Equal(0, (await reconcile.Content.ReadFromJsonAsync<MediaReconcileReportDto>())!.MarkedMissing);
    }

    [Fact]
    public async Task The_import_route_conflicts_when_the_backup_cannot_be_verified()
    {
        var blocker = Path.Combine(_backups, "blocker");
        File.WriteAllText(blocker, "file");
        using var blocked = new MediaTestHost(new Dictionary<string, string?> { ["Backup:Path"] = blocker });
        Directory.CreateDirectory(blocked.Factory.KeysPath);
        await CreateMemberAsync(blocked.Factory, "boss", "password1", admin: true);
        using var admin = blocked.Factory.CreateClient(NoRedirect);
        await SignInAsync(admin, "boss", "password1");

        using var response = await SendAsync(admin, HttpMethod.Post, "/api/admin/legacy-import");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain(blocked.ArchivePath, await response.Content.ReadAsStringAsync());
        await blocked.WithDbAsync(async (_, db) => Assert.Empty(await db.LegacyImportRuns.ToListAsync()));
    }
}
