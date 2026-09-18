using WebApp.Client.Models;
using WebApp.Services;

namespace WebApp.Endpoints;

internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboard/system", GetSystem);
        endpoints.MapGet("/api/dashboard/memory", GetMemory);
        endpoints.MapGet("/api/dashboard/storage", GetStorage);
        endpoints.MapGet("/api/dashboard/network", GetNetwork);
        endpoints.MapGet("/api/dashboard/archive", GetArchive);
        endpoints.MapGet("/api/dashboard/docker", GetDockerAsync);
        endpoints.MapGet("/api/dashboard/health", GetHealth);
        endpoints.MapGet("/api/dashboard/history", GetHistory);
        endpoints.MapGet("/api/dashboard/alerts", GetAlerts);
        endpoints.MapGet("/api/dashboard/jobs", GetJobs);
        endpoints.MapPost("/api/dashboard/jobs/{id}/pause", Pause);
        endpoints.MapPost("/api/dashboard/jobs/{id}/resume", Resume);
        endpoints.MapPost("/api/dashboard/jobs/{id}/stop", Stop);
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

    private static IResult GetNetwork(INetworkMetricsService networkMetricsService) =>
        Results.Ok(networkMetricsService.GetNetworkMetrics());

    private static IResult GetArchive(IArchiveMetricsService archiveMetricsService) =>
        Results.Ok(archiveMetricsService.GetArchiveMetrics());

    private static IResult GetJobs(IVideoConversionJobStatusStore statuses) => Results.Ok(statuses.GetAll().Select(ToConversionDto));
    private static IResult Pause(string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller) => Control(id, statuses, controller.Pause, statuses.Pause);
    private static IResult Resume(string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller) => Control(id, statuses, controller.Resume, statuses.Resume);
    private static IResult Stop(string id, IVideoConversionJobStatusStore statuses, IVideoConversionProcessController controller) => Control(id, statuses, controller.Stop, statuses.Stop);
    private static IResult Control(string id, IVideoConversionJobStatusStore statuses, Func<string, bool> processAction, Func<string, bool> statusAction)
    {
        if (statuses.Get(id) is null) return Results.NotFound();
        if (!processAction(id) || !statusAction(id)) return Results.Conflict(new { message = "This conversion job is no longer in a state that can be controlled." });
        return Results.Ok(ToConversionDto(statuses.Get(id)!));
    }
    internal static VideoConversionJobDto ToConversionDto(WebApp.Models.VideoConversionStatus status) => new(status.JobId, status.SourceName, status.Action.ToString(), status.State, status.SourceSizeBytes, status.OutputSizeBytes, status.OutputItemId, status.Diagnostic, status.QueuedAtUtc, status.StartedAtUtc, status.SourceDurationSeconds, status.ProcessedDurationSeconds, status.Speed);

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
