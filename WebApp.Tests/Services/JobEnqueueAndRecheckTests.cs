using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data.Entities;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

/// <summary>Stable-identity payloads at enqueue and the durable checks every worker runs at execution, on a real-Identity host.</summary>
public sealed class JobEnqueueAndRecheckTests : IDisposable
{
    private readonly MediaTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string VideoPath => Path.Combine(_host.ArchivePath, "Videos", "clip one.mp4");

    private VideoFileEntry Source()
    {
        File.WriteAllBytes(VideoPath, [1, 2, 3]);
        var info = new FileInfo(VideoPath);
        return new VideoFileEntry("browser-snapshot-id", VideoPath, "clip one.mp4", "clip one.mp4", ".mp4", info.Length, info.LastWriteTimeUtc);
    }

    [Fact]
    public async Task Cut_payload_holds_stable_identity_only_never_paths_or_snapshot_ids()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        var job = new CutJob("job", Source(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), admin);

        var payload = await _host.AsAsync(admin, sp => sp.GetRequiredService<JobEnqueueService>().BuildCutPayloadAsync(job));

        Assert.Contains("MediaItemId", payload);
        Assert.Contains("ContentRevision", payload);
        Assert.DoesNotContain("clip one", payload);
        Assert.DoesNotContain("Videos", payload);
        Assert.DoesNotContain(_host.ArchivePath, payload);
        Assert.DoesNotContain("browser-snapshot-id", payload);
        await _host.WithDbAsync(async (_, db) => Assert.Single(await db.MediaItems.ToListAsync()));
    }

    [Fact]
    public async Task Mutation_payload_uses_folder_and_media_identities_without_paths_or_labels()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        Source();
        var folder = Path.Combine(_host.ArchivePath, "Videos", "Holiday Trip");
        Directory.CreateDirectory(folder);
        var batch = new ArchiveMutationJob("m", ArchiveMutationKind.BatchMove, VideoPath, Path.Combine(_host.ArchivePath, "Pictures", "clip one.mp4"), false, 2, "secret label",
            [new ArchiveMutationBatchEntry(VideoPath, Path.Combine(_host.ArchivePath, "Pictures", "clip one.mp4"), false, 1),
             new ArchiveMutationBatchEntry(folder, Path.Combine(_host.ArchivePath, "Pictures", "Holiday Trip"), true, 0)], admin);

        var payload = await _host.AsAsync(admin, sp => sp.GetRequiredService<JobEnqueueService>().BuildMutationPayloadAsync(batch));

        Assert.Contains("FolderId", payload);
        Assert.Contains("MediaItemId", payload);
        Assert.DoesNotContain("Holiday", payload);
        Assert.DoesNotContain("clip one", payload);
        Assert.DoesNotContain("secret label", payload);
    }

    private async Task<(string UserId, string Payload, long MediaId)> EnqueuedCutAsync(string name)
    {
        var userId = await _host.AddUserAsync(name, admin: true);
        var job = new CutJob("job-" + name, Source(), TimeSpan.Zero, TimeSpan.FromSeconds(1), userId);
        var payload = await _host.AsAsync(userId, sp => sp.GetRequiredService<JobEnqueueService>().BuildCutPayloadAsync(job));
        await _host.WithDbAsync(async (_, db) =>
        {
            db.Jobs.Add(new Job { Id = job.JobId, Type = JobType.Cut, State = "Pending", UserId = userId, PayloadJson = payload, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var mediaId = await _host.WithDbAsync(async (_, db) => (await db.MediaItems.SingleAsync()).Id);
        return (userId, payload, mediaId);
    }

    private IFolderJobAuthorizer Authorizer => _host.Factory.Services.GetRequiredService<IFolderJobAuthorizer>();

    [Fact]
    public async Task A_job_whose_captured_identity_is_still_active_passes_the_recheck()
    {
        var (userId, _, _) = await EnqueuedCutAsync("owner");
        var job = new CutJob("job-owner", Source(), TimeSpan.Zero, TimeSpan.FromSeconds(1), userId);

        Assert.True(await Authorizer.CanRunAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task A_replaced_or_missing_media_identity_fails_the_recheck()
    {
        var (userId, _, mediaId) = await EnqueuedCutAsync("owner");
        var job = new CutJob("job-owner", Source(), TimeSpan.Zero, TimeSpan.FromSeconds(1), userId);

        await _host.WithDbAsync(async (_, db) =>
        {
            (await db.MediaItems.SingleAsync(item => item.Id == mediaId)).ContentRevision++;
            await db.SaveChangesAsync();
        });
        Assert.False(await Authorizer.CanRunAsync(job, CancellationToken.None));

        await _host.WithDbAsync(async (_, db) =>
        {
            var item = await db.MediaItems.SingleAsync(media => media.Id == mediaId);
            item.ContentRevision--;
            item.Status = MediaStatus.Missing;
            await db.SaveChangesAsync();
        });
        Assert.False(await Authorizer.CanRunAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task A_job_whose_owner_account_was_deleted_fails_instead_of_running_as_internal()
    {
        var (userId, _, _) = await EnqueuedCutAsync("owner");
        var job = new CutJob("job-owner", Source(), TimeSpan.Zero, TimeSpan.FromSeconds(1), userId);
        await _host.WithDbAsync(async (_, db) =>
        {
            // What the SetNull foreign key does when the account is deleted, without needing the full lifecycle service.
            (await db.Jobs.SingleAsync(row => row.Id == job.JobId)).UserId = null;
            await db.SaveChangesAsync();
        });

        Assert.False(await Authorizer.CanRunAsync(job, CancellationToken.None));
        // A genuinely internal job (no actor, no owner) is still unchecked.
        Assert.True(await Authorizer.CanRunAsync(job with { ActorUserId = null }, CancellationToken.None));
    }

    [Fact]
    public async Task A_job_with_no_durable_row_is_internal_and_only_the_folder_check_applies()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        var job = new CutJob("never-persisted", Source(), TimeSpan.Zero, TimeSpan.FromSeconds(1), admin);

        Assert.True(await Authorizer.CanRunAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task Every_job_type_fails_when_its_owner_account_is_gone()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        var source = Source();
        await _host.WithDbAsync(async (_, db) =>
        {
            foreach (var (id, type) in new[] { ("comp", JobType.Composition), ("mut", JobType.ArchiveMutation), ("conv", JobType.VideoConversion) })
                db.Jobs.Add(new Job { Id = id, Type = type, State = "Pending", UserId = null, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        var entry = new ArchiveItemEntry("i", ArchiveCategory.Defaults[0], VideoPath, "clip one.mp4", ArchiveItemKind.File, ".mp4", 3, DateTime.UtcNow, true);

        Assert.False(await Authorizer.CanRunAsync(new CompositionJob("comp", [source, source], admin), CancellationToken.None));
        Assert.False(await Authorizer.CanRunAsync(new ArchiveMutationJob("mut", ArchiveMutationKind.MoveToTrash, VideoPath, null, false, 1, "x", null, admin), CancellationToken.None));
        Assert.False(await Authorizer.CanRunAsync(new VideoConversionJob("conv", entry, MediaAction.Keep, new VideoConversionProbeResult("mp4", "h264", null, null, 1, 1, TimeSpan.Zero), null, admin), CancellationToken.None));
    }

    [Fact]
    public async Task A_mutation_whose_captured_file_was_replaced_fails_the_recheck()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        Source();
        var job = new ArchiveMutationJob("mut", ArchiveMutationKind.MoveToTrash, VideoPath, null, false, 1, "x", null, admin);
        var payload = await _host.AsAsync(admin, sp => sp.GetRequiredService<JobEnqueueService>().BuildMutationPayloadAsync(job));
        await _host.WithDbAsync(async (_, db) =>
        {
            db.Jobs.Add(new Job { Id = "mut", Type = JobType.ArchiveMutation, State = "Pending", UserId = admin, PayloadJson = payload, CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });
        Assert.True(await Authorizer.CanRunAsync(job, CancellationToken.None));

        await _host.WithDbAsync(async (_, db) =>
        {
            (await db.MediaItems.SingleAsync()).ContentRevision++;
            await db.SaveChangesAsync();
        });

        Assert.False(await Authorizer.CanRunAsync(job, CancellationToken.None));
    }

    [Fact]
    public async Task Composition_and_conversion_payloads_hold_identities_and_selection_only()
    {
        var admin = await _host.AddUserAsync("admin2", admin: true);
        var source = Source();
        var composition = new CompositionJob("c", [source], admin);
        var entry = new ArchiveItemEntry("snapshot", ArchiveCategory.Defaults[0], VideoPath, "clip one.mp4", ArchiveItemKind.File, ".mp4", 3, DateTime.UtcNow, true);
        var conversion = new VideoConversionJob("v", entry, MediaAction.FullTranscode, new VideoConversionProbeResult("mp4", "h264", null, null, 1, 1, TimeSpan.Zero), null, admin);

        var compositionPayload = await _host.AsAsync(admin, sp => sp.GetRequiredService<JobEnqueueService>().BuildCompositionPayloadAsync(composition));
        var conversionPayload = await _host.AsAsync(admin, sp => sp.GetRequiredService<JobEnqueueService>().BuildConversionPayloadAsync(conversion, new VideoConversionSelectionDto("compress", 720)));

        Assert.Contains("MediaItemId", compositionPayload);
        Assert.DoesNotContain("clip one", compositionPayload);
        Assert.Contains("\"categoryKey\":\"videos\"", conversionPayload);
        Assert.Contains("720", conversionPayload);
        Assert.DoesNotContain("snapshot", conversionPayload);
        Assert.DoesNotContain(_host.ArchivePath, conversionPayload);
    }
}
