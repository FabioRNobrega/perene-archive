namespace WebApp.Client.Models;

public sealed record DashboardArchiveDto(
    int TotalFiles,
    int VideoFiles,
    int AudioFiles,
    int EpubFiles,
    int ImageFiles,
    int PdfFiles,
    int TextDocumentFiles,
    int OtherFiles,
    int ActiveUsers,
    int ActiveMediaStreams,
    int ActiveFfmpegProcesses,
    int QueuedThumbnailJobs,
    int QueuedHoverPreviewJobs,
    int QueuedSubtitleJobs,
    int QueuedCutJobs,
    int QueuedCompositionJobs,
    int QueuedConversionJobs = 0,
    int QueuedArchiveMutationJobs = 0);
