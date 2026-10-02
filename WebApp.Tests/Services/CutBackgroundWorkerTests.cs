using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WebApp.Authorization;
using WebApp.Configuration;
using WebApp.Data.Entities;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

/// <summary>The worker must leave a durable, generic outcome on the cut's Job row for every way a cut can end.</summary>
public sealed class CutBackgroundWorkerTests : IDisposable
{
    private readonly JobTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static CutJob NewJob(string id = "cut-1") =>
        new(id, new VideoFileEntry("id", "/server-only/clip.mp4", "clip.mp4", "clip.mp4", ".mp4", 1, DateTime.UtcNow), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(75));

    private async Task<(Job Row, CutJobStatus Status, FakeCuts Cuts)> RunAsync(bool allowed, Func<CutJob, Task<CutGenerationResult>> generate)
    {
        var job = NewJob();
        var queue = new CutJobQueue(Options.Create(new VideoCutOptions { Path = "/unused" }));
        var cuts = new FakeCuts();
        _db.Cuts.Seed(job, "{}");
        var worker = new CutBackgroundWorker(queue, new FakeGenerator(generate), cuts, new FakeAuthorizer(allowed), _db.Cuts, NullLogger<CutBackgroundWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        queue.TryEnqueue(job);
        try
        {
            for (var i = 0; i < 100; i++)
            {
                await using var poll = _db.NewContext();
                if (JobStore.IsTerminal(JobType.Cut, (await poll.Jobs.SingleAsync()).State)) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        await using var db = _db.NewContext();
        var row = await db.Jobs.SingleAsync();
        return (row, System.Text.Json.JsonSerializer.Deserialize<CutJobStatus>(row.StatusJson!, SqliteJobStore.JsonOptions)!, cuts);
    }

    private static class JobStore
    {
        public static bool IsTerminal(JobType type, string state) => state is "Completed" or "Failed";
    }

    [Fact]
    public async Task A_successful_cut_is_recorded_as_completed_and_the_cut_list_is_rescanned()
    {
        var (row, status, cuts) = await RunAsync(true, _ => Task.FromResult(CutGenerationResult.Success()));

        Assert.Equal("Completed", row.State);
        Assert.Equal("Completed", status.State);
        Assert.Null(status.Diagnostic);
        Assert.NotNull(row.FinishedUtc);
        Assert.Equal(1, cuts.Scans);
        Assert.Equal("clip.mp4 (0:05–1:15)", status.Label);
    }

    [Fact]
    public async Task A_generator_failure_is_recorded_as_failed_with_its_diagnostic()
    {
        var (row, status, cuts) = await RunAsync(true, _ => Task.FromResult(CutGenerationResult.Failed("ffmpeg could not cut this media.")));

        Assert.Equal("Failed", row.State);
        Assert.Equal("ffmpeg could not cut this media.", status.Diagnostic);
        Assert.Equal(0, cuts.Scans);
    }

    [Fact]
    public async Task A_cancelled_cut_is_recorded_as_failed()
    {
        var (row, status, _) = await RunAsync(true, _ => Task.FromResult(CutGenerationResult.Cancelled()));

        Assert.Equal("Failed", row.State);
        Assert.Equal("The cut was cancelled.", status.Diagnostic);
    }

    [Fact]
    public async Task An_unexpected_exception_is_recorded_as_failed_with_a_generic_message()
    {
        var (row, status, _) = await RunAsync(true, _ => throw new InvalidOperationException("/server-only/secret path"));

        Assert.Equal("Failed", row.State);
        Assert.Equal("Unexpected error during cut generation.", status.Diagnostic);
        Assert.DoesNotContain("secret", row.StatusJson);
    }

    [Fact]
    public async Task A_cut_whose_requester_lost_access_is_failed_without_running_the_generator()
    {
        var generated = false;
        var (row, status, _) = await RunAsync(false, _ => { generated = true; return Task.FromResult(CutGenerationResult.Success()); });

        Assert.False(generated);
        Assert.Equal("Failed", row.State);
        Assert.Equal("You no longer have access to complete this cut.", status.Diagnostic);
    }

    private sealed class FakeGenerator(Func<CutJob, Task<CutGenerationResult>> generate) : ICutGenerator
    {
        public Task<CutGenerationResult> GenerateAsync(CutJob job, CancellationToken cancellationToken) => generate(job);
    }

    private sealed class FakeCuts : IVideoCutService
    {
        public int Scans;
        public Task<IReadOnlyList<VideoFileEntry>> ScanAsync(CancellationToken cancellationToken = default)
        {
            Scans++;
            return Task.FromResult<IReadOnlyList<VideoFileEntry>>([]);
        }

        public IReadOnlyList<VideoFileEntry> GetCurrentSnapshot() => [];
        public bool TryResolve(string id, out VideoFileEntry? entry) { entry = null; return false; }
    }

    private sealed class FakeAuthorizer(bool allowed) : IFolderJobAuthorizer
    {
        public Task<bool> CanRunAsync(CutJob job, CancellationToken cancellationToken) => Task.FromResult(allowed);
        public Task<bool> CanRunAsync(CompositionJob job, CancellationToken cancellationToken) => Task.FromResult(allowed);
        public Task<bool> CanRunAsync(VideoConversionJob job, CancellationToken cancellationToken) => Task.FromResult(allowed);
        public Task<bool> CanRunAsync(ArchiveMutationJob job, CancellationToken cancellationToken) => Task.FromResult(allowed);
        public Task<bool> CanReadAsync(string? actorUserId, VideoFileEntry entry, CancellationToken cancellationToken) => Task.FromResult(allowed);
    }
}
