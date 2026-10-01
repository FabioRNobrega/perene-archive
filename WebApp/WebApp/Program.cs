using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebApp.Client.Pages;
using WebApp.Components;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Endpoints;
using WebApp.Identity;
using WebApp.Security;
using WebApp.Services;

// Offline operator commands run before any host exists: they never migrate, seed, or listen.
if (AdminCli.IsCommand(args))
{
    return await AdminCli.RunAsync(args);
}

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(DatabaseOptions.IsValid, "Database:Path must be an absolute path to a file in an existing writable directory.")
    .Validate(
        options => DatabaseOptions.IsDisjointFromRoots(options,
        [
            builder.Configuration[$"{ArchiveRootOptions.SectionName}:Path"],
            builder.Configuration[$"{VideoLibraryOptions.SectionName}:Path"],
            builder.Configuration[$"{ThumbnailCacheOptions.SectionName}:Path"],
            builder.Configuration[$"{VideoCutOptions.SectionName}:Path"],
            builder.Configuration[$"{VideoCompositionOptions.SectionName}:Path"]
        ]),
        "Database:Path must not be inside the archive, cache, or output directories.")
    .ValidateOnStart();
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .Validate(AuthOptions.HasPositiveTemporaryPasswordDays, "Auth:TemporaryPasswordDays must be greater than zero.")
    .Validate(AuthOptions.HasPositiveValidationInterval, "Auth:SecurityStampValidationMinutes must be greater than zero.")
    .ValidateOnStart();
var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={databaseOptions.Path};Foreign Keys=True;Default Timeout=5")
        .AddInterceptors(new SqliteConnectionInterceptor()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
        // The local bootstrap account is explicitly requested as admin/admin and must change it at first sign-in.
        options.Password.RequiredLength = 5;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/account/login";
    options.SlidingExpiration = true;
    // Unauthenticated API calls get a plain 401/403 instead of a redirect to the login page.
    options.Events.OnRedirectToLogin = context => RedirectOrStatus(context, StatusCodes.Status401Unauthorized);
    options.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context, StatusCodes.Status403Forbidden);
});
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(authOptions.SecurityStampValidationMinutes));
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser().Build());
builder.Services.AddAntiforgery(options => options.HeaderName = AntiforgeryEndpointFilter.HeaderName);
builder.Services.AddScoped<ApplicationSignInManager>();
builder.Services.AddScoped<AccountLifecycleService>();
builder.Services.AddPereneDataProtection(builder.Configuration);
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
builder.Services.AddOptions<ComicReaderOptions>()
    .Bind(builder.Configuration.GetSection(ComicReaderOptions.SectionName))
    .Validate(ComicReaderOptions.IsValid, "ComicReader limits must be positive and the aggregate limit must be at least one page.")
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
builder.Services.AddSingleton<IVideoAudioTrackProbe, FfprobeAudioTrackProbe>();
builder.Services.AddSingleton<AudioTrackCache>();
builder.Services.AddSingleton<IAudioTrackRemuxer, FfmpegAudioTrackRemuxer>();
builder.Services.AddSingleton<AudioTrackRemuxService>();
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
builder.Services.AddSingleton<ConversionEstimateCalculator>();
builder.Services.AddSingleton<ConversionProfileCatalog>();
builder.Services.AddSingleton<ConversionProfileResolver>();
builder.Services.AddSingleton<VideoConversionArgumentBuilder>();
builder.Services.AddSingleton<VideoConversionNamingService>();
builder.Services.AddSingleton<IVideoConversionJobQueue, VideoConversionJobQueue>();
builder.Services.AddSingleton<IVideoConversionJobStatusStore, VideoConversionJobStatusStore>();
builder.Services.AddSingleton<IVideoConversionProcessSignal, PosixVideoConversionProcessSignal>();
builder.Services.AddSingleton<IVideoConversionProcessController, VideoConversionProcessController>();
builder.Services.AddSingleton<IVideoConversionGenerator, FfmpegVideoConversionGenerator>();
builder.Services.AddHostedService<VideoConversionBackgroundWorker>();
builder.Services.AddSingleton<IStorageUsageService, StorageUsageService>();
builder.Services.AddSingleton<IArchiveService, ArchiveService>();
builder.Services.AddSingleton<IArchiveFavoritesService, ArchiveFavoritesService>();
builder.Services.AddSingleton<IArchiveMutationJobQueue, ArchiveMutationJobQueue>();
builder.Services.AddSingleton<IArchiveMutationJobStatusStore, ArchiveMutationJobStatusStore>();
builder.Services.AddSingleton<IArchiveMutationExecutor, ArchiveMutationExecutor>();
builder.Services.AddHostedService<ArchiveMutationBackgroundWorker>();
builder.Services.AddSingleton<IComicBookService, ComicBookService>();
builder.Services.AddSingleton<IArchiveDownloadService, ArchiveDownloadService>();
builder.Services.AddSingleton<IArchiveUploadService, ArchiveUploadService>();
builder.Services.AddHostedService<ArchiveUploadCleanupWorker>();
builder.Services.AddSingleton<ICustomStorageViewService, CustomStorageViewService>();
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
builder.Services.AddSingleton<IFolderThumbnailProcessor, FolderThumbnailProcessor>();
builder.Services.AddSingleton<IEpubContentSanitizer, EpubContentSanitizer>();
builder.Services.AddSingleton<IEpubBookService, EpubBookService>();
builder.Services.AddSingleton<IEpubNoteService, EpubNoteService>();
builder.Services.AddSingleton<IEpubProgressService, EpubProgressService>();
builder.Services.AddSingleton<IEpubReaderThemeService, EpubReaderThemeService>();
builder.Services.AddSingleton<IComicProgressService, ComicProgressService>();
builder.Services.AddSingleton<IEpubHighlightService, EpubHighlightService>();
builder.Services.AddSingleton<ITextDocumentService, TextDocumentService>();
builder.Services.AddSingleton<ITextDocumentPdfExporter, TextDocumentPdfExporter>();

var app = builder.Build();
await StartupInitializer.InitializeAsync(app.Services);
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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapAccountEndpoints();
// One group filter validates the antiforgery token on every unsafe method of every API endpoint below.
var protectedApi = app.MapGroup(string.Empty).AddEndpointFilter<AntiforgeryEndpointFilter>();
protectedApi.MapVideoEndpoints();
protectedApi.MapCutEndpoints();
protectedApi.MapCompositionEndpoints();
protectedApi.MapStorageEndpoints();
protectedApi.MapArchiveEndpoints();
protectedApi.MapDashboardEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(WebApp.Client._Imports).Assembly);

app.Run();
return 0;

static Task RedirectOrStatus(Microsoft.AspNetCore.Authentication.RedirectContext<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> context, int status)
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = status;
    }
    else
    {
        context.Response.Redirect(context.RedirectUri);
    }

    return Task.CompletedTask;
}

public partial class Program;
