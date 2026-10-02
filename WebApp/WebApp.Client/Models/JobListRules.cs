namespace WebApp.Client.Models;

public enum JobRowKind { Conversion, Composition, Move, Cut }

/// <summary>One line of the Jobs list: everything the collapsed row shows, plus the source DTO the details panel reads.</summary>
public sealed record JobRowModel(
    string Id,
    JobRowKind Kind,
    string Title,
    string State,
    bool IsActive,
    int? ProgressPercent,
    TimeSpan? TotalTime,
    string? FailureReason,
    DateTimeOffset SortAt,
    VideoConversionJobDto? Conversion,
    JobSummaryDto? Other)
{
    public string TypeLabel => JobListRules.TypeLabel(Kind);
    public string IconClass => JobListRules.IconClass(Kind);
    public string BadgeClass => JobListRules.BadgeClass(State);
}

/// <summary>Presentation rules for the Jobs list kept in C#: row mapping, ordering, filtering, paging and which actions apply.</summary>
public static class JobListRules
{
    public const int DefaultPageSize = 10;

    /// <summary>The page sizes the user can choose from.</summary>
    public static readonly IReadOnlyList<int> PageSizes = [5, 10, 25, 50];

    public static readonly IReadOnlyList<(string Key, string Label)> Filters =
        [("all", "All"), ("conversion", "Conversions"), ("composition", "Compositions"), ("move", "Moves & trash"), ("cut", "Cuts")];

    public static string TypeLabel(JobRowKind kind) => kind switch
    {
        JobRowKind.Conversion => "Conversion",
        JobRowKind.Composition => "Composition",
        JobRowKind.Move => "Move / trash",
        _ => "Cut"
    };

    public static string IconClass(JobRowKind kind) => kind switch
    {
        JobRowKind.Conversion => "bi-film",
        JobRowKind.Composition => "bi-collection-play",
        JobRowKind.Move => "bi-folder-symlink",
        _ => "bi-scissors"
    };

    public static string BadgeClass(string state) => state switch
    {
        "Completed" => "text-bg-success",
        "Failed" => "text-bg-danger",
        "Stopped" or "Skipped" => "text-bg-secondary",
        "Paused" => "text-bg-warning",
        "Finalizing" => "text-bg-info",
        _ => "text-bg-primary"
    };

    public static IReadOnlyList<JobRowModel> Build(IEnumerable<VideoConversionJobDto> conversions, IEnumerable<JobSummaryDto> others) =>
        conversions.Select(FromConversion).Concat(others.Select(FromOther)).ToList();

    public static JobRowModel FromConversion(VideoConversionJobDto job)
    {
        var active = IsConversionActive(job.State);
        return new JobRowModel(job.JobId, JobRowKind.Conversion, job.SourceName, job.State.ToString(), active, ConversionProgress(job),
            active ? null : TotalTime(job.QueuedAtUtc, job.StartedAtUtc, job.FinishedAtUtc),
            job.State == VideoConversionJobState.Failed ? job.Diagnostic ?? "Failed." : null,
            job.QueuedAtUtc ?? DateTimeOffset.MinValue, job, null);
    }

    public static JobRowModel FromOther(JobSummaryDto job)
    {
        var kind = job.Kind switch { "Composition" => JobRowKind.Composition, "ArchiveMutation" => JobRowKind.Move, _ => JobRowKind.Cut };
        var active = job.State is "Pending" or "Processing";
        int? percent = kind == JobRowKind.Move && active && job.TotalItems is > 0
            ? Math.Clamp((int)Math.Round((job.ProcessedItems ?? 0) * 100d / job.TotalItems.Value), 0, 100)
            : null;
        return new JobRowModel(job.JobId, kind, job.Title, job.State, active, percent,
            active ? null : TotalTime(job.CreatedUtc, job.StartedUtc, job.FinishedUtc),
            job.State == "Failed" ? job.Detail ?? "Failed." : null,
            job.CreatedUtc, null, job);
    }

    public static bool IsConversionActive(VideoConversionJobState state) =>
        state is VideoConversionJobState.Pending or VideoConversionJobState.Processing or VideoConversionJobState.Paused or VideoConversionJobState.Finalizing;

    public static int ConversionProgress(VideoConversionJobDto job) => (int)Math.Round(job.State switch
    {
        VideoConversionJobState.Completed => 100,
        VideoConversionJobState.Finalizing => 99,
        _ when job.SourceDurationSeconds is > 0 && job.ProcessedDurationSeconds is not null =>
            Math.Min(99, Math.Max(0, job.ProcessedDurationSeconds.Value / job.SourceDurationSeconds.Value * 100)),
        _ => 0
    }, MidpointRounding.AwayFromZero);

    /// <summary>Running time once finished: from when it started, or from when it was queued if it never started.</summary>
    public static TimeSpan? TotalTime(DateTimeOffset? queued, DateTimeOffset? started, DateTimeOffset? finished) =>
        finished is { } end && (started ?? queued) is { } begin && end >= begin ? end - begin : null;

    /// <summary>Running jobs first, then everything else, each group newest first.</summary>
    public static IReadOnlyList<JobRowModel> Order(IEnumerable<JobRowModel> rows) =>
        rows.OrderByDescending(row => row.IsActive).ThenByDescending(row => row.SortAt).ToList();

    public static bool Matches(JobRowModel row, string filter) => filter switch
    {
        "conversion" => row.Kind == JobRowKind.Conversion,
        "composition" => row.Kind == JobRowKind.Composition,
        "move" => row.Kind == JobRowKind.Move,
        "cut" => row.Kind == JobRowKind.Cut,
        _ => true
    };

    public static int CountFor(IEnumerable<JobRowModel> rows, string filter) => rows.Count(row => Matches(row, filter));

    public static int NormalizePageSize(int pageSize) => PageSizes.Contains(pageSize) ? pageSize : DefaultPageSize;

    public static int PageCount(int total, int pageSize = DefaultPageSize) =>
        Math.Max(1, (total + NormalizePageSize(pageSize) - 1) / NormalizePageSize(pageSize));

    public static int ClampPage(int page, int total, int pageSize = DefaultPageSize) => Math.Clamp(page, 1, PageCount(total, pageSize));

    public static IReadOnlyList<JobRowModel> Page(IReadOnlyList<JobRowModel> rows, int page, int pageSize = DefaultPageSize)
    {
        var size = NormalizePageSize(pageSize);
        return rows.Skip((ClampPage(page, rows.Count, size) - 1) * size).Take(size).ToList();
    }

    /// <summary>The page numbers to show: a window of up to five around the current page.</summary>
    public static IReadOnlyList<int> PageWindow(int page, int total, int pageSize = DefaultPageSize)
    {
        var count = PageCount(total, pageSize);
        var start = Math.Clamp(page - 2, 1, Math.Max(1, count - 4));
        return Enumerable.Range(start, Math.Min(5, count)).ToList();
    }

    public static bool CanPauseOrResume(JobRowModel row) => row.Conversion?.State is VideoConversionJobState.Processing or VideoConversionJobState.Paused;
    public static bool CanStop(JobRowModel row) => CanPauseOrResume(row);
    public static bool CanRetry(JobRowModel row) => row.Conversion?.State is VideoConversionJobState.Failed or VideoConversionJobState.Stopped;
}
