using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Lists the durable composition, archive-move/trash and cut jobs a caller may see for the Jobs page. Conversions keep their own richer
/// list. Members see only their own jobs and Admins see all; titles come from the stored status or payload and never include a path.
/// </summary>
internal sealed class JobActivityService(AppDbContext db)
{
    internal const int MaxJobs = 500;
    internal const string RemovedAccount = "Removed account";

    public async Task<IReadOnlyList<JobSummaryDto>> ListAsync(string? userId, bool isAdmin, CancellationToken cancellationToken)
    {
        if (!isAdmin && userId is null) return [];
        var rows = await db.Jobs.AsNoTracking()
            .Where(job => job.Type != JobType.VideoConversion && (isAdmin || job.UserId == userId))
            .OrderByDescending(job => job.CreatedUtc).Take(MaxJobs)
            .Select(job => new { job.Id, job.Type, job.State, job.PayloadJson, job.StatusJson, job.CreatedUtc, job.StartedUtc, job.FinishedUtc, Name = job.User == null ? null : job.User.DisplayName })
            .ToListAsync(cancellationToken);

        return rows.Select(row => Map(row.Id, row.Type, row.State, row.PayloadJson, row.StatusJson, row.CreatedUtc, row.StartedUtc, row.FinishedUtc, row.Name ?? RemovedAccount)).ToList();
    }

    private static JobSummaryDto Map(string id, JobType type, string state, string? payload, string? statusJson, DateTimeOffset created, DateTimeOffset? started, DateTimeOffset? finished, string startedBy)
    {
        switch (type)
        {
            case JobType.Composition:
            {
                var status = Read<CompositionJobStatus>(statusJson);
                return new JobSummaryDto(id, "Composition", CompositionTitle(payload), state, status?.Diagnostic, null, null, created, started, finished, startedBy);
            }
            case JobType.ArchiveMutation:
            {
                var status = Read<ArchiveMutationJobStatus>(statusJson);
                var title = status?.Label ?? "Archive operation";
                return new JobSummaryDto(id, "ArchiveMutation", title, state, status?.Diagnostic, status?.ProcessedItems, status?.TotalItems, created, started, finished, startedBy);
            }
            default:
            {
                var status = Read<CutJobStatus>(statusJson);
                return new JobSummaryDto(id, "Cut", status?.Label ?? "Cut", state, status?.Diagnostic, null, null, created, started, finished, startedBy);
            }
        }
    }

    private static string CompositionTitle(string? payload)
    {
        try
        {
            if (!string.IsNullOrEmpty(payload) && JsonDocument.Parse(payload).RootElement.TryGetProperty("sources", out var sources) && sources.ValueKind == JsonValueKind.Array)
                return $"Composition of {sources.GetArrayLength()} clips";
        }
        catch (JsonException)
        {
        }

        return "Composition";
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, SqliteJobStore.JsonOptions); }
        catch (JsonException) { return null; }
    }
}
