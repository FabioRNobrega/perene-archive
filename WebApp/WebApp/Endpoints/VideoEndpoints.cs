using WebApp.Authorization;
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
        var read = AccessRules.LibraryVideo(FolderOperation.Read);
        endpoints.MapPost("/api/videos/scan", ScanAsync);
        endpoints.MapGet("/api/videos", GetCurrentSnapshot);
        endpoints.MapGet("/api/videos/{id}", GetVideoById).RequireFolderAccess(read);
        endpoints.MapGet("/api/videos/{id}/stream", StreamAsync).RequireFolderAccess(read);
        endpoints.MapPost("/api/videos/{id}/audio-tracks/{index:int}", PrepareAudioTrackAsync).RequireFolderAccess(read);
        endpoints.MapGet("/api/videos/{id}/thumbnail", GetThumbnail).RequireFolderAccess(read);
        endpoints.MapGet("/api/videos/{id}/preview", GetPreview).RequireFolderAccess(read);
        endpoints.MapGet("/api/videos/{id}/subtitle", GetSubtitle).RequireFolderAccess(read);
        endpoints.MapPost("/api/videos/{id}/cuts", CreateCutAsync)
            .RequireFolderAccess(AccessRules.All(read, AccessRules.Root(FolderLocator.CutRootKey, FolderOperation.Create)));
        return endpoints;
    }

    private static async Task<IResult> ScanAsync(
        HttpContext http,
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = await FilterReadableAsync(http, await library.ScanAsync(cancellationToken));
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
        HttpContext http,
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var entries = await FilterReadableAsync(http, library.GetCurrentSnapshot());
        thumbnailCoordinator.Reconcile(entries);
        hoverPreviewCoordinator.Reconcile(entries);
        subtitleCoordinator.Reconcile(entries);
        var items = await Task.WhenAll(entries.Select(entry =>
            BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, cancellationToken)));
        return Results.Ok(items);
    }

    private static async Task<IResult> GetVideoById(
        HttpContext http,
        string id,
        IVideoLibraryService library,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        var entry = await library.ResolveAsync(id, cancellationToken);
        // ResolveAsync can rescan, so the entry it finds may not be the one the route filter saw: check it again.
        if (entry is null || (await FilterReadableAsync(http, [entry])).Count == 0)
        {
            return Results.NotFound();
        }

        var item = await BuildDto(entry, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, cancellationToken);
        return Results.Ok(item);
    }

    private static async Task<IResult> PrepareAudioTrackAsync(
        HttpContext http,
        string id,
        int index,
        IVideoLibraryService library,
        VideoMetadataCoordinator metadataCoordinator,
        AudioTrackRemuxService remuxService,
        IFolderJobAuthorizer jobAuthorizer,
        CancellationToken cancellationToken)
    {
        if (!library.TryResolve(id, out var entry) || entry is null ||
            !await IsValidAudioTrackAsync(entry, index, metadataCoordinator, cancellationToken))
        {
            return Results.NotFound();
        }

        var userId = ArchiveListingAccess.UserId(http);
        return Results.Ok(new AudioTrackPrepareResponse(
            remuxService.Ensure(entry, index, () => jobAuthorizer.CanReadAsync(userId, entry, CancellationToken.None)).ToString()));
    }

    internal static async Task<bool> IsValidAudioTrackAsync(
        VideoFileEntry entry,
        int index,
        VideoMetadataCoordinator metadataCoordinator,
        CancellationToken cancellationToken)
    {
        if (index < 1)
        {
            return false;
        }

        try
        {
            var metadata = await metadataCoordinator.GetOrComputeAsync(entry, cancellationToken);
            return index < (metadata.AudioTracks?.Count ?? 0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<IResult> StreamAsync(
        string id,
        int? audio,
        IVideoLibraryService library,
        VideoMetadataCoordinator metadataCoordinator,
        AudioTrackRemuxService remuxService,
        CancellationToken cancellationToken)
    {
        if (!library.TryResolve(id, out var entry) || entry is null)
        {
            return Results.NotFound();
        }

        if (audio is not null and not 0)
        {
            if (!await IsValidAudioTrackAsync(entry, audio.Value, metadataCoordinator, cancellationToken))
            {
                return Results.NotFound();
            }

            return remuxService.IsReady(entry, audio.Value)
                ? Results.File(remuxService.GetFinalPath(entry, audio.Value), "video/mp4", enableRangeProcessing: true)
                : Results.StatusCode(StatusCodes.Status409Conflict);
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
        HttpContext http,
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
        var job = new CutJob(jobId, entry, TimeSpan.FromSeconds(request.Start), TimeSpan.FromSeconds(request.End), ArchiveListingAccess.UserId(http));
        if (!queue.TryEnqueue(job))
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Accepted($"/api/cuts", new VideoCutResponse(jobId));
    }

    /// <summary>Keeps only the library videos whose folder the caller may read; Admins see everything.</summary>
    internal static async Task<IReadOnlyList<VideoFileEntry>> FilterReadableAsync(HttpContext http, IReadOnlyList<VideoFileEntry> entries)
    {
        var readable = await http.RequestServices.GetRequiredService<FolderAuthorizer>().GetReadableAsync(http.User, http.RequestAborted);
        if (readable is null) return [];
        if (readable.IsAdmin) return entries;
        var locator = http.RequestServices.GetRequiredService<FolderLocator>();
        return entries.Where(entry => locator.LocateContainer(entry.PhysicalPath) is { } location && readable.IsReadable(location)).ToList();
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
            metadata.Duration?.TotalSeconds, metadata.Width, metadata.Height,
            BuildAudioTracks(metadata));
    }

    internal static IReadOnlyList<AudioTrackDto>? BuildAudioTracks(VideoMetadata metadata) =>
        metadata.AudioTracks is { Count: > 1 } tracks
            ? tracks.Select(track => new AudioTrackDto(
                track.Index,
                track.Language,
                track.Language is null
                    ? $"Track {track.Index + 1} (language unknown)"
                    : $"Track {track.Index + 1} ({track.Language})")).ToList()
            : null;

    internal sealed record VideoCutRequest(double Start, double End);

    internal sealed record VideoCutResponse(string JobId);
}
