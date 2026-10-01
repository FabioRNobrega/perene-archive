namespace WebApp.Client.Models;

/// <summary>Tri-state value of one operation cell; Unset defers to inheritance and defaults.</summary>
public enum AccessCellState { Unset = 0, Allow = 1, Deny = 2 }

/// <summary>One operation cell: the explicit state on this folder plus the effective result and where it comes from.</summary>
public sealed record FolderAccessCellDto(AccessCellState State, bool Effective, string Provenance, bool Locked);

/// <summary>One folder row of the access editor. <see cref="FolderKey"/> is an opaque token; no path or database ID is exposed.</summary>
public sealed record FolderAccessFolderDto(
    string FolderKey,
    string? ParentKey,
    string Label,
    int Depth,
    bool IsPrivate,
    bool IsOwner,
    string? ConcurrencyStamp,
    FolderAccessCellDto Read,
    FolderAccessCellDto Write,
    FolderAccessCellDto Create,
    FolderAccessCellDto Delete,
    FolderAccessCellDto? Manage);

public sealed record FolderAccessDto(string UserName, string DisplayName, bool CanDelegate, IReadOnlyList<FolderAccessFolderDto> Folders);

/// <summary>A changed cell group for one folder. A null state means "unchanged"; only changed cells are sent.</summary>
public sealed record FolderAccessChangeDto(
    string FolderKey,
    string? ConcurrencyStamp,
    AccessCellState? Read = null,
    AccessCellState? Write = null,
    AccessCellState? Create = null,
    AccessCellState? Delete = null,
    AccessCellState? Manage = null,
    bool? IsPrivate = null,
    bool ClearRow = false,
    bool? Enforced = null,
    int? LockMask = null);

public sealed record FolderAccessSaveRequest(IReadOnlyList<FolderAccessChangeDto> Changes);

/// <summary>What a pending change would hide: counts and folder labels only.</summary>
public sealed record FolderAccessImpactDto(
    int FolderCount,
    int ItemCount,
    IReadOnlyList<string> FolderLabels,
    IReadOnlyList<string> AffectedUsers,
    IReadOnlyList<string> OwnerConflictLabels);

/// <summary>Why a save did not apply: a stale stamp (<see cref="IsConflict"/>) or a rejected change.</summary>
public sealed record FolderAccessSaveOutcome(bool Succeeded, bool IsConflict, IReadOnlyList<string> Errors)
{
    public static readonly FolderAccessSaveOutcome Success = new(true, false, []);
}
