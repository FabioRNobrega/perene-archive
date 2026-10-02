using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class JobListRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    private static VideoConversionJobDto Conversion(string id, VideoConversionJobState state, int queuedMinutes = 0, string? diagnostic = null, double? processed = null, double? duration = null) =>
        new(id, id + ".mkv", "FullTranscode", state, 10, Diagnostic: diagnostic, QueuedAtUtc: T0.AddMinutes(queuedMinutes),
            StartedAtUtc: T0.AddMinutes(queuedMinutes), SourceDurationSeconds: duration, ProcessedDurationSeconds: processed,
            FinishedAtUtc: JobListRules.IsConversionActive(state) ? null : T0.AddMinutes(queuedMinutes + 3));

    private static JobSummaryDto Other(string id, string kind, string state, int createdMinutes = 0, string? detail = null, int? processed = null, int? total = null) =>
        new(id, kind, id, state, detail, processed, total, T0.AddMinutes(createdMinutes), null, state is "Completed" or "Failed" ? T0.AddMinutes(createdMinutes + 2) : null, "Someone");

    [Fact]
    public void Rows_get_a_type_label_icon_and_badge_per_kind_and_state()
    {
        var rows = JobListRules.Build([Conversion("c", VideoConversionJobState.Completed)], [Other("m", "Composition", "Completed"), Other("mv", "ArchiveMutation", "Failed"), Other("k", "Cut", "Pending")]);

        Assert.Equal(["Conversion", "Composition", "Move / trash", "Cut"], rows.Select(row => row.TypeLabel));
        Assert.Equal(["bi-film", "bi-collection-play", "bi-folder-symlink", "bi-scissors"], rows.Select(row => row.IconClass));
        Assert.Equal(["text-bg-success", "text-bg-success", "text-bg-danger", "text-bg-primary"], rows.Select(row => row.BadgeClass));
        Assert.Equal("text-bg-warning", JobListRules.BadgeClass("Paused"));
        Assert.Equal("text-bg-secondary", JobListRules.BadgeClass("Stopped"));
    }

    [Fact]
    public void Running_jobs_come_first_then_newest_first()
    {
        var rows = JobListRules.Order(JobListRules.Build(
            [Conversion("old-done", VideoConversionJobState.Completed, 1), Conversion("running", VideoConversionJobState.Processing, 2), Conversion("paused", VideoConversionJobState.Paused, 0)],
            [Other("new-done", "Cut", "Completed", 10), Other("queued", "Composition", "Pending", 5)]));

        Assert.Equal(["queued", "running", "paused", "new-done", "old-done"], rows.Select(row => row.Id));
    }

    [Fact]
    public void Active_rows_show_progress_finished_rows_show_total_time_and_failed_rows_show_the_reason()
    {
        var running = JobListRules.FromConversion(Conversion("r", VideoConversionJobState.Processing, processed: 30, duration: 120));
        var done = JobListRules.FromConversion(Conversion("d", VideoConversionJobState.Completed));
        var failed = JobListRules.FromConversion(Conversion("f", VideoConversionJobState.Failed, diagnostic: "Interrupted by a restart."));
        var move = JobListRules.FromOther(Other("mv", "ArchiveMutation", "Processing", processed: 1, total: 4));
        var cut = JobListRules.FromOther(Other("k", "Cut", "Processing"));

        Assert.Equal(25, running.ProgressPercent);
        Assert.Null(running.TotalTime);
        Assert.Equal(TimeSpan.FromMinutes(3), done.TotalTime);
        Assert.Null(done.FailureReason);
        Assert.Equal("Interrupted by a restart.", failed.FailureReason);
        Assert.Equal(25, move.ProgressPercent);
        Assert.Null(cut.ProgressPercent); // active but no measurable progress: the row shows an indeterminate bar
        Assert.True(cut.IsActive);
        Assert.Null(JobListRules.FromConversion(Conversion("s", VideoConversionJobState.Stopped, diagnostic: "Stopped.")).FailureReason);
    }

    [Fact]
    public void Total_time_runs_from_start_or_from_queue_when_it_never_started()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), JobListRules.TotalTime(T0, T0.AddMinutes(1), T0.AddMinutes(3)));
        Assert.Equal(TimeSpan.FromMinutes(3), JobListRules.TotalTime(T0, null, T0.AddMinutes(3)));
        Assert.Null(JobListRules.TotalTime(T0, T0, null));
        Assert.Null(JobListRules.TotalTime(null, null, T0));
        Assert.Null(JobListRules.TotalTime(T0, T0.AddMinutes(5), T0.AddMinutes(1)));
    }

    [Fact]
    public void Filters_match_their_type_and_counts_follow()
    {
        var rows = JobListRules.Build([Conversion("c1", VideoConversionJobState.Failed), Conversion("c2", VideoConversionJobState.Failed)],
            [Other("comp", "Composition", "Completed"), Other("mv", "ArchiveMutation", "Completed"), Other("cut", "Cut", "Completed")]);

        Assert.Equal(5, JobListRules.CountFor(rows, "all"));
        Assert.Equal(2, JobListRules.CountFor(rows, "conversion"));
        Assert.Equal(1, JobListRules.CountFor(rows, "composition"));
        Assert.Equal(1, JobListRules.CountFor(rows, "move"));
        Assert.Equal(1, JobListRules.CountFor(rows, "cut"));
        Assert.Equal(["All", "Conversions", "Compositions", "Moves & trash", "Cuts"], JobListRules.Filters.Select(filter => filter.Label));
    }

    [Fact]
    public void Paging_defaults_to_ten_per_page_clamps_and_windows_the_page_numbers()
    {
        var rows = JobListRules.Build([], Enumerable.Range(0, 23).Select(i => Other("j" + i, "Cut", "Completed", i)));

        Assert.Equal(3, JobListRules.PageCount(rows.Count));
        Assert.Equal(1, JobListRules.PageCount(0));
        Assert.Equal(10, JobListRules.Page(rows, 1).Count);
        Assert.Equal(3, JobListRules.Page(rows, 3).Count);
        Assert.Equal(3, JobListRules.Page(rows, 99).Count); // clamped to the last page
        Assert.Equal(1, JobListRules.ClampPage(0, 23));
        Assert.Equal([1, 2, 3], JobListRules.PageWindow(1, 23));
        Assert.Equal([3, 4, 5, 6, 7], JobListRules.PageWindow(5, 100));
        Assert.Equal([6, 7, 8, 9, 10], JobListRules.PageWindow(10, 100));
    }

    [Theory]
    [InlineData(5, 5, 5)]
    [InlineData(10, 10, 3)]
    [InlineData(25, 23, 1)]
    [InlineData(50, 23, 1)]
    public void The_chosen_page_size_controls_how_many_rows_and_pages_there_are(int pageSize, int firstPageRows, int pages)
    {
        var rows = JobListRules.Build([], Enumerable.Range(0, 23).Select(i => Other("j" + i, "Cut", "Completed", i)));

        Assert.Equal(firstPageRows, JobListRules.Page(rows, 1, pageSize).Count);
        Assert.Equal(pages, JobListRules.PageCount(rows.Count, pageSize));
        Assert.Equal(23 - (pages - 1) * pageSize, JobListRules.Page(rows, pages, pageSize).Count);
        Assert.Equal(Enumerable.Range(1, Math.Min(5, pages)), JobListRules.PageWindow(1, rows.Count, pageSize));
    }

    [Fact]
    public void Only_the_offered_page_sizes_are_accepted_and_anything_else_falls_back_to_the_default()
    {
        Assert.Equal([5, 10, 25, 50], JobListRules.PageSizes);
        Assert.Equal(25, JobListRules.NormalizePageSize(25));
        Assert.Equal(JobListRules.DefaultPageSize, JobListRules.NormalizePageSize(7));
        Assert.Equal(JobListRules.DefaultPageSize, JobListRules.NormalizePageSize(0));
        Assert.Equal(10, JobListRules.Page(JobListRules.Build([], Enumerable.Range(0, 30).Select(i => Other("j" + i, "Cut", "Completed", i))), 1, 999).Count);
    }

    [Fact]
    public void Actions_apply_only_to_conversions_in_the_right_state()
    {
        JobRowModel Row(VideoConversionJobState state) => JobListRules.FromConversion(Conversion("c", state));

        Assert.True(JobListRules.CanPauseOrResume(Row(VideoConversionJobState.Processing)));
        Assert.True(JobListRules.CanStop(Row(VideoConversionJobState.Paused)));
        Assert.False(JobListRules.CanStop(Row(VideoConversionJobState.Pending)));
        Assert.True(JobListRules.CanRetry(Row(VideoConversionJobState.Failed)));
        Assert.True(JobListRules.CanRetry(Row(VideoConversionJobState.Stopped)));
        Assert.False(JobListRules.CanRetry(Row(VideoConversionJobState.Completed)));
        var cut = JobListRules.FromOther(Other("k", "Cut", "Failed"));
        Assert.False(JobListRules.CanRetry(cut));
        Assert.False(JobListRules.CanStop(cut));
    }
}
