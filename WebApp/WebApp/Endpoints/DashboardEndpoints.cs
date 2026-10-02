using WebApp.Authorization;
using WebApp.Client.Models;
using Microsoft.EntityFrameworkCore;
using WebApp.Services;

namespace WebApp.Endpoints;

internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/system", GetSystem);
        endpoints.MapGet("/api/dashboard/memory", GetMemory);
        endpoints.MapGet("/api/dashboard/storage", GetStorage);
        endpoints.MapGet("/api/dashboard/storage/custom", GetCustomStorageViews);
        endpoints.MapPost("/api/dashboard/storage/custom", AddCustomStorageView);
        endpoints.MapDelete("/api/dashboard/storage/custom/{viewId}", RemoveCustomStorageView);
        endpoints.MapPatch("/api/dashboard/storage/custom/{viewId}", UpdateCustomStorageView);
        endpoints.MapGet("/api/dashboard/network", GetNetwork);
        endpoints.MapGet("/api/dashboard/archive", GetArchive);
        endpoints.MapGet("/api/dashboard/docker", GetDockerAsync);
        endpoints.MapGet("/api/dashboard/health", GetHealth);
        endpoints.MapGet("/api/dashboard/history", GetHistory);
        endpoints.MapGet("/api/dashboard/alerts", GetAlerts);
        endpoints.MapGet("/api/dashboard/jobs", GetJobs);
        endpoints.MapGet("/api/dashboard/jobs/activity", GetActivityAsync);
        endpoints.MapPost("/api/dashboard/jobs/{id}/pause", Pause);
        endpoints.MapPost("/api/dashboard/jobs/{id}/resume", Resume);
        endpoints.MapPost("/api/dashboard/jobs/{id}/stop", Stop);
        endpoints.MapPost("/api/dashboard/jobs/{id}/retry", RetryAsync);
        return endpoints;
    }

    private static IResult GetSystem(ISystemMetricsService systemMetricsService) =>
        Results.Ok(systemMetricsService.GetSystemMetrics());

    private static IResult GetMemory(ISystemMetricsService systemMetricsService) =>
        Results.Ok(systemMetricsService.GetMemoryMetrics());

    private static IResult GetStorage(IStorageUsageService storageUsageService)
    {
        var usage = storageUsageService.GetUsage();
        var throughput = storageUsageService.GetThroughput();
        var usedPercent = usage.TotalBytes > 0 ? usage.UsedBytes * 100.0 / usage.TotalBytes : (double?)null;
        var health = usage.TotalBytes > 0 ? DashboardThresholds.Evaluate(usedPercent) : DashboardHealthStatus.Unavailable;

        return Results.Ok(new DashboardStorageDto(
            usage.TotalBytes > 0,
            usage.UsedBytes,
            usage.TotalBytes,
            throughput.ReadBytesPerSecond,
            throughput.WriteBytesPerSecond,
            health));
    }

    // Custom storage views name folders and report their sizes, so they are an Admin tool; a member sees an empty list.
    private static async Task<IResult> GetCustomStorageViews(HttpContext http, ICustomStorageViewService customStorageViews, CancellationToken cancellationToken) =>
        await ArchiveListingAccess.IsAdminAsync(http)
            ? Results.Ok(await customStorageViews.GetAllAsync(cancellationToken))
            : Results.Ok(Array.Empty<CustomStorageViewDto>());

    private static async Task<IResult> AddCustomStorageView(
        HttpContext http, AddCustomStorageViewRequest request, ICustomStorageViewService customStorageViews, CancellationToken cancellationToken)
    {
        if (!await ArchiveListingAccess.IsAdminAsync(http))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.MaxSizeBytes <= 0)
        {
            return Results.BadRequest(new { error = "A positive max size is required." });
        }

        if (!request.IsWholeArchive && string.IsNullOrWhiteSpace(request.CategoryKey))
        {
            return Results.BadRequest(new { error = "A category is required." });
        }

        try
        {
            var views = await customStorageViews.AddAsync(
                request.CategoryKey, request.FolderId, request.IsWholeArchive, request.MaxSizeBytes, cancellationToken);
            return Results.Ok(views);
        }
        catch (ArchiveValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArchiveForbiddenException exception)
        {
            return Results.Problem(title: "This folder cannot be tracked.", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> RemoveCustomStorageView(
        HttpContext http, string viewId, ICustomStorageViewService customStorageViews, CancellationToken cancellationToken)
    {
        if (!await ArchiveListingAccess.IsAdminAsync(http))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var views = await customStorageViews.RemoveAsync(viewId, cancellationToken);
        return views is null ? Results.NotFound() : Results.Ok(views);
    }

    private static async Task<IResult> UpdateCustomStorageView(
        HttpContext http, string viewId, UpdateCustomStorageViewMaxSizeRequest request, ICustomStorageViewService customStorageViews, CancellationToken cancellationToken)
    {
        if (!await ArchiveListingAccess.IsAdminAsync(http))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (request.MaxSizeBytes <= 0)
        {
            return Results.BadRequest(new { error = "A positive max size is required." });
        }

        var views = await customStorageViews.UpdateMaxSizeAsync(viewId, request.MaxSizeBytes, cancellationToken);
        return views is null ? Results.NotFound() : Results.Ok(views);
    }

    private static IResult GetNetwork(INetworkMetricsService networkMetricsService) =>
        Results.Ok(networkMetricsService.GetNetworkMetrics());

    // File counts only include folders the caller may read.
    private static async Task<IResult> GetArchive(HttpContext http, IArchiveMetricsService archiveMetricsService) =>
        Results.Ok(archiveMetricsService.GetArchiveMetrics(await ArchiveListingAccess.CanEnterAsync(http)));

    // Conversion jobs name their source file, so a member only sees and controls the jobs they started; Admins see all.
    private static async Task<IResult> GetJobs(HttpContext http, IVideoConversionJobStatusStore statuses, IJobVisibility owners, WebApp.Data.AppDbContext db)
    {
        var isAdmin = await ArchiveListingAccess.IsAdminAsync(http);
        var userId = ArchiveListingAccess.UserId(http);
        var visible = statuses.GetAll().Where(status => owners.IsVisibleTo(status.JobId, userId, isAdmin)).ToList();
        var ids = visible.Select(status => status.JobId).ToList();
        // Display names only; the owner's account ID never leaves the server. A deleted account leaves the name empty.
        var names = (await db.Jobs.AsNoTracking().Where(job => ids.Contains(job.Id)).Select(job => new { job.Id, Name = job.User == null ? null : job.User.DisplayName }).ToListAsync(http.RequestAborted))
            .ToDictionary(row => row.Id, row => row.Name, StringComparer.Ordinal);
        return Results.Ok(visible.Select(status => ToConversionDto(status, names.GetValueOrDefault(status.JobId) ?? RemovedAccount)).ToList());
    }

    // Compositions, archive moves/trash and cuts for the Jobs page; members see only their own, Admins see all.
    private static async Task<IResult> GetActivityAsync(HttpContext http, JobActivityService activity) =>
        Results.Ok(await activity.ListAsync(ArchiveListingAccess.UserId(http), await ArchiveListingAccess.IsAdminAsync(http), http.RequestAborted));

    private const string RemovedAccount = "Removed account";

    // Retry re-queues a Failed or Stopped conversion from its stored identity and selection in place under the same job id and card.
    private static async Task<IResult> RetryAsync(HttpContext http, string id, IVideoConversionJobStatusStore statuses, IJobVisibility owners, WebApp.Data.AppDbContext db, FolderLocator locator, IArchiveService archive, IVideoConversionProbe probe, ConversionProfileResolver resolver, IVideoConversionJobQueue queue, CancellationToken cancellationToken)
    {
        var status = statuses.Get(id);
        if (status is null || !owners.IsVisibleTo(id, ArchiveListingAccess.UserId(http), await ArchiveListingAccess.IsAdminAsync(http))) return Results.NotFound();
        if (status.State is not (VideoConversionJobState.Failed or VideoConversionJobState.Stopped)) return Results.Conflict(new { error = "Only a failed or stopped conversion can be retried." });

        var payload = await db.Jobs.AsNoTracking().Where(job => job.Id == id).Select(job => job.PayloadJson).FirstOrDefaultAsync(cancellationToken);
        var request = ReadRetryRequest(payload);
        if (request is null) return Results.Conflict(new { error = "This conversion cannot be retried." });

        var item = await db.MediaItems.AsNoTracking().Include(media => media.Folder)
            .FirstOrDefaultAsync(media => media.Id == request.MediaItemId && media.Status == WebApp.Data.Entities.MediaStatus.Active, cancellationToken);
        var physicalPath = item?.Folder is null ? null : locator.PhysicalPath(new FolderLocation(item.Folder.RootKey, item.RelativePath));
        if (physicalPath is null || !File.Exists(physicalPath) ||
            !archive.TryResolveConvertibleVideo(request.CategoryKey, archive.ComputeItemId(request.CategoryKey, physicalPath), out var source) || source is null)
            return Results.Conflict(new { error = "The original file is no longer available." });

        return await ArchiveEndpoints.QueueConversionAsync(http, source, request.Selection, probe, resolver, queue, statuses, cancellationToken, checkAccess: true, restartJobId: id);
    }

    private sealed record RetryRequest(long MediaItemId, string CategoryKey, VideoConversionSelectionDto Selection);

    private static RetryRequest? ReadRetryRequest(string? payload)
    {
        if (string.IsNullOrEmpty(payload)) return null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("categoryKey", out var category) || category.GetString() is not { Length: > 0 } categoryKey ||
                !root.TryGetProperty("selection", out var selection) || selection.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !root.TryGetProperty("sources", out var sources) || sources.ValueKind != System.Text.Json.JsonValueKind.Array || sources.GetArrayLength() != 1 ||
                !sources[0].TryGetProperty("MediaItemId", out var mediaItemId) || !mediaItemId.TryGetInt64(out var id))
                return null;
            var parsed = System.Text.Json.JsonSerializer.Deserialize<VideoConversionSelectionDto>(selection.GetRawText());
            return parsed is null ? null : new RetryRequest(id, categoryKey, parsed);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static Task<IResult> Pause(HttpContext http, string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller, IJobVisibility owners) => Control(http, id, statuses, owners, controller.Pause, statuses.Pause);
    private static Task<IResult> Resume(HttpContext http, string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller, IJobVisibility owners) => Control(http, id, statuses, owners, statuses.Resume, controller.Resume);
    private static Task<IResult> Stop(HttpContext http, string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller, IJobVisibility owners) => Control(http, id, statuses, owners, controller.Stop, statuses.Stop);
    private static async Task<IResult> Control(HttpContext http, string id, IVideoConversionJobStatusStore statuses, IJobVisibility owners, Func<string, bool> processAction, Func<string, bool> statusAction)
    {
        if (statuses.Get(id) is null || !owners.IsVisibleTo(id, ArchiveListingAccess.UserId(http), await ArchiveListingAccess.IsAdminAsync(http))) return Results.NotFound();
        if (!processAction(id) || !statusAction(id)) return Results.Conflict(new { message = "This conversion job is no longer in a state that can be controlled." });
        return Results.Ok(ToConversionDto(statuses.Get(id)!));
    }
    internal static VideoConversionJobDto ToConversionDto(WebApp.Models.VideoConversionStatus status, string? startedBy = null) => new(status.JobId, status.SourceName, status.Action.ToString(), status.State, status.SourceSizeBytes, status.OutputSizeBytes, status.OutputItemId, status.Diagnostic, status.QueuedAtUtc, status.StartedAtUtc, status.SourceDurationSeconds, status.ProcessedDurationSeconds, status.Speed, status.ProfileLabel, status.OutputHeight, status.EstimatedSizeBytes, status.FinishedAtUtc, startedBy);

    private static async Task<IResult> GetDockerAsync(IDockerMetricsService dockerMetricsService, CancellationToken cancellationToken) =>
        Results.Ok(await dockerMetricsService.GetDockerMetricsAsync(cancellationToken));

    private static IResult GetHealth(IHealthAggregationService healthAggregationService) =>
        Results.Ok(healthAggregationService.GetHealth());

    private static IResult GetHistory(IMetricsHistoryService metricsHistoryService) =>
        Results.Ok(metricsHistoryService.GetHistory());

    private static IResult GetAlerts(
        ISystemMetricsService systemMetricsService,
        IStorageUsageService storageUsageService,
        IAlertEvaluationService alertEvaluationService)
    {
        var system = systemMetricsService.GetSystemMetrics();
        var memory = systemMetricsService.GetMemoryMetrics();
        var storage = storageUsageService.GetUsage();

        double? memoryPercent = memory is { IsAvailable: true, TotalBytes: > 0 }
            ? memory.UsedBytes!.Value * 100.0 / memory.TotalBytes!.Value
            : null;
        double? storagePercent = storage.TotalBytes > 0 ? storage.UsedBytes * 100.0 / storage.TotalBytes : null;

        var inputs = new DashboardAlertInputs(storagePercent, memoryPercent, system.CpuPercent);
        return Results.Ok(new DashboardAlertsDto(alertEvaluationService.Evaluate(inputs)));
    }
}
