using WebApp.Client.Models;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveMetricsService(
    IArchiveService archiveService,
    IActiveClientTracker activeClientTracker,
    IProcessLister processLister,
    IThumbnailJobQueue thumbnailJobQueue,
    IHoverPreviewJobQueue hoverPreviewJobQueue,
    ISubtitleJobQueue subtitleJobQueue,
    ICutJobQueue cutJobQueue,
    ICompositionJobQueue compositionJobQueue,
    IArchiveMutationJobQueue archiveMutationJobQueue,
    IVideoConversionJobQueue? conversionJobQueue = null) : IArchiveMetricsService
{
    private static readonly string[] FfmpegProcessNames = ["ffmpeg", "ffprobe"];

    public DashboardArchiveDto GetArchiveMetrics()
    {
        var files = CountFiles();
        var activeFfmpegProcesses = processLister.GetRunningProcessNames()
            .Count(name => FfmpegProcessNames.Contains(name, StringComparer.OrdinalIgnoreCase));

        return new DashboardArchiveDto(
            files.Total,
            files.Videos,
            files.Audio,
            files.Epubs,
            files.Images,
            files.Pdfs,
            files.TextDocuments,
            files.Other,
            activeClientTracker.CountActive(),
            activeFfmpegProcesses,
            activeFfmpegProcesses,
            thumbnailJobQueue.ActiveCount,
            hoverPreviewJobQueue.ActiveCount,
            subtitleJobQueue.ActiveCount,
            cutJobQueue.ActiveCount,
            compositionJobQueue.ActiveCount,
            conversionJobQueue?.ActiveCount ?? 0,
            archiveMutationJobQueue.ActiveCount);
    }

    private ArchiveFileCounts CountFiles()
    {
        var counts = new ArchiveFileCounts();
        foreach (var category in ArchiveCategory.Defaults)
        {
            try
            {
                var root = archiveService.GetCategoryRootPath(category.Key);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                    IgnoreInaccessible = true,
                }))
                {
                    counts.Add(Path.GetExtension(path));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Skip categories that cannot be read.
            }
        }

        return counts;
    }

    private sealed class ArchiveFileCounts
    {
        private static readonly HashSet<string> VideoExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".m4v" };
        private static readonly HashSet<string> AudioExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a" };
        private static readonly HashSet<string> ImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".ico" };
        private static readonly HashSet<string> EpubExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".epub" };
        private static readonly HashSet<string> PdfExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".pdf" };
        private static readonly HashSet<string> TextDocumentExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

        public int Videos { get; private set; }
        public int Audio { get; private set; }
        public int Epubs { get; private set; }
        public int Images { get; private set; }
        public int Pdfs { get; private set; }
        public int TextDocuments { get; private set; }
        public int Other { get; private set; }
        public int Total => Videos + Audio + Epubs + Images + Pdfs + TextDocuments + Other;

        public void Add(string extension)
        {
            if (VideoExtensions.Contains(extension))
            {
                Videos++;
            }
            else if (AudioExtensions.Contains(extension))
            {
                Audio++;
            }
            else if (EpubExtensions.Contains(extension))
            {
                Epubs++;
            }
            else if (ImageExtensions.Contains(extension))
            {
                Images++;
            }
            else if (PdfExtensions.Contains(extension))
            {
                Pdfs++;
            }
            else if (TextDocumentExtensions.Contains(extension))
            {
                TextDocuments++;
            }
            else
            {
                Other++;
            }
        }
    }
}
