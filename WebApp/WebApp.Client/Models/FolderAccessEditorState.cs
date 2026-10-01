namespace WebApp.Client.Models;

public enum FolderAccessOperation { Read, Write, Create, Delete, Manage }

/// <summary>
/// The access editor's client-side edit buffer. It maps the checkbox-plus-deny cells onto the tri-state flags losslessly
/// (Allow / Deny / Unset), remembers only cells that differ from what the server sent, and never decides access.
/// </summary>
public sealed class FolderAccessEditorState
{
    private readonly Dictionary<string, FolderAccessFolderDto> _folders = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Folder, FolderAccessOperation Operation), AccessCellState> _edits = [];
    private readonly Dictionary<string, bool> _privateEdits = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    public void Load(FolderAccessDto dto)
    {
        _folders.Clear();
        _edits.Clear();
        _privateEdits.Clear();
        foreach (var folder in dto.Folders) _folders[folder.FolderKey] = folder;
        Dto = dto;
    }

    /// <summary>Loads fresh server data after a conflict while keeping the pending edits that still differ from it.</summary>
    public void Rebase(FolderAccessDto dto)
    {
        var edits = _edits.ToList();
        var privateEdits = _privateEdits.ToList();
        Load(dto);
        foreach (var ((folder, operation), state) in edits)
        {
            if (_folders.ContainsKey(folder)) SetState(folder, operation, state);
        }

        foreach (var (folder, isPrivate) in privateEdits)
        {
            if (_folders.ContainsKey(folder)) SetPrivate(folder, isPrivate);
        }
    }

    public FolderAccessDto? Dto { get; private set; }
    public IReadOnlyCollection<string> ExpandedKeys => _expanded;
    public bool HasUnsavedChanges => _edits.Count > 0 || _privateEdits.Count > 0;

    public FolderAccessCellDto? Cell(string folderKey, FolderAccessOperation operation) =>
        _folders.TryGetValue(folderKey, out var folder) ? CellOf(folder, operation) : null;

    private static FolderAccessCellDto? CellOf(FolderAccessFolderDto folder, FolderAccessOperation operation) => operation switch
    {
        FolderAccessOperation.Read => folder.Read,
        FolderAccessOperation.Write => folder.Write,
        FolderAccessOperation.Create => folder.Create,
        FolderAccessOperation.Delete => folder.Delete,
        _ => folder.Manage
    };

    /// <summary>The state to show: the pending edit when there is one, otherwise what the server sent.</summary>
    public AccessCellState StateOf(string folderKey, FolderAccessOperation operation)
    {
        if (_edits.TryGetValue((folderKey, operation), out var edited)) return edited;
        return Cell(folderKey, operation)?.State ?? AccessCellState.Unset;
    }

    public bool IsPrivate(string folderKey) =>
        _privateEdits.TryGetValue(folderKey, out var value) ? value : _folders.TryGetValue(folderKey, out var folder) && folder.IsPrivate;

    /// <summary>Records an edit; setting a cell back to the server's value removes the edit so unchanged cells are never submitted.</summary>
    public void SetState(string folderKey, FolderAccessOperation operation, AccessCellState state)
    {
        if (Cell(folderKey, operation) is not { Locked: false } cell) return;
        if (cell.State == state) _edits.Remove((folderKey, operation));
        else _edits[(folderKey, operation)] = state;
    }

    /// <summary>Whether access is currently granted for display: a pending or saved explicit state wins, otherwise the server's effective result.</summary>
    public bool EffectiveOf(string folderKey, FolderAccessOperation operation) => StateOf(folderKey, operation) switch
    {
        AccessCellState.Allow => true,
        AccessCellState.Deny => false,
        _ => Cell(folderKey, operation)?.Effective ?? false
    };

    /// <summary>
    /// The lock/unlock switch: flips the displayed access. Flipping back to what the server reported restores the original
    /// explicit state (so nothing is submitted); otherwise it records an explicit Allow or Deny.
    /// </summary>
    public void ToggleAccess(string folderKey, FolderAccessOperation operation)
    {
        if (Cell(folderKey, operation) is not { } cell) return;
        var target = !EffectiveOf(folderKey, operation);
        SetState(folderKey, operation, target == cell.Effective ? cell.State : target ? AccessCellState.Allow : AccessCellState.Deny);
    }

    /// <summary>The Allow checkbox: checked is Allow, unchecked is Unset (never a hidden Deny).</summary>
    public void ToggleAllow(string folderKey, FolderAccessOperation operation) =>
        SetState(folderKey, operation, StateOf(folderKey, operation) == AccessCellState.Allow ? AccessCellState.Unset : AccessCellState.Allow);

    /// <summary>The Deny toggle, mutually exclusive with Allow.</summary>
    public void ToggleDeny(string folderKey, FolderAccessOperation operation) =>
        SetState(folderKey, operation, StateOf(folderKey, operation) == AccessCellState.Deny ? AccessCellState.Unset : AccessCellState.Deny);

    public void SetPrivate(string folderKey, bool isPrivate)
    {
        if (!_folders.TryGetValue(folderKey, out var folder)) return;
        if (folder.IsPrivate == isPrivate) _privateEdits.Remove(folderKey);
        else _privateEdits[folderKey] = isPrivate;
    }

    /// <summary>Resets every editable cell of a folder to Unset ("reset to inherited").</summary>
    public void ResetRow(string folderKey)
    {
        foreach (var operation in Enum.GetValues<FolderAccessOperation>())
        {
            if (Cell(folderKey, operation) is { Locked: false }) SetState(folderKey, operation, AccessCellState.Unset);
        }
    }

    /// <summary>"Apply to subfolders": copies one operation's current state onto every descendant folder (an explicit edit, never automatic).</summary>
    public void ApplyToSubfolders(string folderKey, FolderAccessOperation operation)
    {
        var state = StateOf(folderKey, operation);
        foreach (var descendant in DescendantsOf(folderKey)) SetState(descendant.FolderKey, operation, state);
    }

    public IEnumerable<FolderAccessFolderDto> DescendantsOf(string folderKey)
    {
        var pending = new Queue<string>([folderKey]);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            foreach (var child in _folders.Values.Where(folder => folder.ParentKey == current))
            {
                pending.Enqueue(child.FolderKey);
                yield return child;
            }
        }
    }

    public bool IsExpanded(string folderKey) => _expanded.Contains(folderKey);
    public void ToggleExpanded(string folderKey) { if (!_expanded.Remove(folderKey)) _expanded.Add(folderKey); }

    public bool HasChildren(string folderKey) => _folders.Values.Any(folder => folder.ParentKey == folderKey);

    /// <summary>Visible rows in tree order: every root, and the children of expanded folders only.</summary>
    public IEnumerable<FolderAccessFolderDto> VisibleFolders()
    {
        if (Dto is null) yield break;
        foreach (var folder in Dto.Folders)
        {
            if (IsVisible(folder)) yield return folder;
        }
    }

    private bool IsVisible(FolderAccessFolderDto folder)
    {
        var parentKey = folder.ParentKey;
        while (parentKey is not null)
        {
            if (!_expanded.Contains(parentKey)) return false;
            parentKey = _folders.TryGetValue(parentKey, out var parent) ? parent.ParentKey : null;
        }

        return true;
    }

    /// <summary>Only the changed cells, grouped per folder with the stamp the row had when loaded.</summary>
    public IReadOnlyList<FolderAccessChangeDto> BuildChanges()
    {
        var keys = _edits.Keys.Select(key => key.Folder).Concat(_privateEdits.Keys).Distinct(StringComparer.Ordinal).ToList();
        var changes = new List<FolderAccessChangeDto>();
        foreach (var key in keys)
        {
            AccessCellState? Edit(FolderAccessOperation operation) => _edits.TryGetValue((key, operation), out var state) ? state : null;
            changes.Add(new FolderAccessChangeDto(
                key,
                _folders[key].ConcurrencyStamp,
                Edit(FolderAccessOperation.Read),
                Edit(FolderAccessOperation.Write),
                Edit(FolderAccessOperation.Create),
                Edit(FolderAccessOperation.Delete),
                Edit(FolderAccessOperation.Manage),
                _privateEdits.TryGetValue(key, out var isPrivate) ? isPrivate : null));
        }

        return changes;
    }

    /// <summary>Labels of folders where an edit would deny the folder's owner (the editor asks before doing that).</summary>
    public IReadOnlyList<string> OwnerDenyLabels() => _edits
        .Where(edit => edit.Value == AccessCellState.Deny && _folders[edit.Key.Folder].IsOwner)
        .Select(edit => _folders[edit.Key.Folder].Label).Distinct().ToList();

    /// <summary>Reverts every pending Deny on folders the user owns ("Keep owner access").</summary>
    public void KeepOwnerAccess()
    {
        foreach (var key in _edits.Where(edit => edit.Value == AccessCellState.Deny && _folders[edit.Key.Folder].IsOwner).Select(edit => edit.Key).ToList())
            _edits.Remove(key);
    }

    public void Discard()
    {
        _edits.Clear();
        _privateEdits.Clear();
    }
}
