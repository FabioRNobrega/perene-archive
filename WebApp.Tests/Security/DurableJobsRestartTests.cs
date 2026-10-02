using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Security;

public sealed class DurableJobsRestartTests
{
    [Fact]
    public void A_host_restart_fails_unfinished_jobs_as_interrupted_and_keeps_finished_ones()
    {
        using var root = new TemporaryDirectory();
        using (var first = new AccountFactory(root.Path))
        {
            var statuses = first.Services.GetRequiredService<IVideoConversionJobStatusStore>();
            var entry = new ArchiveItemEntry("source", ArchiveCategory.Defaults[0], "/server-only/source.mkv", "source.mkv", ArchiveItemKind.File, ".mkv", 12, DateTime.UtcNow, true);
            var probe = new VideoConversionProbeResult("matroska", "vp9", "opus", null, 10, 10, TimeSpan.FromSeconds(20));
            statuses.Seed(new VideoConversionJob("running", entry, MediaAction.FullTranscode, probe));
            Assert.True(statuses.TryBeginProcessing("running"));
            statuses.Seed(new VideoConversionJob("done", entry, MediaAction.FullTranscode, probe));
            statuses.Fail("done", "ordinary failure");
            first.Services.GetRequiredService<ICompositionJobStatusStore>().Seed("queued");
        }

        using var restarted = new AccountFactory(root.Path);
        var conversions = restarted.Services.GetRequiredService<IVideoConversionJobStatusStore>();

        var running = conversions.Get("running")!;
        Assert.Equal(VideoConversionJobState.Failed, running.State);
        Assert.Equal("Interrupted by a restart.", running.Diagnostic);
        Assert.Equal("ordinary failure", conversions.Get("done")!.Diagnostic);
        var composition = Assert.Single(restarted.Services.GetRequiredService<ICompositionJobStatusStore>().GetAll());
        Assert.Equal(CompositionJobState.Failed, composition.State);
    }
}
