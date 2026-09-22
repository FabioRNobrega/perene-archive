using System.Threading.Channels;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveMutationJobQueue : IArchiveMutationJobQueue
{
    private const int Capacity = 64;

    private readonly Channel<ArchiveMutationJob> _channel = Channel.CreateBounded<ArchiveMutationJob>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait,
    });

    private int _activeCount;

    public bool TryEnqueue(ArchiveMutationJob job)
    {
        if (!_channel.Writer.TryWrite(job))
        {
            return false;
        }

        Interlocked.Increment(ref _activeCount);
        return true;
    }

    public async Task<ArchiveMutationJob> DequeueAsync(CancellationToken cancellationToken) =>
        await _channel.Reader.ReadAsync(cancellationToken);

    public void Complete() => Interlocked.Decrement(ref _activeCount);

    public int ActiveCount => Volatile.Read(ref _activeCount);
}
