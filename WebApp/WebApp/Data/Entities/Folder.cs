using WebApp.Identity;

namespace WebApp.Data.Entities;

public enum FolderAccessMode { Shared = 0, Private = 1 }

public enum FolderStatus { Active = 0, Removed = 1 }

/// <summary>
/// A server-only record for an archive/cut/composition folder. <see cref="RootKey"/> and <see cref="RelativePath"/>
/// are identity metadata and never reach the browser. A directory without its own row inherits from its nearest ancestor row.
/// </summary>
public sealed class Folder
{
    public long Id { get; set; }
    public required string RootKey { get; set; }

    /// <summary>Forward-slash path below the root; empty for the root itself.</summary>
    public string RelativePath { get; set; } = string.Empty;
    public required string Label { get; set; }
    public long? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public string? OwnerUserId { get; set; }
    public ApplicationUser? Owner { get; set; }
    public FolderAccessMode AccessMode { get; set; } = FolderAccessMode.Shared;
    public FolderStatus Status { get; set; } = FolderStatus.Active;
    public DateTimeOffset CreatedUtc { get; set; }
    public List<FolderPermission> Permissions { get; set; } = [];
}
