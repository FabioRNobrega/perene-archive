using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveMutationJobStatusStoreTests
{
    [Fact]
    public void Seeded_job_starts_pending_and_transitions_through_processing_to_completed()
    {
        var store = new ArchiveMutationJobStatusStore();
        store.Seed(CreateJob("job-1", totalItems: 3));

        var seeded = Assert.Single(store.GetAll());
        Assert.Equal(ArchiveMutationJobState.Pending, seeded.State);
        Assert.Equal(3, seeded.TotalItems);
        Assert.Equal(0, seeded.ProcessedItems);

        store.MarkProcessing("job-1");
        Assert.Equal(ArchiveMutationJobState.Processing, store.Get("job-1")!.State);

        store.ReportProgress("job-1", 2);
        Assert.Equal(2, store.Get("job-1")!.ProcessedItems);

        store.MarkCompleted("job-1");
        var completed = store.Get("job-1")!;
        Assert.Equal(ArchiveMutationJobState.Completed, completed.State);
        Assert.Equal(3, completed.ProcessedItems);
    }

    [Fact]
    public void Failed_job_records_diagnostic()
    {
        var store = new ArchiveMutationJobStatusStore();
        store.Seed(CreateJob("job-1", totalItems: 1));

        store.MarkFailed("job-1", "disk full");

        var failed = Assert.Single(store.GetAll());
        Assert.Equal(ArchiveMutationJobState.Failed, failed.State);
        Assert.Equal("disk full", failed.Diagnostic);
    }

    [Fact]
    public void Progress_after_a_terminal_state_does_not_regress_it()
    {
        var store = new ArchiveMutationJobStatusStore();
        store.Seed(CreateJob("job-1", totalItems: 5));
        store.MarkProcessing("job-1");
        store.MarkFailed("job-1", "boom");

        store.ReportProgress("job-1", 5);
        store.MarkCompleted("job-1");

        var status = store.Get("job-1")!;
        Assert.Equal(ArchiveMutationJobState.Failed, status.State);
        Assert.Equal("boom", status.Diagnostic);
    }

    [Fact]
    public void Jobs_are_returned_in_the_order_they_were_seeded()
    {
        var store = new ArchiveMutationJobStatusStore();
        store.Seed(CreateJob("first", totalItems: 1));
        store.Seed(CreateJob("second", totalItems: 1));
        store.Seed(CreateJob("third", totalItems: 1));

        Assert.Equal(["first", "second", "third"], store.GetAll().Select(status => status.JobId));
    }

    private static ArchiveMutationJob CreateJob(string jobId, int totalItems) =>
        new(jobId, ArchiveMutationKind.Move, "/videos/a.mp4", "/videos/target/a.mp4", IsFolder: false, totalItems, Label: "a.mp4");
}
