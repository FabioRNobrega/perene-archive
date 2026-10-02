using WebApp.Models;

namespace WebApp.Services;

internal interface ICompositionJobStatusStore
{
    /// <summary>Records a Pending job for <paramref name="userId"/>; <paramref name="payloadJson"/> is the path-free identity payload.</summary>
    void Seed(string jobId, string? userId = null, string? payloadJson = null);

    void MarkProcessing(string jobId);

    void MarkCompleted(string jobId, string resultVideoId);

    void MarkFailed(string jobId, string? diagnostic);

    IReadOnlyList<CompositionJobStatus> GetAll();
}
