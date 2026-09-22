using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveMutationJobQueue
{
    bool TryEnqueue(ArchiveMutationJob job);

    Task<ArchiveMutationJob> DequeueAsync(CancellationToken cancellationToken);

    void Complete();

    int ActiveCount { get; }
}
