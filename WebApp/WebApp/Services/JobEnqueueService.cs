using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>A stable server-side identity captured when a job is enqueued. Never a path and never a browser snapshot ID.</summary>
internal sealed record JobIdentity(long MediaItemId, long FolderId, long Size, DateTime LastWriteTimeUtc, string? ContentFingerprint, int ContentRevision);

/// <summary>A folder (or the nearest known ancestor of one) touched by an archive mutation.</summary>
internal sealed record JobFolderRef(long FolderId, bool Exact);

internal sealed record JobMutationItem(JobIdentity? Media, JobFolderRef? Folder, bool IsFolder);

/// <summary>
/// The only boundary where browser-visible snapshot IDs and physical entries become stable identities for durable jobs.
/// It builds the path-free JSON payload stored on the <c>Job</c> row; the in-memory queues keep carrying the full job record.
/// </summary>
internal sealed class JobEnqueueService(MediaReconciliationService reconciliation, FolderLocator locator, AppDbContext db)
{
    private static readonly JsonSerializerOptions Json = new();

    public async Task<string> BuildCutPayloadAsync(CutJob job, CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(new
        {
            kind = "cut",
            sources = await IdentitiesAsync([job.SourceEntry.PhysicalPath], cancellationToken),
            startSeconds = job.Start.TotalSeconds,
            endSeconds = job.End.TotalSeconds
        }, Json);

    public async Task<string> BuildCompositionPayloadAsync(CompositionJob job, CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(new
        {
            kind = "composition",
            sources = await IdentitiesAsync(job.OrderedSources.Select(source => source.PhysicalPath).ToList(), cancellationToken)
        }, Json);

    public async Task<string> BuildConversionPayloadAsync(VideoConversionJob job, VideoConversionSelectionDto? selection = null, CancellationToken cancellationToken = default) =>
        JsonSerializer.Serialize(new
        {
            kind = "conversion",
            action = job.Action.ToString(),
            categoryKey = job.Source.Category.Key,
            selection,
            outputHeight = job.Profile?.OutputHeight,
            sources = await IdentitiesAsync([job.Source.PhysicalPath], cancellationToken, MediaFile.CategoryOf(job.Source))
        }, Json);

    public async Task<string> BuildMutationPayloadAsync(ArchiveMutationJob job, CancellationToken cancellationToken = default)
    {
        var entries = job.BatchEntries is { Count: > 0 }
            ? job.BatchEntries.Select(entry => (entry.SourcePath, entry.IsFolder)).ToList()
            : [(job.SourcePath, job.IsFolder)];
        var items = new List<JobMutationItem>();
        foreach (var (path, isFolder) in entries)
        {
            items.Add(isFolder
                ? new JobMutationItem(null, await FolderRefAsync(path, cancellationToken), true)
                : new JobMutationItem((await IdentitiesAsync([path], cancellationToken)).FirstOrDefault(), null, false));
        }

        JobFolderRef? destination = null;
        if (job.DestinationPath is not null)
        {
            // Batch jobs carry per-entry destinations; every one lands in the same folder, so the first is representative.
            var destinationPath = job.BatchEntries is { Count: > 0 } ? job.BatchEntries[0].DestinationPath : job.DestinationPath;
            destination = await FolderRefAsync(Path.GetDirectoryName(destinationPath) ?? destinationPath, cancellationToken);
        }

        return JsonSerializer.Serialize(new { kind = "mutation", operation = job.Kind.ToString(), totalItems = job.TotalItems, items, destination }, Json);
    }

    private async Task<List<JobIdentity>> IdentitiesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken, string category = MediaIdentityClassifier.OtherCategory)
    {
        var identities = new List<JobIdentity>();
        foreach (var path in paths)
        {
            var file = reconciliation.DescribeFile(path, category);
            if (file is null) continue;
            // Prefer the existing Active row so capturing identity never reclassifies a book or comic under a guessed category.
            var item = await db.MediaItems.AsNoTracking().FirstOrDefaultAsync(media =>
                media.Status == MediaStatus.Active && media.RelativePath == file.RelativePath &&
                db.Folders.Any(folder => folder.Id == media.FolderId && folder.RootKey == file.RootKey && folder.RelativePath == ""), cancellationToken)
                ?? await reconciliation.EnsureAsync(file, cancellationToken);
            if (item is null) continue;
            identities.Add(new JobIdentity(item.Id, item.FolderId, item.Size, item.LastWriteTimeUtc, item.ContentFingerprint, item.ContentRevision));
        }

        return identities;
    }

    /// <summary>The folder row for a directory, or its nearest existing ancestor when no row exists yet.</summary>
    private async Task<JobFolderRef?> FolderRefAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var location = locator.LocateDirectory(directoryPath);
        for (var exact = true; location is not null; exact = false, location = location.Parent())
        {
            var id = await db.Folders.AsNoTracking()
                .Where(folder => folder.RootKey == location.RootKey && folder.RelativePath == location.RelativePath)
                .Select(folder => (long?)folder.Id).FirstOrDefaultAsync(cancellationToken);
            if (id is not null) return new JobFolderRef(id.Value, exact);
        }

        return null;
    }
}
