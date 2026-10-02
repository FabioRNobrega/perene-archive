using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Endpoints;

internal static class CompositionEndpoints
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mov"] = "video/quicktime",
            [".m4v"] = "video/x-m4v"
        };

    public static IEndpointRouteBuilder MapCompositionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var read = AccessRules.Root(FolderLocator.CompositionRootKey, FolderOperation.Read);
        endpoints.MapPost("/api/compositions", CreateCompositionAsync).RequireFolderAccess(AccessRules.All(
            AccessRules.Root(FolderLocator.CutRootKey, FolderOperation.Read),
            AccessRules.Root(FolderLocator.CompositionRootKey, FolderOperation.Create)));
        endpoints.MapGet("/api/compositions/jobs", GetJobs).RequireFolderAccess(read);
        endpoints.MapGet("/api/compositions", GetCurrentSnapshot);
        endpoints.MapGet("/api/compositions/{id}/stream", StreamAsync).RequireFolderAccess(read);
        endpoints.MapGet("/api/compositions/{id}/thumbnail", GetThumbnail).RequireFolderAccess(read);
        endpoints.MapGet("/api/compositions/{id}/preview", GetPreview).RequireFolderAccess(read);
        return endpoints;
    }

    private static async Task<IResult> CreateCompositionAsync(
        HttpContext http,
        CreateCompositionRequest request,
        IVideoCutService cuts,
        ICompositionJobQueue queue,
        ICompositionJobStatusStore statusStore,
        JobEnqueueService enqueue)
    {
        if (request.VideoIds is null || request.VideoIds.Count < 2)
        {
            return Results.BadRequest();
        }

        var resolved = new List<VideoFileEntry>(request.VideoIds.Count);
        foreach (var id in request.VideoIds)
        {
            if (!cuts.TryResolve(id, out var entry) || entry is null)
            {
                return Results.BadRequest();
            }

            resolved.Add(entry);
        }

        var ordered = resolved
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

        var jobId = Guid.NewGuid().ToString("N");
        var job = new CompositionJob(jobId, ordered, ArchiveListingAccess.UserId(http));
        statusStore.Seed(jobId, job.ActorUserId, await enqueue.BuildCompositionPayloadAsync(job, http.RequestAborted));

        if (!queue.TryEnqueue(job))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted("/api/compositions/jobs", new CreateCompositionResponse(jobId));
    }

    private static async Task<IResult> GetJobs(HttpContext http, ICompositionJobStatusStore statusStore, IJobVisibility visibility)
    {
        // A member sees the compositions they started; Admins see all.
        var isAdmin = await ArchiveListingAccess.IsAdminAsync(http);
        var userId = ArchiveListingAccess.UserId(http);
        var jobs = statusStore.GetAll()
            .Where(status => visibility.IsVisibleTo(status.JobId, userId, isAdmin))
            .Select(status => new CompositionJobDto(status.JobId, status.State, status.ResultVideoId, status.Diagnostic))
            .ToList();
        return Results.Ok(jobs);
    }

    private static async Task<IResult> GetCurrentSnapshot(
        HttpContext http,
        IVideoCompositionService compositions,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = await VideoEndpoints.FilterReadableAsync(http, await compositions.ScanAsync(cancellationToken));
            thumbnailCoordinator.Reconcile(entries);
            hoverPreviewCoordinator.Reconcile(entries);
            var items = await Task.WhenAll(entries.Select(entry =>
                BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, metadataCoordinator, cancellationToken)));
            return Results.Ok(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch
        {
            return Results.Problem(
                title: "Video compositions scan failed.",
                detail: "The configured compositions folder could not be scanned.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult StreamAsync(string id, IVideoCompositionService compositions)
    {
        if (!compositions.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        try
        {
            if (!ContentTypes.TryGetValue(entry.Extension, out var contentType))
            {
                return Results.NotFound();
            }

            var stream = new FileStream(
                entry.PhysicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Results.Stream(stream, contentType, enableRangeProcessing: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static IResult GetThumbnail(string id, IVideoCompositionService compositions, ThumbnailCoordinator coordinator)
    {
        if (!compositions.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        if (coordinator.Resolve(entry) != ThumbnailState.Ready)
        {
            return Results.NotFound();
        }

        try
        {
            var stream = new FileStream(
                coordinator.GetFinalPath(entry),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Results.Stream(stream, "image/jpeg");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static IResult GetPreview(string id, IVideoCompositionService compositions, HoverPreviewCoordinator coordinator)
    {
        if (!compositions.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        if (coordinator.Resolve(entry) != HoverPreviewState.Ready)
        {
            return Results.NotFound();
        }

        try
        {
            var stream = new FileStream(
                coordinator.GetFinalPath(entry),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return Results.Stream(stream, "video/mp4", enableRangeProcessing: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<VideoItemDto> BuildDto(
        VideoFileEntry entry,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var thumbnailState = thumbnailCoordinator.Resolve(entry);
        var thumbnailUrl = thumbnailState == ThumbnailState.Ready ? $"/api/compositions/{entry.Id}/thumbnail" : null;
        var hoverPreviewState = hoverPreviewCoordinator.Resolve(entry);
        var hoverPreviewUrl = hoverPreviewState == HoverPreviewState.Ready ? $"/api/compositions/{entry.Id}/preview" : null;

        VideoMetadata metadata;
        try
        {
            metadata = await metadataCoordinator.GetOrComputeAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            metadata = new VideoMetadata(null, null, null);
        }

        return new VideoItemDto(
            entry.Id, entry.Name, entry.Extension, entry.SizeBytes,
            thumbnailState, thumbnailUrl, hoverPreviewState, hoverPreviewUrl,
            SubtitleState.Unavailable, null,
            metadata.Duration?.TotalSeconds, metadata.Width, metadata.Height);
    }

    internal sealed record CreateCompositionResponse(string JobId);
}
