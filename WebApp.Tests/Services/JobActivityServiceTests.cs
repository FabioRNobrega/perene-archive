using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Identity;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class JobActivityServiceTests : IDisposable
{
    private readonly JobTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private async Task<string> AddUserAsync(string name, string display)
    {
        await using var db = _db.NewContext();
        var user = new ApplicationUser { UserName = name, NormalizedUserName = name.ToUpperInvariant(), DisplayName = display, CreatedUtc = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<IReadOnlyList<JobSummaryDto>> ListAsync(string? userId, bool isAdmin)
    {
        await using var db = _db.NewContext();
        return await new JobActivityService(db).ListAsync(userId, isAdmin, CancellationToken.None);
    }

    private static CutJob Cut(string id, string? actor) =>
        new(id, new VideoFileEntry("i", "/server-only/Beach Day.mp4", "x", "Beach Day.mp4", ".mp4", 1, DateTime.UtcNow), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(9), actor);

    private static ArchiveMutationJob Mutation(string id, string? actor) =>
        new(id, ArchiveMutationKind.MoveToTrash, "/server-only/a.mp4", null, false, 3, "3 items to Trash", null, actor);

    [Fact]
    public async Task Lists_compositions_moves_and_cuts_newest_first_with_titles_and_starter_names()
    {
        var alice = await AddUserAsync("alice", "Alice Archive");
        _db.Cuts.Seed(Cut("cut", alice), "{}");
        await Task.Delay(5);
        _db.Mutation.Seed(Mutation("move", alice));
        _db.Mutation.ReportProgress("move", 2);
        await Task.Delay(5);
        _db.Composition.Seed("comp", alice, "{\"sources\":[{},{},{}]}");

        var list = await ListAsync(alice, isAdmin: false);

        Assert.Equal(["comp", "move", "cut"], list.Select(job => job.JobId));
        Assert.All(list, job => Assert.Equal("Alice Archive", job.StartedBy));
        Assert.Equal("Composition of 3 clips", list[0].Title);
        Assert.Equal("Composition", list[0].Kind);
        Assert.Equal("3 items to Trash", list[1].Title);
        Assert.Equal((2, 3), (list[1].ProcessedItems, list[1].TotalItems));
        Assert.Equal("Beach Day.mp4 (0:02–0:09)", list[2].Title);
        Assert.Equal("Cut", list[2].Kind);
        Assert.DoesNotContain("server-only", string.Join('|', list.Select(job => job.Title + job.Detail)));
    }

    [Fact]
    public async Task Excludes_conversions_and_hides_other_members_jobs_from_members_but_not_from_admins()
    {
        var alice = await AddUserAsync("alice", "Alice");
        var bob = await AddUserAsync("bob", "Bob");
        _db.Cuts.Seed(Cut("alice-cut", alice));
        _db.Cuts.Seed(Cut("bob-cut", bob));
        _db.Conversion.Seed(new VideoConversionJob("conv", new ArchiveItemEntry("s", ArchiveCategory.Defaults[0], "/x/a.mkv", "a.mkv", ArchiveItemKind.File, ".mkv", 1, DateTime.UtcNow, true),
            MediaAction.Keep, new VideoConversionProbeResult("mp4", "h264", null, null, 1, 1, TimeSpan.Zero), null, alice));

        Assert.Equal(["alice-cut"], (await ListAsync(alice, false)).Select(job => job.JobId));
        Assert.Equal(["bob-cut"], (await ListAsync(bob, false)).Select(job => job.JobId));
        Assert.Equal(["alice-cut", "bob-cut"], (await ListAsync("someone-else", true)).Select(job => job.JobId).OrderBy(id => id));
        Assert.Empty(await ListAsync(null, false));
    }

    [Fact]
    public async Task A_deleted_owner_shows_as_a_removed_account_to_admins_only()
    {
        var alice = await AddUserAsync("alice", "Alice");
        _db.Cuts.Seed(Cut("cut", alice));
        await using (var db = _db.NewContext())
        {
            db.Users.Remove(await db.Users.SingleAsync(user => user.Id == alice));
            await db.SaveChangesAsync();
        }

        Assert.Equal("Removed account", Assert.Single(await ListAsync("admin", true)).StartedBy);
        Assert.Empty(await ListAsync(alice, false));
    }

    [Fact]
    public async Task Failed_outcomes_and_interrupted_cuts_carry_their_generic_detail()
    {
        _db.Cuts.Seed(Cut("failed", null));
        _db.Cuts.MarkFailed("failed", "The cut could not be created.");
        _db.Cuts.Seed(Cut("interrupted", null));
        _db.Cuts.MarkProcessing("interrupted");
        _db.Composition.Seed("comp");
        _db.Composition.MarkFailed("comp", "clip probe failed");
        await using (var db = _db.NewContext()) await SqliteJobStore.MarkInterruptedAsync(db);

        var list = (await ListAsync(null, true)).ToDictionary(job => job.JobId);

        Assert.Equal("The cut could not be created.", list["failed"].Detail);
        Assert.Equal("Failed", list["interrupted"].State);
        Assert.Equal("Interrupted by a restart.", list["interrupted"].Detail);
        Assert.Equal("clip probe failed", list["comp"].Detail);
        Assert.Equal("Composition", list["comp"].Title);
    }

    [Fact]
    public async Task Reports_queued_started_and_finished_times_and_leaves_never_started_jobs_without_a_start()
    {
        _db.Cuts.Seed(Cut("ran", null));
        _db.Cuts.MarkProcessing("ran");
        _db.Cuts.MarkCompleted("ran");
        _db.Cuts.Seed(Cut("waiting", null));

        var list = (await ListAsync(null, true)).ToDictionary(job => job.JobId);

        Assert.NotNull(list["ran"].StartedUtc);
        Assert.NotNull(list["ran"].FinishedUtc);
        Assert.True(list["ran"].StartedUtc >= list["ran"].CreatedUtc);
        Assert.True(list["ran"].FinishedUtc >= list["ran"].StartedUtc);
        Assert.Null(list["waiting"].StartedUtc);
        Assert.Null(list["waiting"].FinishedUtc);
    }
}
