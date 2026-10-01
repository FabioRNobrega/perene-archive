using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class FolderAccessEditorStateTests
{
    private static FolderAccessCellDto Cell(AccessCellState state = AccessCellState.Unset, bool locked = false) => new(state, true, "Shared default", locked);

    private static FolderAccessFolderDto Folder(string key, string? parent, int depth, bool owner = false, FolderAccessCellDto? read = null, FolderAccessCellDto? write = null) =>
        new(key, parent, key.ToUpperInvariant(), depth, false, owner, "stamp-" + key, read ?? Cell(), write ?? Cell(), Cell(), Cell(), null);

    private static FolderAccessEditorState Loaded()
    {
        var state = new FolderAccessEditorState();
        state.Load(new FolderAccessDto("alice", "Alice", false,
        [
            Folder("books", null, 0, read: Cell(AccessCellState.Allow)),
            Folder("mylab", "books", 1, owner: true),
            Folder("deep", "mylab", 2),
            Folder("locked", "books", 1, write: Cell(AccessCellState.Unset, locked: true))
        ]));
        return state;
    }

    [Fact]
    public void Allow_deny_and_unset_round_trip_to_dtos()
    {
        var state = Loaded();
        state.ToggleAllow("mylab", FolderAccessOperation.Write);
        Assert.Equal(AccessCellState.Allow, state.StateOf("mylab", FolderAccessOperation.Write));
        state.ToggleDeny("mylab", FolderAccessOperation.Write);
        Assert.Equal(AccessCellState.Deny, state.StateOf("mylab", FolderAccessOperation.Write));
        var change = Assert.Single(state.BuildChanges());
        Assert.Equal(AccessCellState.Deny, change.Write);
        Assert.Equal("stamp-mylab", change.ConcurrencyStamp);
        state.ToggleDeny("mylab", FolderAccessOperation.Write);
        Assert.Equal(AccessCellState.Unset, state.StateOf("mylab", FolderAccessOperation.Write));
        Assert.Empty(state.BuildChanges());
    }

    [Fact]
    public void Only_changed_cells_are_submitted()
    {
        var state = Loaded();
        state.ToggleAllow("books", FolderAccessOperation.Read);
        state.ToggleAllow("books", FolderAccessOperation.Read);
        Assert.False(state.HasUnsavedChanges);
        state.ToggleAllow("books", FolderAccessOperation.Create);
        var change = Assert.Single(state.BuildChanges());
        Assert.Null(change.Read);
        Assert.Equal(AccessCellState.Allow, change.Create);
    }

    [Fact]
    public void Locked_cells_cannot_be_edited()
    {
        var state = Loaded();
        state.ToggleAllow("locked", FolderAccessOperation.Write);
        Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public void Unsaved_edits_are_tracked_until_discarded()
    {
        var state = Loaded();
        state.ToggleAllow("mylab", FolderAccessOperation.Read);
        Assert.True(state.HasUnsavedChanges);
        state.Discard();
        Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public void Apply_to_subfolders_copies_an_explicit_edit_to_descendants_only()
    {
        var state = Loaded();
        state.ApplyToSubfolders("books", FolderAccessOperation.Read);
        Assert.Equal(AccessCellState.Allow, state.StateOf("deep", FolderAccessOperation.Read));
        Assert.Equal(AccessCellState.Allow, state.StateOf("mylab", FolderAccessOperation.Read));
        Assert.Equal(3, state.BuildChanges().Count);
    }

    [Fact]
    public void Tree_is_collapsed_by_default_and_expands_branch_by_branch()
    {
        var state = Loaded();
        Assert.Equal(["books"], state.VisibleFolders().Select(folder => folder.FolderKey));
        state.ToggleExpanded("books");
        Assert.Equal(["books", "mylab", "locked"], state.VisibleFolders().Select(folder => folder.FolderKey));
    }

    [Fact]
    public void Owner_deny_is_flagged_and_can_be_reverted()
    {
        var state = Loaded();
        state.ToggleDeny("mylab", FolderAccessOperation.Write);
        Assert.Equal(["MYLAB"], state.OwnerDenyLabels());
        state.KeepOwnerAccess();
        Assert.Empty(state.OwnerDenyLabels());
        Assert.False(state.HasUnsavedChanges);
    }

    [Fact]
    public void Rebase_keeps_pending_edits_against_fresh_server_state()
    {
        var state = Loaded();
        state.ToggleAllow("mylab", FolderAccessOperation.Create);
        state.Rebase(state.Dto! with { Folders = state.Dto!.Folders.Select(folder => folder with { ConcurrencyStamp = "new-" + folder.FolderKey }).ToList() });
        var change = Assert.Single(state.BuildChanges());
        Assert.Equal("new-mylab", change.ConcurrencyStamp);
        Assert.Equal(AccessCellState.Allow, change.Create);
    }

    [Fact]
    public void Lock_switch_shows_the_effective_state_and_only_records_real_changes()
    {
        var state = Loaded();
        Assert.True(state.EffectiveOf("mylab", FolderAccessOperation.Read));
        state.ToggleAccess("mylab", FolderAccessOperation.Read);
        Assert.False(state.EffectiveOf("mylab", FolderAccessOperation.Read));
        Assert.Equal(AccessCellState.Deny, Assert.Single(state.BuildChanges()).Read);
        state.ToggleAccess("mylab", FolderAccessOperation.Read);
        Assert.Empty(state.BuildChanges());

        // "books" has an explicit Allow on Read: locking records a Deny, unlocking restores the original Allow.
        state.ToggleAccess("books", FolderAccessOperation.Read);
        Assert.Equal(AccessCellState.Deny, Assert.Single(state.BuildChanges()).Read);
        state.ToggleAccess("books", FolderAccessOperation.Read);
        Assert.Empty(state.BuildChanges());
    }
}
