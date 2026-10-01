using WebApp.Authorization;
using WebApp.Data.Entities;

namespace WebApp.Tests.Authorization;

public sealed class FolderPermissionResolverTests
{
    private const string User = "alice";
    private static readonly PolicyDefaults Shared = PolicyDefaults.Shared;

    private static FolderNode Node(long id, FolderAccessMode mode = FolderAccessMode.Shared, string? owner = null) => new(id, mode, owner);

    private static PermissionGrant Grant(
        PermissionState read = PermissionState.Unset, PermissionState write = PermissionState.Unset,
        PermissionState create = PermissionState.Unset, PermissionState delete = PermissionState.Unset,
        PermissionState manage = PermissionState.Unset, bool enforced = false) =>
        new(read, write, create, delete, manage, enforced);

    private static bool Resolve(FolderOperation op, IReadOnlyList<FolderNode> chain, Dictionary<long, PermissionGrant>? grants = null, bool admin = false, string user = User) =>
        FolderPermissionResolver.Resolve(op, user, admin, chain, grants ?? [], Shared);

    [Theory]
    [InlineData(FolderOperation.Read, true)]
    [InlineData(FolderOperation.Write, false)]
    [InlineData(FolderOperation.Create, false)]
    [InlineData(FolderOperation.Delete, false)]
    [InlineData(FolderOperation.Manage, false)]
    public void Shared_defaults_allow_read_only(FolderOperation op, bool expected) =>
        Assert.Equal(expected, Resolve(op, [Node(1), Node(2)]));

    [Theory]
    [InlineData(FolderOperation.Read)]
    [InlineData(FolderOperation.Write)]
    [InlineData(FolderOperation.Create)]
    [InlineData(FolderOperation.Delete)]
    [InlineData(FolderOperation.Manage)]
    public void Admin_overrides_everything_including_private_and_enforced_deny(FolderOperation op)
    {
        var grants = new Dictionary<long, PermissionGrant> { [1] = Grant(read: PermissionState.Deny, enforced: true) };
        Assert.True(Resolve(op, [Node(1, FolderAccessMode.Private)], grants, admin: true));
    }

    [Fact]
    public void An_empty_chain_fails_closed() => Assert.False(Resolve(FolderOperation.Read, []));

    [Fact]
    public void Private_folder_hides_content_from_a_user_with_only_inherited_read()
    {
        var chain = new[] { Node(1), Node(2, FolderAccessMode.Private), Node(3) };
        Assert.False(Resolve(FolderOperation.Read, chain, new() { [1] = Grant(read: PermissionState.Allow) }));
        Assert.False(Resolve(FolderOperation.Read, chain.Take(2).ToList()));
    }

    [Fact]
    public void Private_folder_opens_with_an_explicit_read_or_ownership()
    {
        var chain = new[] { Node(1), Node(2, FolderAccessMode.Private), Node(3) };
        Assert.True(Resolve(FolderOperation.Read, chain, new() { [2] = Grant(read: PermissionState.Allow) }));
        Assert.True(Resolve(FolderOperation.Read, [Node(1), Node(2, FolderAccessMode.Private, User)]));
    }

    [Fact]
    public void Enforced_deny_cannot_be_overridden_below()
    {
        var chain = new[] { Node(1), Node(2) };
        var grants = new Dictionary<long, PermissionGrant>
        {
            [1] = Grant(write: PermissionState.Deny, enforced: true),
            [2] = Grant(write: PermissionState.Allow)
        };
        Assert.False(Resolve(FolderOperation.Write, chain, grants));

        grants[1] = Grant(write: PermissionState.Deny);
        Assert.True(Resolve(FolderOperation.Write, chain, grants));
    }

    [Fact]
    public void Explicit_row_beats_ownership_and_inheritance()
    {
        var chain = new[] { Node(1), Node(2, owner: User) };
        Assert.True(Resolve(FolderOperation.Write, chain));
        Assert.False(Resolve(FolderOperation.Write, chain, new() { [2] = Grant(write: PermissionState.Deny) }));
        Assert.True(Resolve(FolderOperation.Create, [Node(1), Node(2)],
            new() { [1] = Grant(create: PermissionState.Deny), [2] = Grant(create: PermissionState.Allow) }));
    }

    [Fact]
    public void Ownership_grants_read_write_create_delete_but_never_manage()
    {
        var chain = new[] { Node(1), Node(2, owner: User) };
        Assert.True(Resolve(FolderOperation.Read, chain));
        Assert.True(Resolve(FolderOperation.Write, chain));
        Assert.True(Resolve(FolderOperation.Create, chain));
        Assert.True(Resolve(FolderOperation.Delete, chain));
        Assert.False(Resolve(FolderOperation.Manage, chain));
        Assert.False(Resolve(FolderOperation.Write, [Node(1), Node(2, owner: "bob")]));
    }

    [Fact]
    public void Nearest_inherited_row_wins()
    {
        var chain = new[] { Node(1), Node(2), Node(3) };
        var grants = new Dictionary<long, PermissionGrant>
        {
            [1] = Grant(delete: PermissionState.Allow),
            [2] = Grant(delete: PermissionState.Deny)
        };
        Assert.False(Resolve(FolderOperation.Delete, chain, grants));
        grants[2] = Grant();
        Assert.True(Resolve(FolderOperation.Delete, chain, grants));
    }

    [Fact]
    public void Every_non_read_operation_also_needs_read()
    {
        var chain = new[] { Node(1), Node(2) };
        var grants = new Dictionary<long, PermissionGrant> { [2] = Grant(read: PermissionState.Deny, write: PermissionState.Allow) };
        Assert.False(Resolve(FolderOperation.Write, chain, grants));
    }

    [Fact]
    public void Manage_has_no_default_and_comes_only_from_rows()
    {
        var chain = new[] { Node(1), Node(2) };
        Assert.False(Resolve(FolderOperation.Manage, chain));
        Assert.True(Resolve(FolderOperation.Manage, chain, new() { [1] = Grant(manage: PermissionState.Allow) }));
    }

    [Fact]
    public void Explain_reports_provenance()
    {
        var chain = new[] { Node(1), Node(2) };
        var grants = new Dictionary<long, PermissionGrant> { [1] = Grant(write: PermissionState.Allow) };
        var inherited = FolderPermissionResolver.Explain(FolderOperation.Write, User, chain, grants, Shared);
        Assert.Equal((true, "Inherited", (long?)1), inherited);
        Assert.Equal("Shared default", FolderPermissionResolver.Explain(FolderOperation.Read, User, chain, new Dictionary<long, PermissionGrant>(), Shared).Provenance);
        Assert.Equal("Private: no access", FolderPermissionResolver.Explain(FolderOperation.Read, User, [Node(1, FolderAccessMode.Private)], new Dictionary<long, PermissionGrant>(), Shared).Provenance);
    }
}

public sealed class DelegationRulesTests
{
    private static readonly IReadOnlyDictionary<FolderOperation, PermissionState> Write =
        new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Write] = PermissionState.Allow };

    private static string? Validate(
        bool admin = false, bool self = false, bool manages = true, Func<FolderOperation, bool>? has = null, FolderPermission? existing = null,
        IReadOnlyDictionary<FolderOperation, PermissionState>? cells = null, bool mode = false, bool enforce = false, bool locks = false, bool clear = false) =>
        DelegationRules.Validate(admin, self, manages, has ?? (_ => true), existing, cells ?? Write, mode, enforce, locks, clear);

    [Fact]
    public void Admin_may_do_anything() =>
        Assert.Null(Validate(admin: true, self: true, manages: false, has: _ => false, mode: true, enforce: true, locks: true,
            cells: new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Manage] = PermissionState.Allow }));

    [Fact]
    public void A_manager_may_make_an_in_range_grant() => Assert.Null(Validate());

    [Fact] public void Needs_manage() => Assert.NotNull(Validate(manages: false));
    [Fact] public void Cannot_edit_own_row() => Assert.NotNull(Validate(self: true));
    [Fact] public void Cannot_change_mode() => Assert.NotNull(Validate(mode: true));
    [Fact] public void Cannot_enforce() => Assert.NotNull(Validate(enforce: true));
    [Fact] public void Cannot_lock() => Assert.NotNull(Validate(locks: true));

    [Fact]
    public void Cannot_grant_manage() =>
        Assert.NotNull(Validate(cells: new Dictionary<FolderOperation, PermissionState> { [FolderOperation.Manage] = PermissionState.Allow }));

    [Fact]
    public void Cannot_exceed_own_rights() => Assert.NotNull(Validate(has: op => op != FolderOperation.Write));

    [Fact]
    public void Cannot_change_a_locked_cell_or_clear_a_locked_row()
    {
        var locked = new FolderPermission { FolderId = 1, UserId = "u", LockMask = FolderOperation.Write };
        Assert.NotNull(Validate(existing: locked));
        Assert.NotNull(Validate(existing: locked, cells: new Dictionary<FolderOperation, PermissionState>(), clear: true));
    }
}
