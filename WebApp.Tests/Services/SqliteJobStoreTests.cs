using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data.Entities;
using WebApp.Identity;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class SqliteJobStoreTests : IDisposable
{
    private readonly JobTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static ArchiveMutationJob Mutation(string id, string? actor = null) =>
        new(id, ArchiveMutationKind.Move, "/server-only/a.mp4", "/server-only/b.mp4", false, 1, "a.mp4", null, actor);

    private static VideoConversionJob Conversion(string id, string? actor = null) =>
        new(id, new ArchiveItemEntry(id, ArchiveCategory.Defaults[0], "/server-only/" + id + ".mkv", id + ".mkv", ArchiveItemKind.File, ".mkv", 12, DateTime.UtcNow, true),
            MediaAction.FullTranscode, new VideoConversionProbeResult("matroska", "vp9", "opus", null, 10, 10, TimeSpan.FromSeconds(20)), null, actor);

    private async Task<string> AddUserAsync(string name)
    {
        await using var db = _db.NewContext();
        var user = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), DisplayName = name, CreatedUtc = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public void Status_survives_a_new_store_instance_over_the_same_database()
    {
        _db.Composition.Seed("job-1");
        _db.Composition.MarkProcessing("job-1");
        _db.Composition.MarkCompleted("job-1", "video-id");

        var restarted = new SqliteJobStore(_db.Scopes);

        var status = Assert.Single(((ICompositionJobStatusStore)restarted).GetAll());
        Assert.Equal(CompositionJobState.Completed, status.State);
        Assert.Equal("video-id", status.ResultVideoId);
    }

    [Fact]
    public async Task Startup_marks_every_unfinished_job_as_interrupted_and_leaves_finished_ones_alone()
    {
        _db.Composition.Seed("comp-pending");
        _db.Composition.Seed("comp-done");
        _db.Composition.MarkCompleted("comp-done", "v");
        _db.Mutation.Seed(Mutation("mut-processing"));
        _db.Mutation.MarkProcessing("mut-processing");
        _db.Conversion.Seed(Conversion("conv-paused"));
        _db.Conversion.Processing("conv-paused");
        _db.Conversion.Pause("conv-paused");
        _db.Cuts.Seed(new CutJob("cut-pending", null!, TimeSpan.Zero, TimeSpan.FromSeconds(1)));

        await using (var db = _db.NewContext())
            Assert.Equal(4, await SqliteJobStore.MarkInterruptedAsync(db));

        Assert.Equal(CompositionJobState.Failed, _db.Composition.GetAll().Single(s => s.JobId == "comp-pending").State);
        Assert.Contains("restart", _db.Composition.GetAll().Single(s => s.JobId == "comp-pending").Diagnostic);
        Assert.Equal(CompositionJobState.Completed, _db.Composition.GetAll().Single(s => s.JobId == "comp-done").State);
        Assert.Equal(ArchiveMutationJobState.Failed, _db.Mutation.Get("mut-processing")!.State);
        var conversion = _db.Conversion.Get("conv-paused")!;
        Assert.Equal(VideoConversionJobState.Failed, conversion.State);
        Assert.NotNull(conversion.FinishedAtUtc);
        await using var check = _db.NewContext();
        Assert.Equal("Failed", (await check.Jobs.SingleAsync(job => job.Id == "cut-pending")).State);
        // A second startup finds nothing left to interrupt: jobs are never replayed.
        Assert.Equal(0, await SqliteJobStore.MarkInterruptedAsync(check));
    }

    [Fact]
    public async Task Members_see_only_their_own_jobs_and_admins_see_everything_even_after_the_owner_is_deleted()
    {
        var alice = await AddUserAsync("alice");
        var bob = await AddUserAsync("bob");
        _db.Conversion.Seed(Conversion("alice-job", alice));
        IJobVisibility visibility = _db.Store;

        Assert.True(visibility.IsVisibleTo("alice-job", alice, isAdmin: false));
        Assert.False(visibility.IsVisibleTo("alice-job", bob, isAdmin: false));
        Assert.False(visibility.IsVisibleTo("alice-job", null, isAdmin: false));
        Assert.True(visibility.IsVisibleTo("alice-job", bob, isAdmin: true));

        await using (var db = _db.NewContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(user => user.Id == alice));
            await db.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        Assert.Null((await check.Jobs.SingleAsync(job => job.Id == "alice-job")).UserId);
        Assert.False(visibility.IsVisibleTo("alice-job", alice, isAdmin: false));
        Assert.True(visibility.IsVisibleTo("alice-job", bob, isAdmin: true));
    }

    [Fact]
    public void Conversion_progress_writes_are_throttled_but_finalizing_always_lands()
    {
        _db.Conversion.Seed(Conversion("job"));
        Assert.True(_db.Conversion.TryBeginProcessing("job"));

        _db.Conversion.Progress("job", new VideoConversionProgress(5, 1));
        _db.Conversion.Progress("job", new VideoConversionProgress(9, 1));

        Assert.Equal(5, _db.Conversion.Get("job")!.ProcessedDurationSeconds);

        _db.Conversion.Progress("job", new VideoConversionProgress(null, null, true));
        Assert.Equal(VideoConversionJobState.Finalizing, _db.Conversion.Get("job")!.State);
    }

    [Fact]
    public void Terminal_jobs_beyond_the_retention_cap_are_pruned_oldest_first()
    {
        ICompositionJobStatusStore store = new SqliteJobStore(_db.Scopes, retainedTerminalJobsPerType: 3);
        for (var i = 0; i < 6; i++)
        {
            store.Seed($"job-{i}");
            store.MarkFailed($"job-{i}", "x");
        }

        store.Seed("live");

        var ids = store.GetAll().Select(status => status.JobId).ToList();
        Assert.Equal(["job-3", "job-4", "job-5", "live"], ids);
    }

    [Fact]
    public async Task Restart_resets_a_failed_or_stopped_job_in_place_and_rejects_every_other_state()
    {
        var alice = await AddUserAsync("alice");
        var bob = await AddUserAsync("bob");
        var job = Conversion("job", alice);
        _db.Conversion.Seed(job, "{\"old\":true}");
        Assert.False(_db.Conversion.Restart(job, "{}")); // Pending
        Assert.False(_db.Conversion.Restart(Conversion("unknown", alice), "{}"));

        Assert.True(_db.Conversion.TryBeginProcessing("job"));
        Assert.False(_db.Conversion.Restart(job, "{}")); // Processing
        _db.Conversion.Progress("job", new VideoConversionProgress(7, 1));
        _db.Conversion.Fail("job", "Interrupted by a restart.");

        Assert.True(_db.Conversion.Restart(Conversion("job", bob), "{\"new\":true}"));

        var status = _db.Conversion.Get("job")!;
        Assert.Equal(VideoConversionJobState.Pending, status.State);
        Assert.Null(status.Diagnostic);
        Assert.Null(status.FinishedAtUtc);
        Assert.Null(status.ProcessedDurationSeconds);
        await using var db = _db.NewContext();
        var row = await db.Jobs.SingleAsync();
        Assert.Equal(bob, row.UserId);
        Assert.Equal("{\"new\":true}", row.PayloadJson);
        Assert.Null(row.FinishedUtc);

        // A stopped job can be restarted too.
        Assert.True(_db.Conversion.TryBeginProcessing("job"));
        Assert.True(_db.Conversion.Stop("job"));
        Assert.True(_db.Conversion.Restart(Conversion("job", bob), "{}"));
        Assert.Equal(VideoConversionJobState.Pending, _db.Conversion.Get("job")!.State);
    }

    [Fact]
    public async Task Cut_recorder_tracks_states_and_never_overwrites_a_terminal_one()
    {
        _db.Cuts.Seed(new CutJob("cut", null!, TimeSpan.Zero, TimeSpan.FromSeconds(1)), "{}");
        _db.Cuts.MarkProcessing("cut");
        await using (var db = _db.NewContext()) Assert.Equal("Processing", (await db.Jobs.SingleAsync()).State);

        _db.Cuts.MarkCompleted("cut");
        _db.Cuts.MarkFailed("cut");
        _db.Cuts.MarkProcessing("cut");
        _db.Cuts.MarkFailed("missing"); // unknown ids are ignored

        await using var check = _db.NewContext();
        var row = await check.Jobs.SingleAsync();
        Assert.Equal("Completed", row.State);
        Assert.NotNull(row.FinishedUtc);
        Assert.Equal(JobType.Cut, row.Type);
    }

    [Fact]
    public void Removing_a_conversion_deletes_its_row_and_frees_its_source()
    {
        _db.Conversion.Seed(Conversion("job"));
        Assert.True(_db.Conversion.HasActiveSource("job"));

        _db.Conversion.Remove("job");

        Assert.Null(_db.Conversion.Get("job"));
        Assert.False(_db.Conversion.HasActiveSource("job"));
    }

    [Fact]
    public void Transitions_on_unknown_jobs_are_ignored_and_finished_jobs_do_not_move()
    {
        _db.Composition.MarkProcessing("nope");
        _db.Composition.MarkFailed("nope", "x");
        Assert.Empty(_db.Composition.GetAll());

        _db.Mutation.Seed(Mutation("m"));
        _db.Mutation.MarkCompleted("m");
        _db.Mutation.MarkFailed("m", "late");
        _db.Mutation.ReportProgress("m", 99);
        Assert.Equal(ArchiveMutationJobState.Completed, _db.Mutation.Get("m")!.State);
        Assert.Null(_db.Mutation.Get("m")!.Diagnostic);
    }

    [Fact]
    public void Jobs_of_different_types_never_leak_into_each_others_lists()
    {
        _db.Composition.Seed("c");
        _db.Mutation.Seed(Mutation("m"));
        _db.Conversion.Seed(Conversion("v"));

        Assert.Equal("c", Assert.Single(_db.Composition.GetAll()).JobId);
        Assert.Equal("m", Assert.Single(_db.Mutation.GetAll()).JobId);
        Assert.Equal("v", Assert.Single(_db.Conversion.GetAll()).JobId);
        Assert.Null(_db.Mutation.Get("c"));
    }

    [Fact]
    public async Task Started_time_is_set_when_a_job_first_runs_and_cleared_by_a_retry()
    {
        _db.Composition.Seed("comp");
        _db.Mutation.Seed(Mutation("move"));
        _db.Cuts.Seed(new CutJob("cut", null!, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        _db.Conversion.Seed(Conversion("conv"));
        await using (var queued = _db.NewContext()) Assert.All(await queued.Jobs.ToListAsync(), job => Assert.Null(job.StartedUtc));

        _db.Composition.MarkProcessing("comp");
        _db.Mutation.MarkProcessing("move");
        _db.Cuts.MarkProcessing("cut");
        Assert.True(_db.Conversion.TryBeginProcessing("conv"));

        await using (var running = _db.NewContext())
        {
            Assert.All(await running.Jobs.ToListAsync(), job => Assert.NotNull(job.StartedUtc));
            var first = (await running.Jobs.SingleAsync(job => job.Id == "comp")).StartedUtc;
            _db.Composition.MarkCompleted("comp", "v");
            await using var after = _db.NewContext();
            Assert.Equal(first, (await after.Jobs.SingleAsync(job => job.Id == "comp")).StartedUtc);
        }

        _db.Conversion.Fail("conv", "x");
        Assert.True(_db.Conversion.Restart(Conversion("conv"), "{}"));
        await using var retried = _db.NewContext();
        Assert.Null((await retried.Jobs.SingleAsync(job => job.Id == "conv")).StartedUtc);
    }
}
