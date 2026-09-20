using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Endpoints;

internal static class ArchiveEndpoints
{
    public static IEndpointRouteBuilder MapArchiveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/{category}/items", List);
        endpoints.MapGet("/api/archive/{category}/items/{id}/playlist", GetPlaylist);
        endpoints.MapGet("/api/archive/{category}/items/{id}/stream", StreamVideo);
        endpoints.MapGet("/api/archive/{category}/items/{id}/download", DownloadAsync);
        endpoints.MapGet("/api/archive/{category}/items/{id}/audio", StreamAudio);
        endpoints.MapGet("/api/archive/{category}/items/{id}/cover", GetAlbumCover);
        endpoints.MapGet("/api/archive/{category}/items/{id}/image", GetImage);
        endpoints.MapGet("/api/archive/{category}/items/{id}/comic", GetComic);
        endpoints.MapGet("/api/archive/{category}/items/{id}/comic/pages/{index:int}", GetComicPage);
        endpoints.MapGet("/api/archive/{category}/items/{id}/thumbnail", GetThumbnail);
        endpoints.MapGet("/api/archive/{category}/items/{id}/preview", GetPreview);
        endpoints.MapGet("/api/archive/{category}/items/{id}/subtitle", GetSubtitle);
        endpoints.MapPost("/api/archive/{category}/items/{id}/crop", CreateCropAsync);
        endpoints.MapPost("/api/archive/{category}/items/{id}/conversion/preview", PreviewConversionAsync);
        endpoints.MapPost("/api/archive/{category}/items/{id}/conversion", CreateConversionAsync);
        endpoints.MapGet("/api/archive/{category}/items/{id}/text", GetTextDocument);
        endpoints.MapPost("/api/archive/{category}/items/{id}/text/preview", PreviewTextDocument);
        endpoints.MapPut("/api/archive/{category}/items/{id}/text", SaveTextDocument);
        endpoints.MapPost("/api/archive/{category}/items/{id}/text/export-pdf", ExportTextDocumentPdf);
        endpoints.MapGet("/api/archive/{category}/items/{id}/pdf", GetPdfDocument);
        endpoints.MapGet("/api/archive/{category}/items/{id}/book", GetBookAsync);
        endpoints.MapGet("/api/archive/{category}/items/{id}/book/cover", GetBookCover);
        endpoints.MapGet("/api/archive/{category}/items/{id}/book/chapters/{chapterId}", GetBookChapter);
        endpoints.MapPost("/api/archive/{category}/items/{id}/book/notes", SaveBookNoteAsync);
        endpoints.MapGet("/api/archive/{category}/items/{id}/book/highlights", GetBookHighlightsAsync);
        endpoints.MapGet("/api/archive/{category}/items/{id}/book/progress", GetBookProgressAsync);
        endpoints.MapPut("/api/archive/{category}/items/{id}/book/progress", SaveBookProgressAsync);
        endpoints.MapPost("/api/archive/{category}/folders", CreateFolder);
        endpoints.MapPost("/api/archive/{category}/files", CreateFile);
        endpoints.MapPost("/api/archive/{category}/uploads", CreateUploadSession);
        endpoints.MapGet("/api/archive/{category}/uploads", ListUploadSessions);
        endpoints.MapGet("/api/archive/{category}/uploads/{uploadId}", GetUploadStatus);
        endpoints.MapPut("/api/archive/{category}/uploads/{uploadId}/chunk", AppendUploadChunkAsync).DisableAntiforgery();
        endpoints.MapPost("/api/archive/{category}/uploads/{uploadId}/complete", CompleteUploadAsync);
        endpoints.MapDelete("/api/archive/{category}/uploads/{uploadId}", CancelUploadAsync);
        endpoints.MapPatch("/api/archive/{category}/items/{id}/name", Rename);
        endpoints.MapPatch("/api/archive/{category}/items/{id}/location", Move);
        endpoints.MapDelete("/api/archive/{category}/items/{id}", MoveToTrash);
        endpoints.MapDelete("/api/archive/{category}/items", EmptyTrash);
        return endpoints;
    }

    private static async Task<IResult> PreviewConversionAsync(string category, string id, VideoConversionSelectionDto? requestedSelection, IArchiveService archive, IVideoConversionProbe probe, ConversionProfileCatalog catalog, ConversionProfileResolver resolver, IOptions<VideoConversionOptions> options, CancellationToken cancellationToken)
    {
        if (!archive.TryResolveConvertibleVideo(category, id, out var item) || item is null) return Results.BadRequest(new { error = "This file cannot be converted." });
        var media = await probe.ProbeAsync(item.PhysicalPath, cancellationToken);
        if (media is null) return Results.BadRequest(new { error = "The selected file is not a readable video." });
        var selection = requestedSelection is null ? catalog.DefaultFor(media) : new VideoConversionSelection(requestedSelection.Mode, requestedSelection.OutputHeight, requestedSelection.QualityPreset, requestedSelection.TargetSizeBytes);
        return !resolver.TryResolve(media, item.SizeBytes, selection, out var profile, out var error)
            ? Results.BadRequest(new { error })
            : Results.Ok(ToPlanDto(item, media, profile!, catalog, options.Value));
    }

    private static async Task<IResult> CreateConversionAsync(string category, string id, VideoConversionSelectionDto selection, IArchiveService archive, IVideoConversionProbe probe, ConversionProfileCatalog catalog, ConversionProfileResolver resolver, IVideoConversionJobQueue queue, IVideoConversionJobStatusStore statuses, CancellationToken cancellationToken)
    {
        if (!archive.TryResolveConvertibleVideo(category, id, out var item) || item is null) return Results.BadRequest(new { error = "This file cannot be converted." });
        if (statuses.HasActiveSource(item.Id)) return Results.Conflict(new { error = "A conversion is already active for this file." });
        var media = await probe.ProbeAsync(item.PhysicalPath, cancellationToken);
        if (media is null) return Results.BadRequest(new { error = "The selected file is not a readable video." });
        var serverSelection = new VideoConversionSelection(selection.Mode, selection.OutputHeight, selection.QualityPreset, selection.TargetSizeBytes);
        if (!resolver.TryResolve(media, item.SizeBytes, serverSelection, out var profile, out var error)) return Results.BadRequest(new { error });
        var job = new VideoConversionJob(Guid.NewGuid().ToString("N"), item, profile!.Action, media, profile);
        statuses.Seed(job);
        if (!queue.TryEnqueue(job)) { statuses.Remove(job.JobId); return Results.StatusCode(StatusCodes.Status503ServiceUnavailable); }
        return Results.Accepted($"/api/dashboard/jobs/{job.JobId}", DashboardEndpoints.ToConversionDto(statuses.GetAll().Single(x => x.JobId == job.JobId)));
    }

    private static VideoConversionPlanDto ToPlanDto(ArchiveItemEntry item, VideoConversionProbeResult media, ResolvedVideoConversionProfile profile, ConversionProfileCatalog catalog, VideoConversionOptions options) => new(
        item.Id, item.Name, Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant(), media.VideoCodec, media.AudioCodec, media.Width, media.Height, media.Duration.TotalSeconds, item.SizeBytes,
        new(profile.Selection.Mode, profile.Selection.OutputHeight, profile.Selection.QualityPreset, profile.Selection.TargetSizeBytes), profile.Label, profile.OutputWidth, profile.OutputHeight, profile.VideoBitrate, profile.AudioBitrate, profile.EstimatedSizeBytes, profile.EstimatedSavingsBytes,
        [new("compatible", "Make compatible / preserve quality"), new("compress", "Compress")],
        catalog.HeightsFor(media).Select(height => new VideoConversionOptionDto(height.ToString(), height == media.Height ? "Original resolution" : $"{height}p", height == 720 && media.Height > 720)).ToList(),
        [new("high", "Preserve quality"), new("balanced", "Balanced", true), new("compact", "Smaller file")], options.MinimumTargetSizeBytes, options.MaximumTargetSizeBytes);

    private static async Task<IResult> List(
        string category,
        string? folderId,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.List(category, folderId),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> GetPlaylist(
        string category,
        string id,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.ListPlaylist(category, id),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> CreateFolder(
        string category,
        CreateFolderRequest request,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.CreateFolder(category, request.ParentId, request.Name),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> CreateFile(
        string category,
        CreateFileRequest request,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.CreateFile(category, request.ParentId, request.Name, request.Extension),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static IResult CreateUploadSession(
        string category,
        ArchiveUploadCreateRequest request,
        IArchiveUploadService uploads)
    {
        try
        {
            var session = uploads.Create(category, request.ParentId, request.FileName, request.TotalBytes);
            return Results.Ok(ToDto(session));
        }
        catch (ArchiveValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArchiveForbiddenException exception)
        {
            return Results.Problem(title: "Archive upload is not allowed.", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArchiveConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static IResult ListUploadSessions(string category, string? parentId, IArchiveUploadService uploads) =>
        Results.Ok(uploads.ListActive(category, parentId).Select(ToDto).ToList());

    private static IResult GetUploadStatus(string category, string uploadId, IArchiveUploadService uploads)
    {
        try
        {
            return Results.Ok(ToDto(uploads.GetStatus(category, uploadId)));
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> AppendUploadChunkAsync(
        string category,
        string uploadId,
        long offset,
        long length,
        HttpRequest request,
        IArchiveUploadService uploads,
        IOptions<ArchiveUploadOptions> uploadOptions,
        CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            return Results.BadRequest(new { error = "A positive chunk length is required." });
        }

        var maxBodySizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxBodySizeFeature is { IsReadOnly: false })
        {
            maxBodySizeFeature.MaxRequestBodySize = uploadOptions.Value.ExtraLargeChunkSizeBytes;
        }

        try
        {
            var session = await uploads.AppendChunkAsync(category, uploadId, offset, length, request.Body, cancellationToken);
            return Results.Ok(ToDto(session));
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (ArchiveValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArchiveConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }

    private static async Task<IResult> CompleteUploadAsync(
        string category,
        string uploadId,
        IArchiveUploadService uploads,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(async () =>
        {
            var listing = await uploads.CompleteAsync(category, uploadId, cancellationToken);
            return await ToDtoAsync(
                listing,
                thumbnailCoordinator,
                hoverPreviewCoordinator,
                subtitleCoordinator,
                metadataCoordinator,
                epubBookService,
                cancellationToken);
        });

    private static async Task<IResult> CancelUploadAsync(
        string category, string uploadId, IArchiveUploadService uploads, CancellationToken cancellationToken)
    {
        try
        {
            await uploads.CancelAsync(category, uploadId, cancellationToken);
            return Results.Ok();
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static ArchiveUploadSessionDto ToDto(WebApp.Models.ArchiveUploadSession session) =>
        new(
            session.Id,
            session.CategoryKey,
            session.ParentId,
            session.FileName,
            session.TotalBytes,
            session.ReceivedBytes,
            session.ChunkSizeBytes,
            session.Status switch
            {
                WebApp.Models.ArchiveUploadSessionStatus.Completed => ArchiveUploadStatus.Completed,
                WebApp.Models.ArchiveUploadSessionStatus.Cancelled => ArchiveUploadStatus.Cancelled,
                _ => ArchiveUploadStatus.Uploading
            },
            session.CreatedAt,
            session.LastActivityAt);

    private static async Task<IResult> Rename(
        string category,
        string id,
        RenameArchiveItemRequest request,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.Rename(category, id, request.Name),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> Move(
        string category,
        string id,
        MoveArchiveItemRequest request,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.Move(category, id, request.DestinationCategory, request.DestinationFolderId),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> MoveToTrash(
        string category,
        string id,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.MoveToTrash(category, id),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> EmptyTrash(
        string category,
        IArchiveService archive,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(() => ToDtoAsync(
            archive.EmptyTrash(category),
            thumbnailCoordinator,
            hoverPreviewCoordinator,
            subtitleCoordinator,
            metadataCoordinator,
            epubBookService,
            cancellationToken));

    private static async Task<IResult> CreateCropAsync(
        string category,
        string id,
        ImageCropRequest request,
        IImageCropService cropService,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await cropService.CropAsync(
                category, id, request.X, request.Y, request.Width, request.Height, cancellationToken);

            return outcome.Status switch
            {
                ImageCropOutcomeStatus.Success => Results.Ok(new ImageCropResponse(
                    outcome.Id!,
                    outcome.Name!,
                    $"/api/archive/{Uri.EscapeDataString(category)}/items/{Uri.EscapeDataString(outcome.Id!)}/image")),
                ImageCropOutcomeStatus.NotFound => Results.NotFound(),
                ImageCropOutcomeStatus.OutOfBounds => Results.BadRequest(new { error = outcome.Diagnostic ?? "The crop region is invalid." }),
                _ => Results.Problem(
                    title: "Image crop failed.",
                    detail: "The image could not be cropped.",
                    statusCode: StatusCodes.Status500InternalServerError)
            };
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private static async Task<IResult> DownloadAsync(
        string category,
        string id,
        HttpResponse response,
        IArchiveService archive,
        IArchiveDownloadService downloads,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveDownloadableItem(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (item.Kind == ArchiveItemKind.File)
        {
            try
            {
                var stream = new FileStream(
                    item.PhysicalPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                return Results.File(
                    stream,
                    contentType: "application/octet-stream",
                    fileDownloadName: item.Name,
                    lastModified: item.LastWriteTimeUtc,
                    enableRangeProcessing: true);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
                return Results.NotFound();
            }
        }

        response.ContentType = "application/zip";
        response.Headers.ContentDisposition = $"attachment; filename=\"{item.Name.Replace("\\", "\\\\").Replace("\"", "\\\"")}.zip\"";
        await using var zipStream = response.BodyWriter.AsStream(leaveOpen: true);
        await downloads.WriteFolderZipAsync(item, zipStream, cancellationToken);
        return Results.Empty;
    }

    private static IResult StreamVideo(string category, string id, IArchiveService archive)
    {
        if (!archive.TryResolveVideo(category, id, out var item) || item is null || item.Extension is null)
        {
            return Results.NotFound();
        }

        if (!VideoContentTypes.TryGetValue(item.Extension, out var contentType))
        {
            return Results.NotFound();
        }

        try
        {
            var stream = new FileStream(
                item.PhysicalPath,
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

    private static IResult StreamAudio(string category, string id, IArchiveService archive)
    {
        if (!archive.TryResolveMusic(category, id, out var item) || item is null || item.Extension is null)
        {
            return Results.NotFound();
        }

        if (!AudioContentTypes.TryGetValue(item.Extension, out var contentType))
        {
            return Results.NotFound();
        }

        try
        {
            var stream = new FileStream(
                item.PhysicalPath,
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

    private static IResult GetAlbumCover(string category, string id, IArchiveService archive)
    {
        if (!archive.TryResolveAlbumCover(category, id, out var cover) || cover is null)
        {
            return Results.NotFound();
        }

        if (!ImageContentTypes.TryGetValue(cover.Extension, out var contentType))
        {
            return Results.NotFound();
        }

        return Results.File(cover.PhysicalPath, contentType, lastModified: cover.LastWriteTimeUtc);
    }

    private static IResult GetImage(string category, string id, IArchiveService archive)
    {
        if (!archive.TryResolveImage(category, id, out var item) || item is null || item.Extension is null)
        {
            return Results.NotFound();
        }

        if (!ImageContentTypes.TryGetValue(item.Extension, out var contentType))
        {
            return Results.NotFound();
        }

        return Results.File(item.PhysicalPath, contentType, lastModified: item.LastWriteTimeUtc);
    }

    private static IResult GetComic(string category, string id, IArchiveService archive, IComicBookService comics, ILogger<Program> logger)
    {
        if (!archive.TryResolveComic(category, id, out var item) || item is null)
        {
            logger.LogWarning("Comic metadata request could not resolve opaque archive item. Category={Category} ItemId={ItemId}", category, id);
            return Results.NotFound();
        }
        if (!comics.TryGetMetadata(item, out var metadata) || metadata is null)
        {
            logger.LogWarning("Comic metadata request was rejected while reading CBZ content. Category={Category} ItemId={ItemId}", category, id);
            return Results.NotFound();
        }
        logger.LogInformation("Comic metadata returned. Category={Category} ItemId={ItemId} PageCount={PageCount}", category, id, metadata.PageCount);
        return Results.Ok(new ComicBookDto(item.Name, metadata.PageCount));
    }

    private static IResult GetComicPage(string category, string id, int index, IArchiveService archive, IComicBookService comics, ILogger<Program> logger)
    {
        if (!archive.TryResolveComic(category, id, out var item) || item is null)
        {
            logger.LogWarning("Comic page request could not resolve opaque archive item. Category={Category} ItemId={ItemId} Index={Index}", category, id, index);
            return Results.NotFound();
        }
        if (!comics.TryGetPage(item, index, out var page) || page is null)
        {
            logger.LogWarning("Comic page request was rejected while reading CBZ content. Category={Category} ItemId={ItemId} Index={Index}", category, id, index);
            return Results.NotFound();
        }
        return Results.File(page.Bytes, page.ContentType);
    }

    private static IResult GetThumbnail(
        string category,
        string id,
        IArchiveService archive,
        ThumbnailCoordinator coordinator)
    {
        if (!TryResolveMediaEntry(category, id, archive, out var entry) ||
            entry is null ||
            coordinator.Resolve(entry) != ThumbnailState.Ready)
        {
            return Results.NotFound();
        }

        return Results.File(coordinator.GetFinalPath(entry), "image/jpeg");
    }

    private static IResult GetPreview(
        string category,
        string id,
        IArchiveService archive,
        HoverPreviewCoordinator coordinator)
    {
        if (!TryResolveMediaEntry(category, id, archive, out var entry) ||
            entry is null ||
            coordinator.Resolve(entry) != HoverPreviewState.Ready)
        {
            return Results.NotFound();
        }

        return Results.File(coordinator.GetFinalPath(entry), "video/mp4", enableRangeProcessing: true);
    }

    private static IResult GetSubtitle(
        string category,
        string id,
        IArchiveService archive,
        SubtitleCoordinator coordinator)
    {
        if (!TryResolveMediaEntry(category, id, archive, out var entry) ||
            entry is null ||
            coordinator.Resolve(entry) != SubtitleState.Ready)
        {
            return Results.NotFound();
        }

        return Results.File(coordinator.GetFinalPath(entry), "text/vtt");
    }

    private static IResult GetTextDocument(string category, string id, IArchiveService archive, ITextDocumentService textDocuments)
    {
        if (!archive.TryResolveTextDocument(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        try
        {
            var result = textDocuments.Load(item);
            return Results.Ok(new TextDocumentDto(item.Id, item.Name, result.DocumentKind, result.Source, result.Revision, result.PreviewHtml));
        }
        catch (TextDocumentValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static IResult PreviewTextDocument(
        string category, string id, TextDocumentPreviewRequest request, IArchiveService archive, ITextDocumentService textDocuments)
    {
        if (!archive.TryResolveTextDocument(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (request.Source is null)
        {
            return Results.BadRequest(new { error = "Source is required." });
        }

        if (System.Text.Encoding.UTF8.GetByteCount(request.Source) > TextDocumentService.MaxSourceBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var html = textDocuments.RenderPreview(item, request.Source);
            return Results.Ok(new TextDocumentPreviewDto(html));
        }
        catch (TextDocumentValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }

    private static IResult SaveTextDocument(
        string category, string id, TextDocumentSaveRequest request, IArchiveService archive, ITextDocumentService textDocuments)
    {
        if (!archive.TryResolveTextDocument(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (request.Source is null || string.IsNullOrEmpty(request.Revision))
        {
            return Results.BadRequest(new { error = "Source and revision are required." });
        }

        if (System.Text.Encoding.UTF8.GetByteCount(request.Source) > TextDocumentService.MaxSourceBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var result = textDocuments.Save(item, request.Source, request.Revision);
            return Results.Ok(new TextDocumentDto(item.Id, item.Name, result.DocumentKind, request.Source, result.Revision, result.PreviewHtml));
        }
        catch (TextDocumentConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (TextDocumentValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                title: "The document could not be saved.",
                detail: "The document could not be written.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult ExportTextDocumentPdf(
        string category, string id, IArchiveService archive, ITextDocumentService textDocuments, ITextDocumentPdfExporter pdfExporter)
    {
        if (!archive.TryResolveTextDocument(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        try
        {
            var loaded = textDocuments.Load(item);
            var pdfBytes = pdfExporter.Export(item.Name, loaded.PreviewHtml);
            var fileName = textDocuments.SavePdfExport(item, pdfBytes);
            return Results.Ok(new TextDocumentExportPdfDto(fileName));
        }
        catch (TextDocumentValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                title: "The document could not be exported.",
                detail: "The PDF file could not be written.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult GetPdfDocument(string category, string id, IArchiveService archive)
    {
        if (!archive.TryResolvePdfDocument(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        return Results.File(item.PhysicalPath, "application/pdf", lastModified: item.LastWriteTimeUtc, enableRangeProcessing: true);
    }

    private static async Task<IResult> GetBookAsync(
        string category,
        string id,
        IArchiveService archive,
        IEpubBookService epubBookService,
        IEpubProgressService progressService,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (!epubBookService.TryGetBook(item, out var book) || book is null)
        {
            return Results.NotFound();
        }

        BookProgressDto? progress;
        try
        {
            progress = await progressService.LoadProgressAsync(
                category, item.Id, item.SizeBytes, item.LastWriteTimeUtc, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }

        var coverUrl = book.HasCover ? BookCoverUrl(item) : null;
        return Results.Ok(book with { CoverUrl = coverUrl, Progress = progress });
    }

    private static IResult GetBookCover(string category, string id, IArchiveService archive, IEpubBookService epubBookService)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (!epubBookService.TryGetCover(item, out var coverBytes, out var contentType) || coverBytes is null || contentType is null)
        {
            return Results.NotFound();
        }

        return Results.File(coverBytes, contentType);
    }

    private static IResult GetBookChapter(
        string category,
        string id,
        string chapterId,
        IArchiveService archive,
        IEpubBookService epubBookService)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (!epubBookService.TryGetChapter(item, chapterId, out var chapter) || chapter is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(chapter);
    }

    private static async Task<IResult> SaveBookNoteAsync(
        string category,
        string id,
        BookNoteRequest request,
        IArchiveService archive,
        IEpubBookService epubBookService,
        IEpubNoteService noteService,
        IEpubHighlightService highlightService,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.SelectedText))
        {
            return Results.BadRequest(new { error = "Selected text is required to save a note." });
        }

        if (!int.TryParse(request.ChapterId, out var chapterIndex) || chapterIndex < 0 ||
            request.TextOffsetStart is not { } start || request.TextOffsetEnd is not { } end || start < 0 || end <= start)
        {
            return Results.BadRequest(new { error = "A valid chapter selection is required to save a note." });
        }

        if (!epubBookService.TryGetBook(item, out var book) || book is null)
        {
            return Results.NotFound();
        }

        if (!epubBookService.TryGetChapter(item, request.ChapterId, out var chapter) || chapter is null)
        {
            return Results.BadRequest(new { error = "The selected chapter could not be validated." });
        }

        var chapterText = chapter.NormalizedText;
        var selectedText = EpubChapterText.Normalize(request.SelectedText);
        if (end > chapterText.Length || !string.Equals(chapterText[start..end], selectedText, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "The selected text no longer matches this chapter.",
                selectionLength = end - start,
                chapterLength = chapterText.Length
            });
        }

        var highlight = new BookHighlightDto(
            Guid.NewGuid().ToString("N"), request.ChapterId, start, end, selectedText,
            request.ContextBefore?.Trim() ?? string.Empty, request.ContextAfter?.Trim() ?? string.Empty, DateTimeOffset.UtcNow);

        try
        {
            await highlightService.SaveHighlightAsync(
                item.Category.Key, item.Id, item.SizeBytes, item.LastWriteTimeUtc, highlight, cancellationToken);
            await noteService.AppendNoteAsync(
                book.Title,
                book.Author,
                chapterIndex,
                request.TextOffsetStart,
                request.TextOffsetEnd,
                request.SelectedText,
                cancellationToken);
            return Results.Ok();
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                title: "The note could not be saved.",
                detail: "The note file could not be written.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> GetBookHighlightsAsync(
        string category,
        string id,
        IArchiveService archive,
        IEpubHighlightService highlightService,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        try
        {
            var highlights = await highlightService.LoadHighlightsAsync(
                item.Category.Key, item.Id, item.SizeBytes, item.LastWriteTimeUtc, cancellationToken);
            return Results.Ok(highlights);
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private static async Task<IResult> GetBookProgressAsync(
        string category,
        string id,
        IArchiveService archive,
        IEpubProgressService progressService,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        try
        {
            var progress = await progressService.LoadProgressAsync(
                category, item.Id, item.SizeBytes, item.LastWriteTimeUtc, cancellationToken);
            // Results.Json/Ok write an empty body (rather than the JSON literal "null") when the value is null,
            // so serialize explicitly to keep this endpoint's response body always JSON-parseable.
            return Results.Text(System.Text.Json.JsonSerializer.Serialize(progress), "application/json");
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
    }

    private static async Task<IResult> SaveBookProgressAsync(
        string category,
        string id,
        BookProgressDto request,
        IArchiveService archive,
        IEpubProgressService progressService,
        CancellationToken cancellationToken)
    {
        if (!archive.TryResolveBook(category, id, out var item) || item is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.ChapterId))
        {
            return Results.BadRequest(new { error = "A valid chapter is required to save progress." });
        }

        if (request.WordOffset < 0)
        {
            return Results.BadRequest(new { error = "A valid word offset is required to save progress." });
        }

        try
        {
            await progressService.SaveProgressAsync(
                category, item.Id, item.SizeBytes, item.LastWriteTimeUtc, request, cancellationToken);
            return Results.Ok();
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(
                title: "Progress could not be saved.",
                detail: "The progress file could not be written.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult Execute(Func<ArchiveListingDto> action)
    {
        try
        {
            return Results.Ok(action());
        }
        catch (ArchiveValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArchiveForbiddenException exception)
        {
            return Results.Problem(title: "Archive operation is not allowed.", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArchiveConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(title: "Archive operation failed.", detail: "The archive item could not be changed.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<ArchiveListingDto>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (OperationCanceledException)
        {
            return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (ArchiveValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (ArchiveForbiddenException exception)
        {
            return Results.Problem(title: "Archive operation is not allowed.", detail: exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArchiveConflictException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Results.Problem(title: "Archive operation failed.", detail: "The archive item could not be changed.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static readonly IReadOnlyDictionary<string, string> VideoContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".mov"] = "video/quicktime",
            [".m4v"] = "video/x-m4v"
        };

    private static readonly IReadOnlyDictionary<string, string> AudioContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = "audio/mpeg",
            [".wav"] = "audio/wav",
            [".m4a"] = "audio/mp4"
        };

    private static readonly IReadOnlyDictionary<string, string> ImageContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".avif"] = "image/avif",
            [".bmp"] = "image/bmp",
            [".ico"] = "image/x-icon"
        };

    private static ArchiveListingDto ToDto(ArchiveListing listing) =>
        new(
            listing.Category.Key,
            listing.Category.DisplayName,
            IsCategoryRoot(listing) ? null : listing.CurrentFolder.Id,
            listing.ParentFolder?.Id,
            listing.Category.CanCreateFolder,
            listing.Breadcrumbs,
            listing.Items.Select(item => ToDto(item)).ToList());

    private static ArchiveListingDto ToDto(
        ArchiveListing listing,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator)
    {
        var videoEntries = listing.Items
            .Where(item => item.IsVideo)
            .Select(ToMediaEntry)
            .ToList();
        thumbnailCoordinator.Reconcile(videoEntries);
        hoverPreviewCoordinator.Reconcile(videoEntries);
        subtitleCoordinator.Reconcile(videoEntries);

        return new(
            listing.Category.Key,
            listing.Category.DisplayName,
            IsCategoryRoot(listing) ? null : listing.CurrentFolder.Id,
            listing.ParentFolder?.Id,
            listing.Category.CanCreateFolder,
            listing.Breadcrumbs,
            listing.Items.Select(item => ToDto(item, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator)).ToList());
    }

    private static async Task<ArchiveListingDto> ToDtoAsync(
        ArchiveListing listing,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken)
    {
        var videoEntries = listing.Items
            .Where(item => item.IsVideo)
            .Select(ToMediaEntry)
            .ToList();
        thumbnailCoordinator.Reconcile(videoEntries);
        hoverPreviewCoordinator.Reconcile(videoEntries);
        subtitleCoordinator.Reconcile(videoEntries);

        var items = await Task.WhenAll(listing.Items.Select(item =>
            ToDtoAsync(item, thumbnailCoordinator, hoverPreviewCoordinator, subtitleCoordinator, metadataCoordinator, epubBookService, cancellationToken)));

        return new(
            listing.Category.Key,
            listing.Category.DisplayName,
            IsCategoryRoot(listing) ? null : listing.CurrentFolder.Id,
            listing.ParentFolder?.Id,
            listing.Category.CanCreateFolder,
            listing.Breadcrumbs,
            items);
    }

    private static ArchiveItemDto ToDto(ArchiveItemEntry item, IEpubBookService? epubBookService = null)
    {
        var (bookCoverUrl, bookTitle, bookAuthor) = item.IsBook
            ? ReadBookSummary(item, epubBookService)
            : (null, null, null);

        return new(
            item.Id,
            item.Name,
            item.Kind,
            item.Extension,
            item.SizeBytes,
            item.LastWriteTimeUtc,
            item.IsVideo,
            IsMusic: item.IsMusic,
            AudioUrl: AudioUrl(item),
            AlbumCoverUrl: AlbumCoverUrl(item),
            IsImage: item.IsImage,
            ImageUrl: ImageUrl(item),
            IsBook: item.IsBook,
            BookCoverUrl: bookCoverUrl,
            BookTitle: bookTitle,
            BookAuthor: bookAuthor,
            IsTextDocument: item.IsTextDocument,
            IsPdfDocument: item.IsPdfDocument,
            IsComic: item.IsComic,
            PdfUrl: PdfUrl(item),
            HasPlayableMedia: item.HasPlayableMedia,
            IsConvertibleVideo: item.IsConvertibleVideo);
    }

    private static (string? CoverUrl, string? Title, string? Author) ReadBookSummary(
        ArchiveItemEntry item, IEpubBookService? epubBookService)
    {
        if (epubBookService is null || !epubBookService.TryGetBook(item, out var book) || book is null)
        {
            return (null, null, null);
        }

        var coverUrl = book.HasCover ? BookCoverUrl(item) : null;
        return (coverUrl, book.Title, book.Author);
    }

    private static ArchiveItemDto ToDto(
        ArchiveItemEntry item,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator)
    {
        if (!item.IsVideo && !item.IsMusic)
        {
            return ToDto(item);
        }

        var entry = ToMediaEntry(item);
        var thumbnailState = ThumbnailState.Unavailable;
        string? thumbnailUrl = null;
        var hoverPreviewState = HoverPreviewState.Unavailable;
        string? hoverPreviewUrl = null;
        var subtitleState = SubtitleState.Unavailable;
        string? subtitleUrl = null;
        if (item.IsVideo)
        {
            thumbnailState = thumbnailCoordinator.Resolve(entry);
            thumbnailUrl = thumbnailState == ThumbnailState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/thumbnail"
                : null;
            hoverPreviewState = hoverPreviewCoordinator.Resolve(entry);
            hoverPreviewUrl = hoverPreviewState == HoverPreviewState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/preview"
                : null;
            subtitleState = subtitleCoordinator.Resolve(entry);
            subtitleUrl = subtitleState == SubtitleState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/subtitle"
                : null;
        }

        return new ArchiveItemDto(
            item.Id,
            item.Name,
            item.Kind,
            item.Extension,
            item.SizeBytes,
            item.LastWriteTimeUtc,
            item.IsVideo,
            thumbnailState,
            thumbnailUrl,
            hoverPreviewState,
            hoverPreviewUrl,
            subtitleState,
            subtitleUrl,
            IsMusic: item.IsMusic,
            AudioUrl: AudioUrl(item),
            AlbumCoverUrl: AlbumCoverUrl(item),
            IsComic: item.IsComic,
            HasPlayableMedia: item.HasPlayableMedia,
            IsConvertibleVideo: item.IsConvertibleVideo);
    }

    private static async Task<ArchiveItemDto> ToDtoAsync(
        ArchiveItemEntry item,
        ThumbnailCoordinator thumbnailCoordinator,
        HoverPreviewCoordinator hoverPreviewCoordinator,
        SubtitleCoordinator subtitleCoordinator,
        VideoMetadataCoordinator metadataCoordinator,
        IEpubBookService epubBookService,
        CancellationToken cancellationToken)
    {
        if (!item.IsVideo && !item.IsMusic)
        {
            return ToDto(item, epubBookService);
        }

        var entry = ToMediaEntry(item);
        var thumbnailState = ThumbnailState.Unavailable;
        string? thumbnailUrl = null;
        var hoverPreviewState = HoverPreviewState.Unavailable;
        string? hoverPreviewUrl = null;
        var subtitleState = SubtitleState.Unavailable;
        string? subtitleUrl = null;
        if (item.IsVideo)
        {
            thumbnailState = thumbnailCoordinator.Resolve(entry);
            thumbnailUrl = thumbnailState == ThumbnailState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/thumbnail"
                : null;
            hoverPreviewState = hoverPreviewCoordinator.Resolve(entry);
            hoverPreviewUrl = hoverPreviewState == HoverPreviewState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/preview"
                : null;
            subtitleState = subtitleCoordinator.Resolve(entry);
            subtitleUrl = subtitleState == SubtitleState.Ready
                ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/subtitle"
                : null;
        }

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

        return new ArchiveItemDto(
            item.Id,
            item.Name,
            item.Kind,
            item.Extension,
            item.SizeBytes,
            item.LastWriteTimeUtc,
            item.IsVideo,
            thumbnailState,
            thumbnailUrl,
            hoverPreviewState,
            hoverPreviewUrl,
            subtitleState,
            subtitleUrl,
            metadata.Duration?.TotalSeconds,
            item.IsVideo ? metadata.Width : null,
            item.IsVideo ? metadata.Height : null,
            item.IsMusic,
            AudioUrl(item),
            AlbumCoverUrl(item),
            IsComic: item.IsComic,
            HasPlayableMedia: item.HasPlayableMedia,
            IsConvertibleVideo: item.IsConvertibleVideo);
    }

    private static string? AudioUrl(ArchiveItemEntry item) =>
        item.IsMusic
            ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/audio"
            : null;

    private static string? ImageUrl(ArchiveItemEntry item) =>
        item.IsImage
            ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/image"
            : null;

    private static string? PdfUrl(ArchiveItemEntry item) =>
        item.IsPdfDocument
            ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/pdf"
            : null;

    private static string BookCoverUrl(ArchiveItemEntry item) =>
        $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.Id)}/book/cover";

    private static string? AlbumCoverUrl(ArchiveItemEntry item) =>
        item.IsMusic && !string.IsNullOrWhiteSpace(item.AlbumCoverId)
            ? $"/api/archive/{Uri.EscapeDataString(item.Category.Key)}/items/{Uri.EscapeDataString(item.AlbumCoverId)}/cover"
            : null;

    private static bool TryResolveMediaEntry(
        string category,
        string id,
        IArchiveService archive,
        out VideoFileEntry? entry)
    {
        entry = null;
        if (!archive.TryResolveVideo(category, id, out var item) || item is null)
        {
            return false;
        }

        entry = ToMediaEntry(item);
        return true;
    }

    private static VideoFileEntry ToMediaEntry(ArchiveItemEntry item) =>
        new(
            item.Id,
            item.PhysicalPath,
            $"archive/{item.Category.Key}/{item.Id}/{item.Name}",
            item.Name,
            item.Extension ?? string.Empty,
            item.SizeBytes ?? 0,
            item.LastWriteTimeUtc);

    private static bool IsCategoryRoot(ArchiveListing listing) =>
        listing.ParentFolder is null;

    internal sealed record ImageCropRequest(int X, int Y, int Width, int Height);

    internal sealed record ImageCropResponse(string Id, string Name, string ImageUrl);
}
