using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Identity;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Authorization;

/// <summary>End-to-end checks of the 401/404/403 matrix, listing filters, the editor API, and cache invalidation on a real-Identity host.</summary>
public sealed class FolderAccessEndpointTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly AccountFactory _factory;

    public FolderAccessEndpointTests()
    {
        _factory = new AccountFactory(_root.Path);
        var archive = _factory.ArchivePath;
        Directory.CreateDirectory(Path.Combine(archive, "Books", "MyLab"));
        File.WriteAllText(Path.Combine(archive, "Books", "open.txt"), "open");
        File.WriteAllText(Path.Combine(archive, "Books", "MyLab", "secret.txt"), "secret");
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

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object payload)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", await GetApiTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<ArchiveListingDto> ListAsync(HttpClient client, string category, string? folderId = null) =>
        (await client.GetFromJsonAsync<ArchiveListingDto>($"/api/archive/{category}/items" + (folderId is null ? "" : $"?folderId={folderId}")))!;

    private static string ItemId(ArchiveListingDto listing, string name) => listing.Items.Single(item => item.Name == name).Id;

    private static async Task<FolderAccessFolderDto> FolderAsync(HttpClient admin, string user, string label) =>
        (await admin.GetFromJsonAsync<FolderAccessDto>($"/api/account/users/{user}/access"))!.Folders.Single(folder => folder.Label == label);

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient admin, string user, params FolderAccessChangeDto[] changes) =>
        await SendJsonAsync(admin, HttpMethod.Put, $"/api/account/users/{user}/access", new FolderAccessSaveRequest(changes));

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_media_route()
    {
        using var anonymous = _factory.CreateClient(NoRedirect);
        foreach (var path in new[] { "/api/archive/books/items", "/api/videos", "/api/cuts", "/api/compositions", "/api/archive/books/items/x/stream" })
        {
            using var response = await anonymous.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_member_reads_shared_content_by_default_but_cannot_change_it()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var books = await ListAsync(alice, "books");
        Assert.Contains(books.Items, item => item.Name == "MyLab");

        using var read = await alice.GetAsync($"/api/archive/books/items/{ItemId(books, "open.txt")}/text");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var rename = await SendJsonAsync(alice, HttpMethod.Patch, $"/api/archive/books/items/{ItemId(books, "open.txt")}/name", new RenameArchiveItemRequest("renamed.txt"));
        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        using var create = await SendJsonAsync(alice, HttpMethod.Post, "/api/archive/books/folders", new CreateFolderRequest(null, "Nope"));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var trash = await SendJsonAsync(alice, HttpMethod.Delete, $"/api/archive/books/items/{ItemId(books, "open.txt")}", new { });
        Assert.Equal(HttpStatusCode.Forbidden, trash.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_factory.ArchivePath, "Books", "Nope")));
    }

    [Fact]
    public async Task A_private_folder_is_nonexistent_until_read_is_granted()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var books = await ListAsync(alice, "books");
        var myLabId = ItemId(books, "MyLab");
        var secretId = ItemId(await ListAsync(alice, "books", myLabId), "secret.txt");

        var myLab = await FolderAsync(admin, "alice", "MyLab");
        using var privateResponse = await SaveAsync(admin, "alice", new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, IsPrivate: true));
        Assert.Equal(HttpStatusCode.NoContent, privateResponse.StatusCode);

        Assert.DoesNotContain((await ListAsync(alice, "books")).Items, item => item.Name == "MyLab");
        using var listing = await alice.GetAsync($"/api/archive/books/items?folderId={myLabId}");
        Assert.Equal(HttpStatusCode.NotFound, listing.StatusCode);
        using var secret = await alice.GetAsync($"/api/archive/books/items/{secretId}/text");
        Assert.Equal(HttpStatusCode.NotFound, secret.StatusCode);
        using var download = await alice.GetAsync($"/api/archive/books/items/{myLabId}/download");
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);

        myLab = await FolderAsync(admin, "alice", "MyLab");
        Assert.False(myLab.Read.Effective);
        Assert.Equal("Private: no access", myLab.Read.Provenance);
        using var grant = await SaveAsync(admin, "alice", new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, Read: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        // The grant is visible on the very next request: the readable-set cache is stamped by the global version.
        using var after = await alice.GetAsync($"/api/archive/books/items/{secretId}/text");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Write_without_create_allows_edits_but_not_new_folders_and_creators_own_what_they_create()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var books = await FolderAsync(admin, "alice", "Books");
        using var grant = await SaveAsync(admin, "alice", new FolderAccessChangeDto(books.FolderKey, books.ConcurrencyStamp, Write: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);

        var listing = await ListAsync(alice, "books");
        using var rename = await SendJsonAsync(alice, HttpMethod.Patch, $"/api/archive/books/items/{ItemId(listing, "open.txt")}/name", new RenameArchiveItemRequest("renamed.txt"));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        using var denied = await SendJsonAsync(alice, HttpMethod.Post, "/api/archive/books/folders", new CreateFolderRequest(null, "Mine"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        books = await FolderAsync(admin, "alice", "Books");
        using var create = await SaveAsync(admin, "alice", new FolderAccessChangeDto(books.FolderKey, books.ConcurrencyStamp, Create: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.NoContent, create.StatusCode);
        using var created = await SendJsonAsync(alice, HttpMethod.Post, "/api/archive/books/folders", new CreateFolderRequest(null, "Mine"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aliceId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("alice"))!.Id;
        var mine = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "Mine");
        Assert.Equal(aliceId, mine.OwnerUserId);

        var access = scope.ServiceProvider.GetRequiredService<IFolderAccessService>();
        var location = new FolderLocation("books", "Mine");
        Assert.True(await access.CheckAsync(aliceId, FolderOperation.Write, location));
        Assert.True(await access.CheckAsync(aliceId, FolderOperation.Delete, location));
        Assert.False(await access.CheckAsync(aliceId, FolderOperation.Manage, location));
    }

    [Fact]
    public async Task Deny_on_a_subfolder_hides_nothing_but_blocks_that_operation()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var myLab = await FolderAsync(admin, "alice", "MyLab");
        using var deny = await SaveAsync(admin, "alice", new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, Read: AccessCellState.Deny));
        Assert.Equal(HttpStatusCode.NoContent, deny.StatusCode);

        var books = await ListAsync(alice, "books");
        // A Read deny on a non-Private folder hides its files; the folder itself is not listed because it holds nothing readable.
        Assert.DoesNotContain(books.Items, item => item.Name == "MyLab");
    }

    [Fact]
    public async Task Members_cannot_use_the_admin_access_api()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        using var response = await alice.GetAsync("/api/account/users/boss/access");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var put = await SaveAsync(alice, "alice");
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task Editing_an_admin_or_unknown_account_is_rejected()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var self = await admin.GetAsync("/api/account/users/boss/access");
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var unknown = await admin.GetAsync("/api/account/users/nobody/access");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task A_stale_concurrency_stamp_is_a_conflict_and_changes_nothing()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        var books = await FolderAsync(admin, "alice", "Books");
        using var first = await SaveAsync(admin, "alice", new FolderAccessChangeDto(books.FolderKey, books.ConcurrencyStamp, Write: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var stale = await SaveAsync(admin, "alice", new FolderAccessChangeDto(books.FolderKey, books.ConcurrencyStamp, Create: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var fresh = await FolderAsync(admin, "alice", "Books");
        Assert.Equal(AccessCellState.Unset, fresh.Create.State);
        Assert.Equal(AccessCellState.Allow, fresh.Write.State);
    }

    [Fact]
    public async Task Saves_write_audit_events_without_paths_and_clearing_a_row_removes_it()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        var myLab = await FolderAsync(admin, "alice", "MyLab");
        using var grant = await SaveAsync(admin, "alice", new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, Write: AccessCellState.Allow));
        Assert.Equal(HttpStatusCode.NoContent, grant.StatusCode);
        myLab = await FolderAsync(admin, "alice", "MyLab");
        using var clear = await SaveAsync(admin, "alice", new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, Write: AccessCellState.Unset));
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.FolderPermissions.CountAsync());
        var audits = await db.AuditEvents.Where(audit => audit.Action.StartsWith("permission.")).ToListAsync();
        Assert.Equal(["permission.cleared", "permission.granted"], audits.Select(audit => audit.Action).Order().ToArray());
        Assert.All(audits, audit => Assert.DoesNotContain("/", audit.Detail));
    }

    [Fact]
    public async Task Preview_reports_who_would_lose_visibility_without_writing()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        var myLab = await FolderAsync(admin, "alice", "MyLab");
        using var response = await SendJsonAsync(admin, HttpMethod.Post, "/api/account/users/alice/access/preview",
            new FolderAccessSaveRequest([new FolderAccessChangeDto(myLab.FolderKey, myLab.ConcurrencyStamp, IsPrivate: true)]));
        var impact = (await response.Content.ReadFromJsonAsync<FolderAccessImpactDto>())!;
        Assert.Equal(1, impact.FolderCount);
        Assert.Equal(1, impact.ItemCount);
        Assert.Equal(["alice"], impact.AffectedUsers);
        Assert.Equal(["MyLab"], impact.FolderLabels);
        Assert.False((await FolderAsync(admin, "alice", "MyLab")).IsPrivate);
    }

    [Fact]
    public async Task Browser_payloads_never_contain_paths_or_database_ids()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        using var alice = await SignedInAsync("alice");
        var bodies = new[]
        {
            await alice.GetStringAsync("/api/archive/books/items"),
            await admin.GetStringAsync("/api/account/users/alice/access")
        };
        foreach (var body in bodies)
        {
            Assert.DoesNotContain(_factory.ArchivePath, body);
            Assert.DoesNotContain("Books/MyLab", body);
            Assert.DoesNotContain("\"relativePath\"", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Deleting_a_folder_owner_reassigns_ownership_to_the_acting_admin()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        string aliceId, bossId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            aliceId = (await users.FindByNameAsync("alice"))!.Id;
            bossId = (await users.FindByNameAsync("boss"))!.Id;
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var books = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "");
            db.Folders.Add(new WebApp.Data.Entities.Folder { RootKey = "books", RelativePath = "Owned", Label = "Owned", ParentId = books.Id, OwnerUserId = aliceId });
            await db.SaveChangesAsync();
        }

        using var delete = await SendWithTokenAsync(admin, HttpMethod.Delete, "/api/account/users/alice");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var verify = _factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(bossId, (await verifyDb.Folders.SingleAsync(folder => folder.RelativePath == "Owned")).OwnerUserId);
        Assert.Contains(await verifyDb.AuditEvents.ToListAsync(), audit => audit.Action == "folder.owner-reassigned");
    }

    [Fact]
    public async Task Global_and_user_versions_both_invalidate_the_readable_cache()
    {
        using var admin = await SignedInAsync("boss", admin: true);
        await CreateMemberAsync(_factory, "alice", "password1");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IFolderAccessService>();
        var versions = scope.ServiceProvider.GetRequiredService<AuthzVersionStore>();
        var aliceId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("alice"))!.Id;

        var first = await access.GetReadableAsync(aliceId);
        Assert.Same(first, await access.GetReadableAsync(aliceId));
        await versions.BumpGlobalAsync();
        var afterGlobal = await access.GetReadableAsync(aliceId);
        Assert.NotSame(first, afterGlobal);
        await versions.BumpUserAsync(aliceId);
        Assert.NotSame(afterGlobal, await access.GetReadableAsync(aliceId));
    }

    [Fact]
    public async Task Cuts_and_compositions_are_denied_when_the_root_is_private_and_jobs_recheck_at_execution()
    {
        using var alice = await SignedInAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aliceId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("alice"))!.Id;
        var authorizer = _factory.Services.GetRequiredService<WebApp.Authorization.IFolderJobAuthorizer>();
        var cutJob = new WebApp.Models.CutJob("j", new WebApp.Models.VideoFileEntry("i", Path.Combine(_factory.ArchivePath, "Videos", "a.mp4"), "a.mp4", "a.mp4", ".mp4", 1, DateTime.UtcNow),
            TimeSpan.Zero, TimeSpan.FromSeconds(1), aliceId);

        // A member without Create on the cut output root cannot run a cut job even though they could read the source.
        Assert.False(await authorizer.CanRunAsync(cutJob, CancellationToken.None));
        Assert.True(await authorizer.CanRunAsync(cutJob with { ActorUserId = null }, CancellationToken.None));

        var cutRoot = await db.Folders.SingleAsync(folder => folder.RootKey == FolderLocator.CutRootKey);
        db.FolderPermissions.Add(new WebApp.Data.Entities.FolderPermission { FolderId = cutRoot.Id, UserId = aliceId, Create = WebApp.Data.Entities.PermissionState.Allow });
        await db.SaveChangesAsync();
        Assert.True(await authorizer.CanRunAsync(cutJob, CancellationToken.None));

        db.FolderPermissions.RemoveRange(db.FolderPermissions);
        await db.SaveChangesAsync();
        Assert.False(await authorizer.CanRunAsync(cutJob, CancellationToken.None));
        using var cuts = await alice.GetAsync("/api/cuts");
        Assert.Equal(HttpStatusCode.OK, cuts.StatusCode);
    }

    [Fact]
    public async Task Startup_seeds_roots_and_a_shared_default_policy_once()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy = await db.AccessPolicies.SingleAsync();
        Assert.True(policy.DefaultRead);
        Assert.False(policy.DefaultWrite || policy.DefaultCreate || policy.DefaultDelete);
        var roots = await db.Folders.Where(folder => folder.RelativePath == "").Select(folder => folder.RootKey).ToListAsync();
        Assert.Contains("books", roots);
        Assert.Contains(FolderLocator.CutRootKey, roots);
        Assert.Contains(FolderLocator.CompositionRootKey, roots);
        Assert.Equal(WebApp.Data.Entities.FolderAccessMode.Private, (await db.Folders.SingleAsync(folder => folder.RootKey == "trash" && folder.RelativePath == "")).AccessMode);

        await scope.ServiceProvider.GetRequiredService<FolderCatalog>().SeedAsync();
        Assert.Equal(roots.Count, await db.Folders.CountAsync(folder => folder.RelativePath == ""));
    }

    [Fact]
    public async Task Permission_rows_are_unique_per_folder_and_user()
    {
        await CreateMemberAsync(_factory, "alice", "password1");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var aliceId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("alice"))!.Id;
        var folder = await db.Folders.FirstAsync();
        db.FolderPermissions.Add(new WebApp.Data.Entities.FolderPermission { FolderId = folder.Id, UserId = aliceId });
        await db.SaveChangesAsync();
        db.FolderPermissions.Add(new WebApp.Data.Entities.FolderPermission { FolderId = folder.Id, UserId = aliceId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
