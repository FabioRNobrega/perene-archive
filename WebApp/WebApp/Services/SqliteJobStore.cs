using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Who may see a job in the polling lists: its owner, or any Admin.</summary>
internal interface IJobVisibility
{
    bool IsVisibleTo(string jobId, string? userId, bool isAdmin);
}

/// <summary>Durable bookkeeping for cut jobs, which have no polling endpoint but are attributed and recorded like the others.</summary>
internal interface ICutJobRecorder
{
    void Seed(CutJob job, string? payloadJson = null);
    void MarkProcessing(string jobId);
    void MarkCompleted(string jobId);
    void MarkFailed(string jobId, string? diagnostic = null);
}

/// <summary>What a cut row shows on the Jobs page: the source file name and A/B range (no path), its state, and a generic outcome.</summary>
internal sealed record CutJobStatus(string JobId, string Label, string State, string? Diagnostic = null);

/// <summary>
/// SQLite-backed implementation of the composition, archive-mutation and video-conversion status stores (plus cut recording and job
/// visibility). The stores are singletons and <see cref="AppDbContext"/> is scoped, so each call opens a short-lived scope; one
/// process-wide gate keeps read-modify-write transitions atomic. The persisted status is the same browser-safe record the in-memory
/// stores held, so endpoint and DTO shapes are unchanged.
/// </summary>
internal sealed class SqliteJobStore(IServiceScopeFactory scopes, int retainedTerminalJobsPerType = SqliteJobStore.DefaultRetainedTerminalJobsPerType) :
    ICompositionJobStatusStore, IArchiveMutationJobStatusStore, IVideoConversionJobStatusStore, ICutJobRecorder, IJobVisibility
{
    internal const int DefaultRetainedTerminalJobsPerType = 2000;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(750);
    internal static JsonSerializerOptions JsonOptions => Json;
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, long> _lastProgressTicks = new(StringComparer.Ordinal);

    // ---- composition ----------------------------------------------------------------------------------------------------

    void ICompositionJobStatusStore.Seed(string jobId, string? userId, string? payloadJson) =>
        Insert(JobType.Composition, jobId, userId, payloadJson, nameof(CompositionJobState.Pending), new CompositionJobStatus(jobId, CompositionJobState.Pending));

    void ICompositionJobStatusStore.MarkProcessing(string jobId) =>
        Mutate<CompositionJobStatus>(JobType.Composition, jobId, s => s with { State = CompositionJobState.Processing }, s => s.State.ToString());

    void ICompositionJobStatusStore.MarkCompleted(string jobId, string resultVideoId) =>
        Mutate<CompositionJobStatus>(JobType.Composition, jobId, s => s with { State = CompositionJobState.Completed, ResultVideoId = resultVideoId }, s => s.State.ToString());

    void ICompositionJobStatusStore.MarkFailed(string jobId, string? diagnostic) =>
        Mutate<CompositionJobStatus>(JobType.Composition, jobId, s => s with { State = CompositionJobState.Failed, Diagnostic = diagnostic }, s => s.State.ToString());

    IReadOnlyList<CompositionJobStatus> ICompositionJobStatusStore.GetAll() => ReadAll<CompositionJobStatus>(JobType.Composition);

    // ---- archive mutation -----------------------------------------------------------------------------------------------

    void IArchiveMutationJobStatusStore.Seed(ArchiveMutationJob job, string? payloadJson) =>
        Insert(JobType.ArchiveMutation, job.JobId, job.ActorUserId, payloadJson, nameof(ArchiveMutationJobState.Pending),
            new ArchiveMutationJobStatus(job.JobId, job.Kind, ArchiveMutationJobState.Pending, job.TotalItems, ProcessedItems: 0, job.Label));

    void IArchiveMutationJobStatusStore.MarkProcessing(string jobId) =>
        Mutate<ArchiveMutationJobStatus>(JobType.ArchiveMutation, jobId,
            s => s.State == ArchiveMutationJobState.Pending ? s with { State = ArchiveMutationJobState.Processing } : null, s => s.State.ToString());

    void IArchiveMutationJobStatusStore.ReportProgress(string jobId, int processedItems)
    {
        if (!ShouldWriteProgress(jobId, force: false)) return;
        Mutate<ArchiveMutationJobStatus>(JobType.ArchiveMutation, jobId,
            s => s.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing
                ? s with { State = ArchiveMutationJobState.Processing, ProcessedItems = Math.Max(s.ProcessedItems, processedItems) }
                : null,
            s => s.State.ToString());
    }

    void IArchiveMutationJobStatusStore.MarkCompleted(string jobId)
    {
        _lastProgressTicks.TryRemove(jobId, out _);
        Mutate<ArchiveMutationJobStatus>(JobType.ArchiveMutation, jobId,
            s => s.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing
                ? s with { State = ArchiveMutationJobState.Completed, ProcessedItems = s.TotalItems }
                : null,
            s => s.State.ToString());
    }

    void IArchiveMutationJobStatusStore.MarkFailed(string jobId, string? diagnostic)
    {
        _lastProgressTicks.TryRemove(jobId, out _);
        Mutate<ArchiveMutationJobStatus>(JobType.ArchiveMutation, jobId,
            s => s.State is ArchiveMutationJobState.Pending or ArchiveMutationJobState.Processing
                ? s with { State = ArchiveMutationJobState.Failed, Diagnostic = diagnostic }
                : null,
            s => s.State.ToString());
    }

    ArchiveMutationJobStatus? IArchiveMutationJobStatusStore.Get(string jobId) => ReadOne<ArchiveMutationJobStatus>(JobType.ArchiveMutation, jobId);

    IReadOnlyList<ArchiveMutationJobStatus> IArchiveMutationJobStatusStore.GetAll() => ReadAll<ArchiveMutationJobStatus>(JobType.ArchiveMutation);

    // ---- video conversion -----------------------------------------------------------------------------------------------

    private static readonly VideoConversionJobState[] ConversionActive =
        [VideoConversionJobState.Pending, VideoConversionJobState.Processing, VideoConversionJobState.Paused, VideoConversionJobState.Finalizing];

    bool IVideoConversionJobStatusStore.HasActiveSource(string sourceId) =>
        ReadAll<VideoConversionStatus>(JobType.VideoConversion).Any(x => x.SourceId == sourceId && ConversionActive.Contains(x.State));

    void IVideoConversionJobStatusStore.Seed(VideoConversionJob j, string? payloadJson) =>
        Insert(JobType.VideoConversion, j.JobId, j.ActorUserId, payloadJson, nameof(VideoConversionJobState.Pending),
            new VideoConversionStatus(j.JobId, j.Source.Id, j.Source.Name, j.Action, VideoConversionJobState.Pending, j.Source.SizeBytes,
                QueuedAtUtc: DateTimeOffset.UtcNow, SourceDurationSeconds: j.Probe.Duration.TotalSeconds, ProfileLabel: j.Profile?.Label,
                OutputHeight: j.Profile?.OutputHeight, EstimatedSizeBytes: j.Profile?.EstimatedSizeBytes));

    /// <summary>Resets a Failed or Stopped job to a fresh Pending state under the same id, so a retry reuses the same card and row.</summary>
    bool IVideoConversionJobStatusStore.Restart(VideoConversionJob j, string? payloadJson)
    {
        _lastProgressTicks.TryRemove(j.JobId, out _);
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = db.Jobs.FirstOrDefault(job => job.Id == j.JobId && job.Type == JobType.VideoConversion);
            var current = row is null ? null : Deserialize<VideoConversionStatus>(row);
            if (row is null || current is null || current.State is not (VideoConversionJobState.Failed or VideoConversionJobState.Stopped)) return false;
            row.UserId = j.ActorUserId;
            row.PayloadJson = payloadJson;
            row.StartedUtc = null; // a retry is a fresh run
            Save(db, row, new VideoConversionStatus(j.JobId, j.Source.Id, j.Source.Name, j.Action, VideoConversionJobState.Pending, j.Source.SizeBytes,
                QueuedAtUtc: DateTimeOffset.UtcNow, SourceDurationSeconds: j.Probe.Duration.TotalSeconds, ProfileLabel: j.Profile?.Label,
                OutputHeight: j.Profile?.OutputHeight, EstimatedSizeBytes: j.Profile?.EstimatedSizeBytes), s => s.State.ToString());
            return true;
        }
    }

    void IVideoConversionJobStatusStore.Remove(string id)
    {
        _lastProgressTicks.TryRemove(id, out _);
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Jobs.Where(job => job.Id == id && job.Type == JobType.VideoConversion).ExecuteDelete();
        }
    }

    void IVideoConversionJobStatusStore.Processing(string id) =>
        Transition(id, [VideoConversionJobState.Pending], x => x with { State = VideoConversionJobState.Processing, StartedAtUtc = DateTimeOffset.UtcNow });

    bool IVideoConversionJobStatusStore.TryBeginProcessing(string id) =>
        Transition(id, [VideoConversionJobState.Pending], x => x with { State = VideoConversionJobState.Processing, StartedAtUtc = DateTimeOffset.UtcNow }, requireIdleSlot: true);

    bool IVideoConversionJobStatusStore.Pause(string id) =>
        Transition(id, [VideoConversionJobState.Processing], x => x with { State = VideoConversionJobState.Paused });

    bool IVideoConversionJobStatusStore.Resume(string id) =>
        Transition(id, [VideoConversionJobState.Paused], x => x with { State = VideoConversionJobState.Processing }, requireIdleSlot: true);

    bool IVideoConversionJobStatusStore.Stop(string id) =>
        Transition(id, [VideoConversionJobState.Processing, VideoConversionJobState.Paused], x => x with
        {
            State = VideoConversionJobState.Stopped,
            Diagnostic = "Stopped. Temporary conversion output was deleted; the original source was not changed.",
            FinishedAtUtc = DateTimeOffset.UtcNow
        });

    void IVideoConversionJobStatusStore.Progress(string id, VideoConversionProgress progress)
    {
        if (!ShouldWriteProgress(id, force: progress.IsFinalizing)) return;
        UpdateConversionIf(id, x => x.State is VideoConversionJobState.Processing or VideoConversionJobState.Finalizing, x => progress.IsFinalizing
            ? x with { State = VideoConversionJobState.Finalizing }
            : x with
            {
                State = VideoConversionJobState.Processing,
                ProcessedDurationSeconds = Math.Max(x.ProcessedDurationSeconds ?? 0, progress.ProcessedDurationSeconds ?? 0),
                Speed = progress.Speed ?? x.Speed
            });
    }

    void IVideoConversionJobStatusStore.Complete(string id, string output, long size) =>
        UpdateConversionIf(id, x => x.State is VideoConversionJobState.Processing or VideoConversionJobState.Finalizing, x => x with
        {
            State = VideoConversionJobState.Completed,
            OutputItemId = output,
            OutputSizeBytes = size,
            ProcessedDurationSeconds = x.SourceDurationSeconds,
            FinishedAtUtc = DateTimeOffset.UtcNow
        });

    void IVideoConversionJobStatusStore.Fail(string id, string msg) =>
        UpdateConversionIf(id, x => x.State is not VideoConversionJobState.Stopped, x => x with { State = VideoConversionJobState.Failed, Diagnostic = msg, FinishedAtUtc = DateTimeOffset.UtcNow });

    void IVideoConversionJobStatusStore.Skip(string id, string msg) =>
        UpdateConversionIf(id, x => x.State is not VideoConversionJobState.Stopped, x => x with { State = VideoConversionJobState.Skipped, Diagnostic = msg, FinishedAtUtc = DateTimeOffset.UtcNow });

    VideoConversionStatus? IVideoConversionJobStatusStore.Get(string id) => ReadOne<VideoConversionStatus>(JobType.VideoConversion, id);

    IReadOnlyList<VideoConversionStatus> IVideoConversionJobStatusStore.GetAll() => ReadAll<VideoConversionStatus>(JobType.VideoConversion);

    private bool Transition(string id, VideoConversionJobState[] expected, Func<VideoConversionStatus, VideoConversionStatus> change, bool requireIdleSlot = false)
    {
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (requireIdleSlot)
            {
                // Only one conversion may run at a time; a different job that is Processing or Finalizing holds the slot.
                var running = new[] { nameof(VideoConversionJobState.Processing), nameof(VideoConversionJobState.Finalizing) };
                if (db.Jobs.Any(job => job.Type == JobType.VideoConversion && job.Id != id && running.Contains(job.State))) return false;
            }

            var row = db.Jobs.FirstOrDefault(job => job.Id == id && job.Type == JobType.VideoConversion);
            if (row?.StatusJson is null) return false;
            var current = Deserialize<VideoConversionStatus>(row);
            if (current is null || !expected.Contains(current.State)) return false;
            Save(db, row, change(current), s => s.State.ToString());
            return true;
        }
    }

    private void UpdateConversionIf(string id, Func<VideoConversionStatus, bool> predicate, Func<VideoConversionStatus, VideoConversionStatus> change) =>
        Mutate<VideoConversionStatus>(JobType.VideoConversion, id, s => predicate(s) ? change(s) : null, s => s.State.ToString());

    // ---- cut recording --------------------------------------------------------------------------------------------------

    void ICutJobRecorder.Seed(CutJob job, string? payloadJson) =>
        Insert(JobType.Cut, job.JobId, job.ActorUserId, payloadJson, "Pending", new CutJobStatus(job.JobId, CutLabel(job), "Pending"));

    void ICutJobRecorder.MarkProcessing(string jobId) => SetCutState(jobId, "Processing");
    void ICutJobRecorder.MarkCompleted(string jobId) => SetCutState(jobId, "Completed");
    void ICutJobRecorder.MarkFailed(string jobId, string? diagnostic) => SetCutState(jobId, "Failed", diagnostic);

    private static string CutLabel(CutJob job) =>
        $"{job.SourceEntry?.Name ?? "Cut"} ({FormatClock(job.Start)}–{FormatClock(job.End)})";

    private static string FormatClock(TimeSpan value) =>
        value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes}:{value.Seconds:00}";

    private void SetCutState(string jobId, string state, string? diagnostic = null)
    {
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = db.Jobs.FirstOrDefault(job => job.Id == jobId && job.Type == JobType.Cut);
            if (row is null || IsTerminal(JobType.Cut, row.State)) return;
            row.State = state;
            if (Deserialize<CutJobStatus>(row) is { } status) row.StatusJson = Serialize(status with { State = state, Diagnostic = diagnostic ?? status.Diagnostic });
            row.UpdatedUtc = DateTimeOffset.UtcNow;
            if (state == "Processing") row.StartedUtc ??= row.UpdatedUtc;
            if (IsTerminal(JobType.Cut, state)) row.FinishedUtc = row.UpdatedUtc;
            db.SaveChanges();
        }
    }

    // ---- visibility -----------------------------------------------------------------------------------------------------

    bool IJobVisibility.IsVisibleTo(string jobId, string? userId, bool isAdmin)
    {
        if (isAdmin) return true;
        if (userId is null) return false;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Jobs.AsNoTracking().Any(job => job.Id == jobId && job.UserId == userId);
    }

    // ---- shared plumbing ------------------------------------------------------------------------------------------------

    internal static bool IsTerminal(JobType type, string state) => TerminalStates(type).Contains(state);

    internal static IReadOnlyList<string> TerminalStates(JobType type) => type switch
    {
        JobType.VideoConversion => [nameof(VideoConversionJobState.Completed), nameof(VideoConversionJobState.Failed), nameof(VideoConversionJobState.Skipped), nameof(VideoConversionJobState.Stopped)],
        _ => ["Completed", "Failed"]
    };

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    private static T? Deserialize<T>(Job row) where T : class => row.StatusJson is null ? null : JsonSerializer.Deserialize<T>(row.StatusJson, Json);

    private bool ShouldWriteProgress(string jobId, bool force)
    {
        var now = Environment.TickCount64;
        if (!force && _lastProgressTicks.TryGetValue(jobId, out var last) && now - last < ProgressInterval.TotalMilliseconds) return false;
        _lastProgressTicks[jobId] = now;
        return true;
    }

    private void Insert<TStatus>(JobType type, string jobId, string? userId, string? payloadJson, string state, TStatus? status) where TStatus : class?
    {
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.Jobs.Add(new Job
            {
                Id = jobId,
                Type = type,
                State = state,
                UserId = userId,
                PayloadJson = payloadJson,
                StatusJson = status is null ? null : Serialize(status),
                CreatedUtc = now,
                UpdatedUtc = now
            });
            db.SaveChanges();

            var terminal = TerminalStates(type);
            var stale = db.Jobs.Where(job => job.Type == type && terminal.Contains(job.State))
                .OrderByDescending(job => job.CreatedUtc).Skip(retainedTerminalJobsPerType).Select(job => job.Id).ToList();
            if (stale.Count > 0) db.Jobs.Where(job => stale.Contains(job.Id)).ExecuteDelete();
        }
    }

    /// <summary>Applies <paramref name="change"/> to the stored status; a null result means "no change".</summary>
    private void Mutate<TStatus>(JobType type, string jobId, Func<TStatus, TStatus?> change, Func<TStatus, string> stateOf) where TStatus : class
    {
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = db.Jobs.FirstOrDefault(job => job.Id == jobId && job.Type == type);
            if (row is null) return;
            var current = Deserialize<TStatus>(row);
            if (current is null) return;
            var next = change(current);
            if (next is null) return;
            Save(db, row, next, stateOf);
        }
    }

    private static void Save<TStatus>(AppDbContext db, Job row, TStatus status, Func<TStatus, string> stateOf) where TStatus : class
    {
        row.State = stateOf(status);
        row.StatusJson = Serialize(status);
        row.UpdatedUtc = DateTimeOffset.UtcNow;
        if (row.StartedUtc is null && row.State is "Processing" or "Paused" or "Finalizing") row.StartedUtc = row.UpdatedUtc;
        row.FinishedUtc = IsTerminal(row.Type, row.State) ? row.UpdatedUtc : null;
        db.SaveChanges();
    }

    private TStatus? ReadOne<TStatus>(JobType type, string jobId) where TStatus : class
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.Jobs.AsNoTracking().FirstOrDefault(job => job.Id == jobId && job.Type == type);
        return row is null ? null : Deserialize<TStatus>(row);
    }

    private List<TStatus> ReadAll<TStatus>(JobType type) where TStatus : class
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Jobs.AsNoTracking().Where(job => job.Type == type && job.StatusJson != null)
            .OrderBy(job => job.CreatedUtc).Select(job => job.StatusJson!).AsEnumerable()
            .Select(json => JsonSerializer.Deserialize<TStatus>(json, Json)).Where(status => status is not null).Select(status => status!).ToList();
    }

    /// <summary>
    /// Marks every non-terminal job from a previous process as failed-by-restart. Jobs are never replayed: the queue is empty after a
    /// restart, so leaving them Pending/Processing would show work that will never run.
    /// </summary>
    internal static async Task<int> MarkInterruptedAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        const string Reason = "Interrupted by a restart.";
        var rows = await db.Jobs.Where(job => job.State != "Completed" && job.State != "Failed" && job.State != "Skipped" && job.State != "Stopped").ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            if (IsTerminal(row.Type, row.State)) continue;
            switch (row.Type)
            {
                case JobType.Composition when Deserialize<CompositionJobStatus>(row) is { } c:
                    row.StatusJson = Serialize(c with { State = CompositionJobState.Failed, Diagnostic = Reason });
                    break;
                case JobType.ArchiveMutation when Deserialize<ArchiveMutationJobStatus>(row) is { } m:
                    row.StatusJson = Serialize(m with { State = ArchiveMutationJobState.Failed, Diagnostic = Reason });
                    break;
                case JobType.Cut when Deserialize<CutJobStatus>(row) is { } cut:
                    row.StatusJson = Serialize(cut with { State = "Failed", Diagnostic = Reason });
                    break;
                case JobType.VideoConversion when Deserialize<VideoConversionStatus>(row) is { } v:
                    row.StatusJson = Serialize(v with { State = VideoConversionJobState.Failed, Diagnostic = Reason, FinishedAtUtc = now });
                    break;
            }

            row.State = "Failed";
            row.UpdatedUtc = now;
            row.FinishedUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
