using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Authorization;

/// <summary>
/// Owns the folder rows: root seeding, materialising on-disk directories for the editor, recording creator ownership,
/// and keeping rows aligned with renames, moves, and removals. Every change bumps the global policy version in its own transaction.
/// </summary>
internal sealed class FolderCatalog(AppDbContext db, FolderLocator locator, AuthzVersionStore versions)
{
    private const int MaxSyncedFolders = 5000;

    /// <summary>Ensures the policy singleton and one row per root. Idempotent; runs at web-host startup only.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var changed = false;
        if (!await db.AccessPolicies.AnyAsync(policy => policy.Id == AccessPolicy.SingletonId, cancellationToken))
        {
            db.AccessPolicies.Add(new AccessPolicy());
            await db.SaveChangesAsync(cancellationToken);
        }

        var existing = await db.Folders.Where(folder => folder.RelativePath == string.Empty).Select(folder => folder.RootKey).ToListAsync(cancellationToken);
        foreach (var root in locator.Roots.Where(root => !existing.Contains(root.Key)))
        {
            db.Folders.Add(new Folder
            {
                RootKey = root.Key,
                RelativePath = string.Empty,
                Label = root.Label,
                // Trash receives content from every folder, including Private ones, so it starts out Private.
                AccessMode = root.Key == "trash" ? FolderAccessMode.Private : FolderAccessMode.Shared,
                CreatedUtc = DateTimeOffset.UtcNow
            });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
            await versions.BumpGlobalAsync(cancellationToken);
        }
    }

    /// <summary>Records a newly created folder with its creator as owner; the mode is Shared unless Private is requested.</summary>
    public async Task<Folder?> RegisterCreatedAsync(FolderLocation location, string? ownerUserId, bool isPrivate, CancellationToken cancellationToken = default)
    {
        if (location.RelativePath.Length == 0) return null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await db.Folders.FirstOrDefaultAsync(
            folder => folder.RootKey == location.RootKey && folder.RelativePath == location.RelativePath, cancellationToken);
        if (existing is not null) return existing;

        var folder = new Folder
        {
            RootKey = location.RootKey,
            RelativePath = location.RelativePath,
            Label = location.RelativePath[(location.RelativePath.LastIndexOf('/') + 1)..],
            ParentId = await NearestParentIdAsync(location, cancellationToken),
            OwnerUserId = ownerUserId,
            AccessMode = isPrivate ? FolderAccessMode.Private : FolderAccessMode.Shared,
            CreatedUtc = DateTimeOffset.UtcNow
        };
        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        await versions.BumpGlobalAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return folder;
    }

    /// <summary>Creates a row for every directory on disk below the archive category roots so each can carry permissions.</summary>
    public async Task SyncTreeAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Folders.ToListAsync(cancellationToken);
        var byKey = rows.ToDictionary(folder => (folder.RootKey, folder.RelativePath));
        var added = 0;

        foreach (var root in locator.Roots.Where(root => root.IsArchiveCategory))
        {
            if (!Directory.Exists(root.Path)) continue;
            var pending = new Stack<FolderLocation>();
            pending.Push(new FolderLocation(root.Key, string.Empty));
            while (pending.Count > 0 && byKey.Count < MaxSyncedFolders)
            {
                var current = pending.Pop();
                var physical = locator.PhysicalPath(current)!;
                IEnumerable<string> children;
                try { children = Directory.EnumerateDirectories(physical).ToArray(); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }

                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith('.') || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    var location = current.Child(name);
                    pending.Push(location);
                    if (byKey.ContainsKey((location.RootKey, location.RelativePath))) continue;

                    // The new row keeps the effective owner of its nearest ancestor so materialising does not change who owns what.
                    var parent = NearestInMemory(byKey, location);
                    var folder = new Folder
                    {
                        RootKey = location.RootKey,
                        RelativePath = location.RelativePath,
                        Label = name,
                        Parent = parent,
                        OwnerUserId = parent?.OwnerUserId,
                        CreatedUtc = DateTimeOffset.UtcNow
                    };
                    db.Folders.Add(folder);
                    byKey[(folder.RootKey, folder.RelativePath)] = folder;
                    added++;
                }
            }
        }

        if (added == 0) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await versions.BumpGlobalAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Moves the row for <paramref name="from"/> and every row below it to <paramref name="to"/>, keeping owners, modes, and permissions.</summary>
    public async Task RelocateAsync(FolderLocation from, FolderLocation to, CancellationToken cancellationToken = default)
    {
        if (from == to) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var moving = await SubtreeAsync(from, cancellationToken);
        if (moving.Count == 0) return;

        // Rows already at the destination describe directories that no longer exist there.
        var stale = await SubtreeAsync(to, cancellationToken);
        db.Folders.RemoveRange(stale.Where(row => !moving.Contains(row)));
        await db.SaveChangesAsync(cancellationToken);

        foreach (var row in moving)
        {
            var suffix = row.RelativePath[from.RelativePath.Length..];
            row.RootKey = to.RootKey;
            row.RelativePath = FolderLocation.Normalize(to.RelativePath + suffix);
            if (row.RelativePath.Length == 0) continue;
            row.Label = row.RelativePath[(row.RelativePath.LastIndexOf('/') + 1)..];
        }

        await db.SaveChangesAsync(cancellationToken);

        var byKey = (await db.Folders.Where(folder => folder.RootKey == to.RootKey).ToListAsync(cancellationToken))
            .ToDictionary(folder => (folder.RootKey, folder.RelativePath));
        foreach (var row in moving.Where(row => row.RelativePath.Length > 0))
        {
            row.ParentId = NearestInMemory(byKey, new FolderLocation(row.RootKey, row.RelativePath))?.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        await versions.BumpGlobalAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Deletes the row at <paramref name="location"/> and everything below it (permissions cascade).</summary>
    public async Task RemoveAsync(FolderLocation location, bool includeSelf = true, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = (await SubtreeAsync(location, cancellationToken)).Where(row => includeSelf || row.RelativePath != location.RelativePath).ToList();
        if (rows.Count == 0) return;
        db.Folders.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        await versions.BumpGlobalAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<List<Folder>> SubtreeAsync(FolderLocation location, CancellationToken cancellationToken)
    {
        var prefix = location.RelativePath + "/";
        var rows = await db.Folders.Where(folder => folder.RootKey == location.RootKey).ToListAsync(cancellationToken);
        return rows
            .Where(folder => location.RelativePath.Length == 0
                ? folder.RelativePath.Length > 0
                : folder.RelativePath == location.RelativePath || folder.RelativePath.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(folder => folder.RelativePath.Length)
            .ToList();
    }

    private async Task<long?> NearestParentIdAsync(FolderLocation location, CancellationToken cancellationToken)
    {
        var parent = location.Parent();
        while (parent is not null)
        {
            var found = await db.Folders.Where(folder => folder.RootKey == parent.RootKey && folder.RelativePath == parent.RelativePath)
                .Select(folder => (long?)folder.Id).FirstOrDefaultAsync(cancellationToken);
            if (found is not null) return found;
            parent = parent.Parent();
        }

        return null;
    }

    private static Folder? NearestInMemory(Dictionary<(string, string), Folder> byKey, FolderLocation location)
    {
        var parent = location.Parent();
        while (parent is not null)
        {
            if (byKey.TryGetValue((parent.RootKey, parent.RelativePath), out var folder)) return folder;
            parent = parent.Parent();
        }

        return null;
    }
}
