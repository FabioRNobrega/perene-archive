using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Endpoints;

internal static class VideoEndpoints
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mov"] = "video/quicktime",
            [".m4v"] = "video/x-m4v"
        };

    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/videos/scan", ScanAsync);
        endpoints.MapGet("/api/videos", GetCurrentSnapshot);
        endpoints.MapGet("/api/videos/{id}", GetVideoById);
        endpoints.MapGet("/api/videos/{id}/stream", StreamAsync);
        endpoints.MapGet("/api/videos/{id}/thumbnail", GetThumbnail);
        endpoints.MapGet("/api/videos/{id}/preview", GetPreview);
        endpoints.MapGet("/api/videos/{id}/subtitle", GetSubtitle);
        endpoints.MapPost("/api/videos/{id}/cuts", CreateCutAsync);
        return endpoints;
    }

    private static async Task<IResult> ScanAsync(
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = await library.ScanAsync(cancellationToken);
            subtitleCoordinator.Reconcile(entries);
            var items = await Task.WhenAll(entries.Select(entry =>
                BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, cancellationToken)));
            return Results.Ok(items);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch
        {
            return Results.Problem(
                title: "Video library scan failed.",
                detail: "The configured library could not be scanned.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetCurrentSnapshot(
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var entries = library.GetCurrentSnapshot();
        thumbnailCoordinator.Reconcile(entries);
        hoverPreviewCoordinator.Reconcile(entries);
        subtitleCoordinator.Reconcile(entries);
        var items = await Task.WhenAll(entries.Select(entry =>
            BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, cancellationToken)));
        return Results.Ok(items);
    }

    private static async Task<IResult> GetVideoById(
        string id,
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var entry = await library.ResolveAsync(id, cancellationToken);
        if (entry is null)
        {
            return Results.NotFound();
        }

        var item = await BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, cancellationToken);
        return Results.Ok(item);
    }

    private static IResult StreamAsync(string id, IVideoLibraryService library)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
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

    private static IResult GetThumbnail(string id, IVideoLibraryService library, ThumbnailCoordinator coordinator)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
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

    private static IResult GetPreview(
        string id, IVideoLibraryService library, HoverPreviewCoordinator coordinator)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
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

    private static IResult GetSubtitle(string id, IVideoLibraryService library, SubtitleCoordinator coordinator)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        if (coordinator.Resolve(entry) != SubtitleState.Ready)
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

            return Results.Stream(stream, "text/vtt");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> CreateCutAsync(
        string id,
        VideoCutRequest request,
        IVideoLibraryService library,
        IVideoDurationProbe durationProbe,
        ICutJobQueue queue,
        CancellationToken cancellationToken)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        if (!double.IsFinite(request.Start) || !double.IsFinite(request.End) ||
            request.Start < 0 || request.Start >= request.End)
        {
            return Results.BadRequest();
        }

        TimeSpan? duration;
        try
        {
            duration = await durationProbe.GetDurationAsync(entry.PhysicalPath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch
        {
            return Results.BadRequest();
        }

        if (duration is null || TimeSpan.FromSeconds(request.End) > duration.Value)
        {
            return Results.BadRequest();
        }

        var jobId = Guid.NewGuid().ToString("N");
        var job = new CutJob(jobId, entry, TimeSpan.FromSeconds(request.Start), TimeSpan.FromSeconds(request.End));
        if (!queue.TryEnqueue(job))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted($"/api/cuts", new VideoCutResponse(jobId));
    }

    private static async Task<VideoItemDto> BuildDto(
        VideoFileEntry entry,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var thumbnailState = thumbnailCoordinator.Resolve(entry);
        var thumbnailUrl = thumbnailState == ThumbnailState.Ready ? $"/api/videos/{entry.Id}/thumbnail" : null;
        var hoverPreviewState = hoverPreviewCoordinator.Resolve(entry);
        var hoverPreviewUrl = hoverPreviewState == HoverPreviewState.Ready ? $"/api/videos/{entry.Id}/preview" : null;
        var subtitleState = subtitleCoordinator.Resolve(entry);
        var subtitleUrl = subtitleState == SubtitleState.Ready ? $"/api/videos/{entry.Id}/subtitle" : null;

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
            subtitleState, subtitleUrl,
            metadata.Duration?.TotalSeconds, metadata.Width, metadata.Height);
    }

    internal sealed record VideoCutRequest(double Start, double End);

    internal sealed record VideoCutResponse(string JobId);
}
