using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApp.Authorization;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// The execution-time gate every worker already calls. It adds the durable checks in front of the existing fresh folder authorization
/// (<see cref="FolderJobAuthorizer"/>): the job's owner must still exist, and every media identity captured at enqueue must still
/// be Active at the same content revision. Any failure is a plain "no"; workers report one generic message.
/// </summary>
internal sealed class JobExecutionRecheckService(FolderJobAuthorizer inner, IServiceScopeFactory scopes) : IFolderJobAuthorizer
{
    public async Task<bool> CanRunAsync(CutJob job, CancellationToken cancellationToken) =>
        await DurableChecksPassAsync(job.JobId, job.ActorUserId, cancellationToken) && await inner.CanRunAsync(job, cancellationToken);

    public async Task<bool> CanRunAsync(CompositionJob job, CancellationToken cancellationToken) =>
        await DurableChecksPassAsync(job.JobId, job.ActorUserId, cancellationToken) && await inner.CanRunAsync(job, cancellationToken);

    public async Task<bool> CanRunAsync(VideoConversionJob job, CancellationToken cancellationToken) =>
        await DurableChecksPassAsync(job.JobId, job.ActorUserId, cancellationToken) && await inner.CanRunAsync(job, cancellationToken);

    public async Task<bool> CanRunAsync(ArchiveMutationJob job, CancellationToken cancellationToken) =>
        await DurableChecksPassAsync(job.JobId, job.ActorUserId, cancellationToken) && await inner.CanRunAsync(job, cancellationToken);

    public Task<bool> CanReadAsync(string? actorUserId, VideoFileEntry entry, CancellationToken cancellationToken) =>
        inner.CanReadAsync(actorUserId, entry, cancellationToken);

    private async Task<bool> DurableChecksPassAsync(string jobId, string? actorUserId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(job => job.Id == jobId, cancellationToken);
        // A job with no durable row was never enqueued through the durable path (an internal job); the folder check still applies.
        if (row is null) return true;

        // "Internal" means the job never had an owner. A job that had one whose account is now gone must fail, not run unchecked.
        if (actorUserId is not null && row.UserId is null) return false;

        foreach (var identity in ReadIdentities(row.PayloadJson))
        {
            var current = await db.MediaItems.AsNoTracking().Where(item => item.Id == identity.MediaItemId)
                .Select(item => new { item.Status, item.ContentRevision }).FirstOrDefaultAsync(cancellationToken);
            if (current is null || current.Status != MediaStatus.Active || current.ContentRevision != identity.ContentRevision) return false;
        }

        return true;
    }

    private static IEnumerable<JobIdentity> ReadIdentities(string? payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) yield break;
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        if (root.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
            foreach (var element in sources.EnumerateArray())
                if (ReadIdentity(element) is { } identity) yield return identity;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var element in items.EnumerateArray())
                if (element.TryGetProperty("Media", out var media) && media.ValueKind == JsonValueKind.Object && ReadIdentity(media) is { } identity)
                    yield return identity;
    }

    private static JobIdentity? ReadIdentity(JsonElement element) =>
        element.TryGetProperty("MediaItemId", out var id) && id.TryGetInt64(out var mediaItemId) &&
        element.TryGetProperty("ContentRevision", out var revision) && revision.TryGetInt32(out var contentRevision)
            ? new JobIdentity(mediaItemId, 0, 0, default, null, contentRevision)
            : null;
}
