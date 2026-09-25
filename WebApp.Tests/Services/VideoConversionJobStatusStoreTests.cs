using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class VideoConversionJobStatusStoreTests
{
    [Fact]
    public void Pause_resume_and_stop_are_guarded_and_stop_rejects_late_progress()
    {
        var store = new VideoConversionJobStatusStore();
        var job = new VideoConversionJob("job", new ArchiveItemEntry("source", ArchiveCategory.Defaults[0], "/server-only/source.mp4", "source.mp4", ArchiveItemKind.File, ".mp4", 12, DateTime.UtcNow, true), MediaAction.FullTranscode, new VideoConversionProbeResult("matroska", "vp9", "opus", null, 10, 10, TimeSpan.FromSeconds(20)));
        store.Seed(job);

        Assert.False(store.Pause(job.JobId));
        store.Processing(job.JobId);
        Assert.True(store.Pause(job.JobId));
        Assert.True(store.HasActiveSource(job.Source.Id));
        store.Progress(job.JobId, new VideoConversionProgress(12, 1));
        Assert.Equal(VideoConversionJobState.Paused, store.Get(job.JobId)!.State);
        Assert.True(store.Resume(job.JobId));
        Assert.True(store.Stop(job.JobId));
        store.Progress(job.JobId, new VideoConversionProgress(null, null, true));
        store.Complete(job.JobId, "output", 5);

        Assert.Equal(VideoConversionJobState.Stopped, store.Get(job.JobId)!.State);
        Assert.False(store.HasActiveSource(job.Source.Id));
    }

    [Fact]
    public void Only_one_job_runs_at_a_time_and_pausing_frees_the_slot()
    {
        var store = new VideoConversionJobStatusStore();
        VideoConversionJob Make(string id) => new(id, new ArchiveItemEntry(id, ArchiveCategory.Defaults[0], "/server-only/" + id + ".mp4", id + ".mp4", ArchiveItemKind.File, ".mp4", 12, DateTime.UtcNow, true), MediaAction.FullTranscode, new VideoConversionProbeResult("matroska", "vp9", "opus", null, 10, 10, TimeSpan.FromSeconds(20)));
        store.Seed(Make("a")); store.Seed(Make("b"));

        Assert.True(store.TryBeginProcessing("a"));
        Assert.False(store.TryBeginProcessing("b"));
        Assert.True(store.Pause("a"));
        Assert.True(store.TryBeginProcessing("b"));
        Assert.False(store.Resume("a"));
        Assert.True(store.Stop("b"));
        Assert.NotNull(store.Get("b")!.FinishedAtUtc);
        Assert.True(store.Resume("a"));
    }
}
