using System.Collections.Concurrent;
using WebApp.Client.Models;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveMutationJobStatusStore : IArchiveMutationJobStatusStore
{
    private readonly ConcurrentDictionary<string, ArchiveMutationJobStatus> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    public void Seed(ArchiveMutationJob job)
    {
        _jobs[job.JobId] = new ArchiveMutationJobStatus(
            job.JobId, job.Kind, ArchiveMutationJobState.Pending, job.TotalItems, ProcessedItems: 0, job.Label);
        _order.Enqueue(job.JobId);
    }

    public void MarkProcessing(string jobId) =>
        UpdateIf(jobId,
            status => status.State == ArchiveMutationJobState.Pending,
            status => status with { State = ArchiveMutationJobState.Processing });

    public void ReportProgress(string jobId, int processedItems) =>
        UpdateIf(jobId,
            status => status.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing,
            status => status with
            {
                State = ArchiveMutationJobState.Processing,
                ProcessedItems = Math.Max(status.ProcessedItems, processedItems)
            });

    public void MarkCompleted(string jobId) =>
        UpdateIf(jobId,
            status => status.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing,
            status => status with { State = ArchiveMutationJobState.Completed, ProcessedItems = status.TotalItems });

    public void MarkFailed(string jobId, string? diagnostic) =>
        UpdateIf(jobId,
            status => status.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing,
            status => status with { State = ArchiveMutationJobState.Failed, Diagnostic = diagnostic });

    public ArchiveMutationJobStatus? Get(string jobId) => _jobs.GetValueOrDefault(jobId);

    public IReadOnlyList<ArchiveMutationJobStatus> GetAll() =>
        _order
            .Select(jobId => _jobs.TryGetValue(jobId, out var status) ? status : null)
            .Where(status => status is not null)
            .Select(status => status!)
            .ToList();

    private void UpdateIf(
        string jobId,
        Func<ArchiveMutationJobStatus, bool> predicate,
        Func<ArchiveMutationJobStatus, ArchiveMutationJobStatus> transform)
    {
        while (_jobs.TryGetValue(jobId, out var current))
        {
            if (!predicate(current) || _jobs.TryUpdate(jobId, transform(current), current))
            {
                return;
            }
        }
    }
}
