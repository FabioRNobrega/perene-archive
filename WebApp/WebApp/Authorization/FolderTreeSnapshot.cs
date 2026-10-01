using WebApp.Data.Entities;

namespace WebApp.Authorization;

public sealed record FolderRecord(
    long Id, string RootKey, string RelativePath, string Label, long? ParentId, FolderAccessMode Mode, string? OwnerUserId)
{
    public FolderLocation Location => new(RootKey, RelativePath);
    public FolderNode Node => new(Id, Mode, OwnerUserId);
}

/// <summary>An immutable view of every active folder row, stamped with the global policy version it was loaded at.</summary>
public sealed class FolderTreeSnapshot
{
    private readonly Dictionary<(string, string), FolderRecord> _byKey;
    private readonly Dictionary<long, FolderRecord> _byId;

    public FolderTreeSnapshot(long version, PolicyDefaults defaults, IEnumerable<FolderRecord> folders)
    {
        Version = version;
        Defaults = defaults;
        All = folders.OrderBy(folder => folder.RootKey, StringComparer.Ordinal).ThenBy(folder => folder.RelativePath, StringComparer.Ordinal).ToList();
        _byKey = All.ToDictionary(folder => (folder.RootKey, folder.RelativePath));
        _byId = All.ToDictionary(folder => folder.Id);
    }

    public long Version { get; }
    public PolicyDefaults Defaults { get; }
    public IReadOnlyList<FolderRecord> All { get; }

    public FolderRecord? Find(long id) => _byId.GetValueOrDefault(id);
    public FolderRecord? Find(FolderLocation location) => _byKey.GetValueOrDefault((location.RootKey, location.RelativePath));

    /// <summary>The rows on the path to <paramref name="location"/> (root first); a directory with no row of its own ends at its nearest ancestor row.</summary>
    public IReadOnlyList<FolderRecord> Chain(FolderLocation location)
    {
        var chain = new List<FolderRecord>();
        if (_byKey.TryGetValue((location.RootKey, string.Empty), out var root)) chain.Add(root);
        else return chain;

        var current = string.Empty;
        foreach (var segment in location.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.Length == 0 ? segment : $"{current}/{segment}";
            if (_byKey.TryGetValue((location.RootKey, current), out var row)) chain.Add(row);
        }

        return chain;
    }

    public FolderRecord? Nearest(FolderLocation location) => Chain(location) is { Count: > 0 } chain ? chain[^1] : null;

    /// <summary>Rows strictly below <paramref name="folder"/>.</summary>
    public IEnumerable<FolderRecord> Descendants(FolderRecord folder) =>
        All.Where(other => other.Id != folder.Id && other.RootKey == folder.RootKey &&
            (folder.RelativePath.Length == 0 || other.RelativePath.StartsWith(folder.RelativePath + "/", StringComparison.Ordinal)));
}

/// <summary>The folders one user may read, plus the legal name-only pass-through ancestors, at one pair of versions.</summary>
public sealed class ReadableFolders(bool isAdmin, FolderTreeSnapshot tree, HashSet<long> readable, HashSet<long> passThrough)
{
    public bool IsAdmin { get; } = isAdmin;
    public FolderTreeSnapshot Tree { get; } = tree;

    public bool IsReadable(FolderLocation location) =>
        IsAdmin || (Tree.Nearest(location) is { } folder && readable.Contains(folder.Id));

    /// <summary>True for a row the user cannot read but may traverse by name because a readable folder sits below it.</summary>
    public bool IsPassThrough(FolderLocation location) =>
        !IsAdmin && Tree.Find(location) is { } folder && !readable.Contains(folder.Id) && passThrough.Contains(folder.Id);

    public bool IsReadable(FolderRecord folder) => IsAdmin || readable.Contains(folder.Id);
}

/// <summary>Process-wide cache stamped by (global policy version, user AuthzVersion).</summary>
public sealed class AccessCaches
{
    internal FolderTreeSnapshot? Tree;
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Policy, long User, ReadableFolders Readable)> Readable = new(StringComparer.Ordinal);
}
