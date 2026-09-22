using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveMutationJobQueueTests
{
    [Fact]
    public async Task Queue_preserves_fifo_order()
    {
        var queue = new ArchiveMutationJobQueue();
        var first = CreateJob("first");
        var second = CreateJob("second");

        Assert.True(queue.TryEnqueue(first));
        Assert.True(queue.TryEnqueue(second));

        Assert.Equal(first, await queue.DequeueAsync(CancellationToken.None));
        Assert.Equal(second, await queue.DequeueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ActiveCount_increments_on_enqueue_and_decrements_on_complete()
    {
        var queue = new ArchiveMutationJobQueue();
        Assert.Equal(0, queue.ActiveCount);

        Assert.True(queue.TryEnqueue(CreateJob("job")));
        Assert.Equal(1, queue.ActiveCount);

        await queue.DequeueAsync(CancellationToken.None);
        queue.Complete();
        Assert.Equal(0, queue.ActiveCount);
    }

    private static ArchiveMutationJob CreateJob(string jobId) =>
        new(jobId, ArchiveMutationKind.Move, "/videos/a.mp4", "/videos/target/a.mp4", IsFolder: false, TotalItems: 1, Label: "a.mp4");
}
