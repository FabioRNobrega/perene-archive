using WebApp.Client.Pages;
using WebApp.Components;
using WebApp.Configuration;
using WebApp.Endpoints;
using WebApp.Services;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();
builder.Services.AddOptions<VideoLibraryOptions>()
    .Bind(builder.Configuration.GetSection(VideoLibraryOptions.SectionName))
    .Validate(VideoLibraryOptions.HasConfiguredPath, "VideoLibrary:Path is required.")
    .Validate(VideoLibraryOptions.HasAbsolutePath, "VideoLibrary:Path must be absolute.")
    .Validate(VideoLibraryOptions.DirectoryExists, "VideoLibrary:Path must identify an existing directory.")
    .Validate(VideoLibraryOptions.DirectoryIsReadable, "VideoLibrary:Path must identify a readable directory.")
    .ValidateOnStart();
builder.Services.AddOptions<ArchiveRootOptions>()
    .Bind(builder.Configuration.GetSection(ArchiveRootOptions.SectionName))
    .Validate(ArchiveRootOptions.HasConfiguredPath, "ArchiveRoot:Path is required.")
    .Validate(ArchiveRootOptions.HasAbsolutePath, "ArchiveRoot:Path must be absolute.")
    .Validate(ArchiveRootOptions.DirectoryExists, "ArchiveRoot:Path must identify an existing directory.")
    .Validate(ArchiveRootOptions.DirectoryIsReadable, "ArchiveRoot:Path must identify a readable directory.")
    .Validate(ArchiveRootOptions.DirectoryIsWritable, "ArchiveRoot:Path must identify a writable directory.")
    .Validate(ArchiveRootOptions.DefaultCategoriesExistOrCanBeCreated, "ArchiveRoot:Path must contain or allow creation of default category folders.")
    .ValidateOnStart();
builder.Services.AddOptions<ThumbnailCacheOptions>()
    .Bind(builder.Configuration.GetSection(ThumbnailCacheOptions.SectionName))
    .Validate(ThumbnailCacheOptions.HasConfiguredPath, "ThumbnailCache:Path is required.")
    .Validate(ThumbnailCacheOptions.HasAbsolutePath, "ThumbnailCache:Path must be absolute.")
    .Validate(ThumbnailCacheOptions.DirectoryExists, "ThumbnailCache:Path must identify an existing directory.")
    .Validate(ThumbnailCacheOptions.DirectoryIsWritable, "ThumbnailCache:Path must identify a writable directory.")
    .Validate(
        options => ThumbnailCacheOptions.IsDisjointFromVideoRoot(
            options, builder.Configuration[$"{VideoLibraryOptions.SectionName}:Path"]),
        "ThumbnailCache:Path must not overlap VideoLibrary:Path.")
    .ValidateOnStart();
builder.Services.AddOptions<VideoCutOptions>()
    .Bind(builder.Configuration.GetSection(VideoCutOptions.SectionName))
    .Validate(VideoCutOptions.HasConfiguredPath, "VideoCut:Path is required.")
    .Validate(VideoCutOptions.HasAbsolutePath, "VideoCut:Path must be absolute.")
    .Validate(VideoCutOptions.DirectoryExists, "VideoCut:Path must identify an existing directory.")
    .Validate(VideoCutOptions.DirectoryIsWritable, "VideoCut:Path must identify a writable directory.")
    .Validate(VideoCutOptions.HasPositiveQueueCapacity, "VideoCut:QueueCapacity must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<VideoCompositionOptions>()
    .Bind(builder.Configuration.GetSection(VideoCompositionOptions.SectionName))
    .Validate(VideoCompositionOptions.HasConfiguredPath, "VideoComposition:Path is required.")
    .Validate(VideoCompositionOptions.HasAbsolutePath, "VideoComposition:Path must be absolute.")
    .Validate(VideoCompositionOptions.DirectoryExists, "VideoComposition:Path must identify an existing directory.")
    .Validate(VideoCompositionOptions.DirectoryIsWritable, "VideoComposition:Path must identify a writable directory.")
    .Validate(VideoCompositionOptions.HasPositiveQueueCapacity, "VideoComposition:QueueCapacity must be greater than zero.")
    .Validate(VideoCompositionOptions.HasPositiveTransitionDuration, "VideoComposition:TransitionDurationSeconds must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<VideoConversionOptions>()
    .Bind(builder.Configuration.GetSection(VideoConversionOptions.SectionName))
    .Validate(VideoConversionOptions.IsValid, "VideoConversion options are invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<ArchiveUploadOptions>()
    .Bind(builder.Configuration.GetSection(ArchiveUploadOptions.SectionName))
    .Validate(ArchiveUploadOptions.HasPositiveTtl, "ArchiveUpload:SessionTtlHours must be greater than zero.")
    .Validate(ArchiveUploadOptions.HasPositiveCleanupInterval, "ArchiveUpload:CleanupIntervalMinutes must be greater than zero.")
    .Validate(ArchiveUploadOptions.HasValidMaxSize, "ArchiveUpload:MaxDeclaredSizeBytes must be at least 10 GB.")
    .Validate(ArchiveUploadOptions.HasPositiveChunkSizes, "ArchiveUpload chunk sizes must be positive and strictly increasing.")
    .Validate(ArchiveUploadOptions.HasIncreasingThresholds, "ArchiveUpload thresholds must be positive, strictly increasing, and within the maximum size.")
    .ValidateOnStart();
builder.Services.AddOptions<HoverPreviewOptions>()
    .Bind(builder.Configuration.GetSection(HoverPreviewOptions.SectionName))
    .Validate(HoverPreviewOptions.HasPositiveWidth, "HoverPreview:Width must be greater than zero.")
    .Validate(HoverPreviewOptions.HasPositiveFrameRate, "HoverPreview:FrameRate must be greater than zero.")
    .Validate(HoverPreviewOptions.HasPositiveSegmentSeconds, "HoverPreview:SegmentSeconds must be greater than zero.")
    .Validate(HoverPreviewOptions.HasPositiveQueueCapacity, "HoverPreview:QueueCapacity must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<KestrelHttpsOptions>()
    .Bind(builder.Configuration.GetSection(KestrelHttpsOptions.SectionName))
    .Validate(KestrelHttpsOptions.HasPositivePort, "HttpsCertificate:Port must be greater than zero.")
    .Validate(KestrelHttpsOptions.HasPasswordWhenPathConfigured, "HttpsCertificate:Password is required when HttpsCertificate:Path is set.")
    .ValidateOnStart();
builder.WebHost.ConfigureKestrel((context, serverOptions) =>
    KestrelHttpsEndpointConfigurator.TryConfigure(
        serverOptions,
        context.Configuration.GetSection(KestrelHttpsOptions.SectionName).Get<KestrelHttpsOptions>() ?? new()));
// Explicitly tells UseHttpsRedirection which port to redirect to: Kestrel's own
// IServerAddressesFeature detection is unreliable behind Docker's port mapping and is not
// populated at all by WebApplicationFactory's in-memory TestServer, so without this the
// redirect silently no-ops even when the HTTPS endpoint above is enabled.
builder.Services.Configure<Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionOptions>(options =>
{
    var httpsOptions = builder.Configuration.GetSection(KestrelHttpsOptions.SectionName).Get<KestrelHttpsOptions>() ?? new();
    if (KestrelHttpsOptions.IsEnabled(httpsOptions))
    {
        options.HttpsPort = httpsOptions.Port;
    }
});
builder.Services.AddSingleton<IVideoLibraryService, VideoLibraryService>();
builder.Services.AddSingleton<ThumbnailCache>();
builder.Services.AddSingleton<ThumbnailCoordinator>();
builder.Services.AddSingleton<IThumbnailJobQueue, ThumbnailJobQueue>();
builder.Services.AddSingleton<IVideoDurationProbe, FfprobeDurationProbe>();
builder.Services.AddSingleton<IVideoResolutionProbe, FfprobeResolutionProbe>();
builder.Services.AddSingleton<VideoMetadataCoordinator>();
builder.Services.AddSingleton<IThumbnailGenerator, FfmpegThumbnailGenerator>();
builder.Services.AddHostedService<ThumbnailBackgroundWorker>();
builder.Services.AddSingleton<HoverPreviewCache>();
builder.Services.AddSingleton<HoverPreviewCoordinator>();
builder.Services.AddSingleton<IHoverPreviewJobQueue, HoverPreviewJobQueue>();
builder.Services.AddSingleton<IHoverPreviewGenerator, FfmpegHoverPreviewGenerator>();
builder.Services.AddHostedService<HoverPreviewBackgroundWorker>();
builder.Services.AddSingleton<SubtitleMatcher>();
builder.Services.AddSingleton<SubtitleCache>();
builder.Services.AddSingleton<SubtitleCoordinator>();
builder.Services.AddSingleton<ISubtitleJobQueue, SubtitleJobQueue>();
builder.Services.AddSingleton<ISubtitleGenerator, FfmpegSubtitleGenerator>();
builder.Services.AddHostedService<SubtitleBackgroundWorker>();
builder.Services.AddSingleton<IVideoCutService, VideoCutService>();
builder.Services.AddSingleton<CutNamingService>();
builder.Services.AddSingleton<ICutJobQueue, CutJobQueue>();
builder.Services.AddSingleton<ICutGenerator, FfmpegCutGenerator>();
builder.Services.AddHostedService<CutBackgroundWorker>();
builder.Services.AddSingleton<IVideoCompositionService, VideoCompositionService>();
builder.Services.AddSingleton<CompositionNamingService>();
builder.Services.AddSingleton<ICompositionJobQueue, CompositionJobQueue>();
builder.Services.AddSingleton<ICompositionJobStatusStore, CompositionJobStatusStore>();
builder.Services.AddSingleton<IVideoCompositionProbe, FfprobeCompositionProbe>();
builder.Services.AddSingleton<ICompositionGenerator, FfmpegCompositionGenerator>();
builder.Services.AddHostedService<CompositionBackgroundWorker>();
builder.Services.AddSingleton<IVideoConversionProbe, FfprobeVideoConversionProbe>();
builder.Services.AddSingleton<MediaConversionPlanner>();
builder.Services.AddSingleton<VideoConversionNamingService>();
builder.Services.AddSingleton<IVideoConversionJobQueue, VideoConversionJobQueue>();
builder.Services.AddSingleton<IVideoConversionJobStatusStore, VideoConversionJobStatusStore>();
builder.Services.AddSingleton<IVideoConversionGenerator, FfmpegVideoConversionGenerator>();
builder.Services.AddHostedService<VideoConversionBackgroundWorker>();
builder.Services.AddSingleton<IStorageUsageService, StorageUsageService>();
builder.Services.AddSingleton<IArchiveService, ArchiveService>();
builder.Services.AddSingleton<IArchiveUploadService, ArchiveUploadService>();
builder.Services.AddHostedService<ArchiveUploadCleanupWorker>();
builder.Services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
builder.Services.AddSingleton<INetworkMetricsService, NetworkMetricsService>();
builder.Services.AddSingleton<IActiveClientTracker, ActiveClientTracker>();
builder.Services.AddSingleton<IProcessLister, SystemProcessLister>();
builder.Services.AddSingleton<IArchiveMetricsService, ArchiveMetricsService>();
builder.Services.AddSingleton<IDockerApiClient, DockerEngineApiClient>();
builder.Services.AddSingleton<IDockerMetricsService, DockerMetricsService>();
builder.Services.AddSingleton<IFfmpegAvailabilityProbe, FfmpegAvailabilityProbe>();
builder.Services.AddSingleton<IHealthAggregationService, HealthAggregationService>();
builder.Services.AddSingleton<IAlertEvaluationService, AlertEvaluationService>();
builder.Services.AddSingleton<MetricsHistoryBackgroundWorker>();
builder.Services.AddSingleton<IMetricsHistoryService>(sp => sp.GetRequiredService<MetricsHistoryBackgroundWorker>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MetricsHistoryBackgroundWorker>());
builder.Services.AddSingleton<ImageCropNamingService>();
builder.Services.AddSingleton<IImageCropGenerator, ImageSharpCropGenerator>();
builder.Services.AddSingleton<IImageCropService, ImageCropService>();
builder.Services.AddSingleton<IEpubContentSanitizer, EpubContentSanitizer>();
builder.Services.AddSingleton<IEpubBookService, EpubBookService>();
builder.Services.AddSingleton<IEpubNoteService, EpubNoteService>();
builder.Services.AddSingleton<IEpubProgressService, EpubProgressService>();
builder.Services.AddSingleton<IEpubHighlightService, EpubHighlightService>();
builder.Services.AddSingleton<ITextDocumentService, TextDocumentService>();
builder.Services.AddSingleton<ITextDocumentPdfExporter, TextDocumentPdfExporter>();

var app = builder.Build();
var configuredAllowedHosts = builder.Configuration["AllowedNetworkHosts:Hosts"]?
    .Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
var allowedHostSet = new HashSet<string>(configuredAllowedHosts, StringComparer.OrdinalIgnoreCase)
{
    "localhost",
    "127.0.0.1",
    "::1",
    "[::1]"
};

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (!string.IsNullOrWhiteSpace(host) && !allowedHostSet.Contains(host))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? context.Request.Host.Value ?? string.Empty;
        context.RequestServices.GetRequiredService<IActiveClientTracker>().Track(clientId);
    }

    await next();
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapVideoEndpoints();
app.MapCutEndpoints();
app.MapCompositionEndpoints();
app.MapStorageEndpoints();
app.MapArchiveEndpoints();
app.MapDashboardEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

app.Run();

public partial class Program;
