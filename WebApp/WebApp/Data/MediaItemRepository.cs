using Microsoft.EntityFrameworkCore;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Data;

/// <summary>
/// The only place that reads or changes media identity rows. Every path lookup goes through <see cref="GetActiveAsync"/> (it states
/// the Active predicate so SQLite can use the partial index), and every transition that re-occupies a path frees it first.
/// </summary>
internal sealed class MediaItemRepository(AppDbContext db)
{
    public Task<MediaItem?> GetActiveAsync(long folderId, string relativePath, CancellationToken cancellationToken = default) =>
        db.MediaItems.FirstOrDefaultAsync(
            item => item.FolderId == folderId && item.RelativePath == relativePath && item.Status == MediaStatus.Active, cancellationToken);

    /// <summary>The most recently created Missing row at a path: the only comparison candidate when a file reappears.</summary>
    public async Task<MediaItem?> GetLatestMissingAtPathAsync(long folderId, string relativePath, CancellationToken cancellationToken = default) =>
        (await db.MediaItems.Where(item => item.FolderId == folderId && item.RelativePath == relativePath && item.Status == MediaStatus.Missing)
            .ToListAsync(cancellationToken)).OrderByDescending(item => item.CreatedUtc).ThenByDescending(item => item.Id).FirstOrDefault();

    public Task<List<MediaItem>> GetMissingByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) =>
        db.MediaItems.Where(item => item.Status == MediaStatus.Missing && item.ContentFingerprint == fingerprint).ToListAsync(cancellationToken);

    public Task<List<MediaItem>> GetByStatusAsync(MediaStatus status, CancellationToken cancellationToken = default) =>
        db.MediaItems.Where(item => item.Status == status).ToListAsync(cancellationToken);

    public async Task<MediaItem> AddActiveAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        item.Status = MediaStatus.Active;
        db.MediaItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return item;
    }

    /// <summary>Confirmed update: same identity, changed bytes. Bumps the revision and refreshes metadata.</summary>
    public async Task ConfirmUpdateAsync(MediaItem item, long size, DateTime lastWriteUtc, string? fingerprint, CancellationToken cancellationToken = default)
    {
        item.ContentRevision++;
        Refresh(item, size, lastWriteUtc, fingerprint);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshAsync(MediaItem item, long size, DateTime lastWriteUtc, string? fingerprint, CancellationToken cancellationToken = default)
    {
        Refresh(item, size, lastWriteUtc, fingerprint);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Installs <paramref name="replacement"/> as the Active row at the old row's path in one transaction, in the order the partial
    /// index requires: free the path (old row to <paramref name="oldStatus"/>), insert the new Active row, then link the old row to it.
    /// </summary>
    public async Task<MediaItem> ReplaceAsync(MediaItem old, MediaStatus oldStatus, MediaItem replacement, CancellationToken cancellationToken = default)
    {
        if (oldStatus is not (MediaStatus.Superseded or MediaStatus.NeedsReview) || !MediaIdentityClassifier.IsAllowedTransition(old.Status, oldStatus))
            throw new InvalidOperationException($"{old.Status} to {oldStatus} is not an allowed media transition.");

        await using var scope = await BeginAsync(cancellationToken);
        old.Status = oldStatus;
        await db.SaveChangesAsync(cancellationToken);
        replacement.Status = MediaStatus.Active;
        db.MediaItems.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        if (oldStatus == MediaStatus.Superseded) old.SupersededByMediaItemId = replacement.Id;
        await db.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return replacement;
    }

    /// <summary>A file at a path whose previous occupant is Missing and no longer a candidate: the Missing row is retired and a new Active row installed.</summary>
    public async Task<MediaItem> RetireMissingAndAddAsync(MediaItem missing, MediaStatus newStatus, MediaItem replacement, CancellationToken cancellationToken = default)
    {
        if (newStatus is not (MediaStatus.Superseded or MediaStatus.NeedsReview) || !MediaIdentityClassifier.IsAllowedTransition(missing.Status, newStatus))
            throw new InvalidOperationException($"{missing.Status} to {newStatus} is not an allowed media transition.");

        await using var scope = await BeginAsync(cancellationToken);
        missing.Status = newStatus;
        replacement.Status = MediaStatus.Active;
        db.MediaItems.Add(replacement);
        await db.SaveChangesAsync(cancellationToken);
        if (newStatus == MediaStatus.Superseded) missing.SupersededByMediaItemId = replacement.Id;
        await db.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return replacement;
    }

    public async Task MarkMissingAsync(MediaItem item, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (!MediaIdentityClassifier.IsAllowedTransition(item.Status, MediaStatus.Missing) || item.Status != MediaStatus.Active) return;
        item.Status = MediaStatus.Missing;
        item.MissingSince = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkNeedsReviewAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        if (item.Status == MediaStatus.NeedsReview || !MediaIdentityClassifier.IsAllowedTransition(item.Status, MediaStatus.NeedsReview)) return;
        item.Status = MediaStatus.NeedsReview;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Missing back to Active at the same path; requires that no other Active row holds that path, otherwise the row goes to review.</summary>
    public async Task<bool> ReactivateAsync(MediaItem missing, long size, DateTime lastWriteUtc, string? fingerprint, bool bumpRevision, CancellationToken cancellationToken = default)
    {
        if (await GetActiveAsync(missing.FolderId, missing.RelativePath, cancellationToken) is not null)
        {
            await MarkNeedsReviewAsync(missing, cancellationToken);
            return false;
        }

        missing.Status = MediaStatus.Active;
        missing.MissingSince = null;
        if (bumpRevision) missing.ContentRevision++;
        Refresh(missing, size, lastWriteUtc, fingerprint);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>A moved or renamed file: repoint the item to its new location (it keeps its identity and therefore its user data).</summary>
    public async Task<bool> RelinkAsync(MediaItem item, long folderId, string relativePath, long size, DateTime lastWriteUtc, string? fingerprint, CancellationToken cancellationToken = default)
    {
        var occupant = await GetActiveAsync(folderId, relativePath, cancellationToken);
        if (occupant is not null && occupant.Id != item.Id) return false;

        await using var scope = await BeginAsync(cancellationToken);
        if (item.Status == MediaStatus.Missing)
        {
            item.Status = MediaStatus.Active;
            item.MissingSince = null;
        }

        item.FolderId = folderId;
        item.RelativePath = relativePath;
        Refresh(item, size, lastWriteUtc, fingerprint);
        await db.SaveChangesAsync(cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>The path an Active item was last seen at (Active rows keep it current through app moves and relinks).</summary>
    public Task<List<MediaItem>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        db.MediaItems.Where(item => item.Status == MediaStatus.Active).ToListAsync(cancellationToken);

    private static void Refresh(MediaItem item, long size, DateTime lastWriteUtc, string? fingerprint)
    {
        item.Size = size;
        item.LastWriteTimeUtc = lastWriteUtc;
        if (fingerprint is not null) item.ContentFingerprint = fingerprint;
    }

    private async Task<TransactionScope> BeginAsync(CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null) return new TransactionScope(null);
        return new TransactionScope(await db.Database.BeginTransactionAsync(cancellationToken));
    }

    /// <summary>Joins an ambient transaction when one is open (the importer's), otherwise owns its own.</summary>
    private sealed class TransactionScope(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? owned) : IAsyncDisposable
    {
        public Task CommitAsync(CancellationToken cancellationToken) => owned is null ? Task.CompletedTask : owned.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => owned is null ? ValueTask.CompletedTask : owned.DisposeAsync();
    }
}
