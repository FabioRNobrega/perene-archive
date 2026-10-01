using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Identity;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Authorization;

public sealed class FolderAccessServiceTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly AccountFactory _factory;

    public FolderAccessServiceTests() => _factory = new AccountFactory(_root.Path);

    public void Dispose()
    {
        _factory.Dispose();
        _root.Dispose();
    }

    private async Task<string> MemberIdAsync(string name, bool admin = false)
    {
        await CreateMemberAsync(_factory, name, "password1", admin: admin);
        return (await FindUserAsync(_factory, name))!.Id;
    }

    private static async Task<Folder> AddFolderAsync(IServiceScope scope, string relativePath, FolderAccessMode mode = FolderAccessMode.Shared)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var books = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "");
        var folder = new Folder { RootKey = "books", RelativePath = relativePath, Label = relativePath, ParentId = books.Id, AccessMode = mode };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<AuthzVersionStore>().BumpGlobalAsync();
        return folder;
    }

    [Fact]
    public async Task Unknown_empty_and_deactivated_accounts_have_no_actor_and_no_access()
    {
        var aliceId = await MemberIdAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        var books = FolderLocation.Create("books", "");

        Assert.Null(await access.GetActorAsync(null));
        Assert.Null(await access.GetActorAsync("missing"));
        Assert.False(await access.CheckAsync("missing", FolderOperation.Read, books));
        Assert.Null(await access.GetReadableAsync("missing"));

        Assert.True(await access.CheckAsync(aliceId, FolderOperation.Read, books));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Users.SingleAsync(user => user.Id == aliceId)).IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await access.GetActorAsync(aliceId));
        Assert.False(await access.CheckAsync(aliceId, FolderOperation.Read, books));
    }

    [Fact]
    public async Task Admin_membership_is_read_from_the_database_and_overrides_everything()
    {
        var bossId = await MemberIdAsync("boss", admin: true);
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        await AddFolderAsync(scope, "Hidden", FolderAccessMode.Private);

        Assert.True((await access.GetActorAsync(bossId))!.IsAdmin);
        Assert.True(await access.CheckAsync(bossId, FolderOperation.Delete, FolderLocation.Create("books", "Hidden")));
        Assert.True(await access.CanReadAsync(bossId, FolderLocation.Create("books", "Hidden")));
    }

    [Fact]
    public async Task Members_get_shared_defaults_until_a_row_changes_them()
    {
        var aliceId = await MemberIdAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        var books = FolderLocation.Create("books", "");

        Assert.True(await access.CheckAsync(aliceId, FolderOperation.Read, books));
        Assert.False(await access.CheckAsync(aliceId, FolderOperation.Write, books));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var root = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "");
        db.FolderPermissions.Add(new FolderPermission { FolderId = root.Id, UserId = aliceId, Write = PermissionState.Allow });
        await db.SaveChangesAsync();

        // CheckAsync is the fresh path: it sees the new row without any version bump.
        Assert.True(await access.CheckAsync(aliceId, FolderOperation.Write, books));
    }

    [Fact]
    public async Task Private_folders_are_unreadable_and_only_their_readable_descendants_make_ancestors_pass_through()
    {
        var aliceId = await MemberIdAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        var hidden = await AddFolderAsync(scope, "Hidden", FolderAccessMode.Private);

        var readable = (await access.GetReadableAsync(aliceId))!;
        Assert.True(readable.IsReadable(FolderLocation.Create("books", "")));
        Assert.False(readable.IsReadable(FolderLocation.Create("books", "Hidden")));
        Assert.False(readable.IsReadable(FolderLocation.Create("books", "Hidden/Child")));

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FolderPermissions.Add(new FolderPermission { FolderId = hidden.Id, UserId = aliceId, Read = PermissionState.Allow });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<AuthzVersionStore>().BumpGlobalAsync();
        Assert.True(await access.CanReadAsync(aliceId, FolderLocation.Create("books", "Hidden")));
    }

    [Fact]
    public async Task The_readable_set_is_cached_until_either_version_changes()
    {
        var aliceId = await MemberIdAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        var versions = scope.ServiceProvider.GetRequiredService<AuthzVersionStore>();

        var first = await access.GetReadableAsync(aliceId);
        Assert.Same(first, await access.GetReadableAsync(aliceId));

        var before = await versions.GetGlobalAsync();
        await versions.BumpGlobalAsync();
        Assert.Equal(before + 1, await versions.GetGlobalAsync());
        var second = await access.GetReadableAsync(aliceId);
        Assert.NotSame(first, second);

        await versions.BumpUserAsync(aliceId);
        Assert.NotSame(second, await access.GetReadableAsync(aliceId));
    }

    [Fact]
    public async Task One_users_cache_is_not_shared_with_another()
    {
        var aliceId = await MemberIdAsync("alice");
        var bobId = await MemberIdAsync("bob");
        using var scope = _factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<FolderAccessService>();
        var versions = scope.ServiceProvider.GetRequiredService<AuthzVersionStore>();

        var alice = await access.GetReadableAsync(aliceId);
        var bob = await access.GetReadableAsync(bobId);
        await versions.BumpUserAsync(aliceId);

        Assert.NotSame(alice, await access.GetReadableAsync(aliceId));
        Assert.Same(bob, await access.GetReadableAsync(bobId));
    }
}
