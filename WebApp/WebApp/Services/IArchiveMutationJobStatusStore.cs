using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveMutationJobStatusStore
{
    void Seed(ArchiveMutationJob job);

    void MarkProcessing(string jobId);

    void ReportProgress(string jobId, int processedItems);

    void MarkCompleted(string jobId);

    void MarkFailed(string jobId, string? diagnostic);

    ArchiveMutationJobStatus? Get(string jobId);

    IReadOnlyList<ArchiveMutationJobStatus> GetAll();
}
