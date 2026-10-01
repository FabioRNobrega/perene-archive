using WebApp.Authorization;
using WebApp.Data.Entities;

namespace WebApp.Tests.Authorization;

public sealed class DelegationRulesTests
{
    private static readonly IReadOnlyDictionary<FolderOperation, PermissionState> ReadAllow =
        new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Read] = PermissionState.Allow };

    private static string? Validate(
        bool admin = false, bool self = false, bool manage = true, Func<FolderOperation, bool>? has = null,
        FolderPermission? existing = null, IReadOnlyDictionary<FolderOperation, PermissionState>? cells = null,
        bool mode = false, bool enforcement = false, bool lockMask = false, bool clear = false) =>
        DelegationRules.Validate(admin, self, manage, has ?? (_ => true), existing, cells ?? ReadAllow, mode, enforcement, lockMask, clear);

    [Fact]
    public void Admin_may_change_anything_even_their_own_row()
    {
        var cells = new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Manage] = PermissionState.Allow };
        Assert.Null(Validate(admin: true, self: true, manage: false, has: _ => false, cells: cells, mode: true, enforcement: true, lockMask: true));
    }

    [Fact]
    public void A_manager_with_the_operation_may_grant_it()
    {
        Assert.Null(Validate());
    }

    [Fact]
    public void Without_manage_every_change_is_rejected()
    {
        Assert.NotNull(Validate(manage: false));
    }

    [Fact]
    public void Managers_cannot_edit_their_own_row()
    {
        Assert.NotNull(Validate(self: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Mode_enforcement_and_lock_changes_are_admin_only(bool mode, bool enforcement, bool lockMask)
    {
        Assert.NotNull(Validate(mode: mode, enforcement: enforcement, lockMask: lockMask));
    }

    [Fact]
    public void Managers_cannot_grant_manage()
    {
        var cells = new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Manage] = PermissionState.Allow };
        Assert.NotNull(Validate(cells: cells));
    }

    [Fact]
    public void Managers_cannot_grant_an_operation_they_do_not_hold()
    {
        Assert.NotNull(Validate(has: operation => operation != FolderOperation.Read));
    }

    [Fact]
    public void An_already_granted_cell_is_not_an_escalation()
    {
        var existing = new FolderPermission { FolderId = 1, UserId = "u", Read = PermissionState.Allow };
        Assert.Null(Validate(has: _ => false, existing: existing));
    }

    [Fact]
    public void Deny_never_counts_as_escalation()
    {
        var cells = new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Write] = PermissionState.Deny };
        Assert.Null(Validate(has: _ => false, cells: cells));
    }

    [Fact]
    public void Locked_cells_cannot_be_changed_by_managers()
    {
        var existing = new FolderPermission { FolderId = 1, UserId = "u", LockMask = FolderOperation.Read };
        Assert.NotNull(Validate(existing: existing));
    }

    [Fact]
    public void Locked_or_enforced_rows_cannot_be_cleared()
    {
        var empty = new Dictionary<FolderOperation, PermissionState>();
        Assert.NotNull(Validate(existing: new FolderPermission { FolderId = 1, UserId = "u", Enforced = true }, cells: empty, clear: true));
        Assert.NotNull(Validate(existing: new FolderPermission { FolderId = 1, UserId = "u", LockMask = FolderOperation.Write }, cells: new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Create] = PermissionState.Allow }, clear: true));
        Assert.Null(Validate(existing: new FolderPermission { FolderId = 1, UserId = "u", Read = PermissionState.Allow }, cells: empty, clear: true));
    }
}
