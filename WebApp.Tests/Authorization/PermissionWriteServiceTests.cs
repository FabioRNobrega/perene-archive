using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Authorization;

public sealed class PermissionWriteServiceTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();
    private readonly AccountFactory _factory;
    private readonly string _booksKey = FolderKeys.Of("books", "");

    public PermissionWriteServiceTests() => _factory = new AccountFactory(_root.Path);

    public void Dispose()
    {
        _factory.Dispose();
        _root.Dispose();
    }

    private async Task<string> UserAsync(string name, bool admin = false)
    {
        await CreateMemberAsync(_factory, name, "password1", admin: admin);
        return (await FindUserAsync(_factory, name))!.Id;
    }

    private static Task<PermissionWriteResult> ApplyAsync(IServiceScope scope, string actorId, string target, params FolderAccessChangeDto[] changes) =>
        scope.ServiceProvider.GetRequiredService<PermissionWriteService>().ApplyAsync(actorId, target, changes);

    [Fact]
    public async Task An_admin_grant_creates_a_row_bumps_the_global_version_and_audits_without_paths()
    {
        var bossId = await UserAsync("boss", admin: true);
        var aliceId = await UserAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<AuthzVersionStore>();
        var before = await versions.GetGlobalAsync();

        var result = await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, Write: AccessCellState.Allow));

        Assert.True(result.Succeeded);
        Assert.Equal(before + 1, await versions.GetGlobalAsync());
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.FolderPermissions.SingleAsync(permission => permission.UserId == aliceId);
        Assert.Equal(PermissionState.Allow, row.Write);
        Assert.Equal(bossId, row.GrantedByUserId);
        var audit = await db.AuditEvents.SingleAsync(item => item.Action == "permission.granted");
        Assert.Equal("alice", audit.TargetUserName);
        Assert.DoesNotContain(_factory.ArchivePath, audit.Detail);
    }

    [Fact]
    public async Task Clearing_every_cell_removes_the_row()
    {
        var bossId = await UserAsync("boss", admin: true);
        await UserAsync("alice");
        using var scope = _factory.Services.CreateScope();
        Assert.True((await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, Write: AccessCellState.Allow))).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stamp = (await db.FolderPermissions.AsNoTracking().SingleAsync()).ConcurrencyStamp;

        var result = await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, stamp, Write: AccessCellState.Unset));

        Assert.True(result.Succeeded);
        Assert.Empty(await db.FolderPermissions.ToListAsync());
    }

    [Fact]
    public async Task Unknown_targets_admin_targets_and_bad_change_sets_are_rejected_without_writes()
    {
        var bossId = await UserAsync("boss", admin: true);
        await UserAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var change = new FolderAccessChangeDto(_booksKey, null, Read: AccessCellState.Allow);

        Assert.Equal(PermissionWriteOutcome.NotFound, (await ApplyAsync(scope, bossId, "ghost", change)).Outcome);
        Assert.Equal(PermissionWriteOutcome.Invalid, (await ApplyAsync(scope, bossId, "boss", change)).Outcome);
        Assert.Equal(PermissionWriteOutcome.Invalid, (await ApplyAsync(scope, bossId, "alice", change, change)).Outcome);
        Assert.Equal(PermissionWriteOutcome.Invalid, (await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto("nonexistent", null, Read: AccessCellState.Allow))).Outcome);
        Assert.Equal(PermissionWriteOutcome.Forbidden, (await ApplyAsync(scope, "missing-actor", "alice", change)).Outcome);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().FolderPermissions.ToListAsync());
    }

    [Fact]
    public async Task A_stale_or_unexpected_stamp_is_a_conflict_and_rolls_everything_back()
    {
        var bossId = await UserAsync("boss", admin: true);
        await UserAsync("alice");
        using var scope = _factory.Services.CreateScope();
        Assert.True((await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, Write: AccessCellState.Allow))).Succeeded);
        var versions = scope.ServiceProvider.GetRequiredService<AuthzVersionStore>();
        var before = await versions.GetGlobalAsync();

        var stale = await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, "stale", Delete: AccessCellState.Allow));
        var missingStamp = await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, Delete: AccessCellState.Allow));

        Assert.Equal(PermissionWriteOutcome.Conflict, stale.Outcome);
        Assert.Equal(PermissionWriteOutcome.Conflict, missingStamp.Outcome);
        Assert.Equal(before, await versions.GetGlobalAsync());
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().FolderPermissions.AsNoTracking().SingleAsync();
        Assert.Equal(PermissionState.Unset, row.Delete);
    }

    [Fact]
    public async Task A_member_without_manage_cannot_change_anyones_access()
    {
        await UserAsync("boss", admin: true);
        var aliceId = await UserAsync("alice");
        await UserAsync("bob");
        using var scope = _factory.Services.CreateScope();

        var result = await ApplyAsync(scope, aliceId, "bob", new FolderAccessChangeDto(_booksKey, null, Read: AccessCellState.Allow));

        Assert.Equal(PermissionWriteOutcome.Forbidden, result.Outcome);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task A_manager_is_limited_by_delegation_rules()
    {
        var bossId = await UserAsync("boss", admin: true);
        var aliceId = await UserAsync("alice");
        await UserAsync("bob");
        using var scope = _factory.Services.CreateScope();
        Assert.True((await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, Manage: AccessCellState.Allow))).Succeeded);

        // Alice manages Books but cannot grant Manage, grant what she lacks (Write), or edit her own row.
        Assert.Equal(PermissionWriteOutcome.Forbidden, (await ApplyAsync(scope, aliceId, "bob", new FolderAccessChangeDto(_booksKey, null, Manage: AccessCellState.Allow))).Outcome);
        Assert.Equal(PermissionWriteOutcome.Forbidden, (await ApplyAsync(scope, aliceId, "bob", new FolderAccessChangeDto(_booksKey, null, Write: AccessCellState.Allow))).Outcome);
        Assert.Equal(PermissionWriteOutcome.Forbidden, (await ApplyAsync(scope, aliceId, "bob", new FolderAccessChangeDto(_booksKey, null, IsPrivate: true))).Outcome);
        var aliceStamp = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().FolderPermissions.AsNoTracking().SingleAsync(row => row.UserId == aliceId)).ConcurrencyStamp;
        Assert.Equal(PermissionWriteOutcome.Forbidden, (await ApplyAsync(scope, aliceId, "alice", new FolderAccessChangeDto(_booksKey, aliceStamp, Read: AccessCellState.Deny))).Outcome);

        // She may grant something she holds (Read is a shared default).
        Assert.True((await ApplyAsync(scope, aliceId, "bob", new FolderAccessChangeDto(_booksKey, null, Read: AccessCellState.Allow))).Succeeded);
    }

    [Fact]
    public async Task Admins_can_make_a_folder_private_and_shared_again()
    {
        var bossId = await UserAsync("boss", admin: true);
        await UserAsync("alice");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.True((await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, IsPrivate: true))).Succeeded);
        Assert.Equal(FolderAccessMode.Private, (await db.Folders.AsNoTracking().SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "")).AccessMode);
        Assert.True((await ApplyAsync(scope, bossId, "alice", new FolderAccessChangeDto(_booksKey, null, IsPrivate: false))).Succeeded);
        Assert.Equal(FolderAccessMode.Shared, (await db.Folders.AsNoTracking().SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "")).AccessMode);
        Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "folder.mode-changed");
    }
}
