using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Data.Entities;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Endpoints;

/// <summary>Retry and "Started by" on the conversion jobs list, on a real-Identity host with a real (ffmpeg-generated) source.</summary>
public sealed class ConversionJobRetryEndpointTests : IDisposable
{
    private readonly MediaTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string VideoPath => Path.Combine(_host.ArchivePath, "Videos", "sample.mkv");

    private static async Task CreateVideoAsync(string path)
    {
        var info = new ProcessStartInfo { FileName = "ffmpeg", UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc=size=640x360:rate=15:duration=1", "-pix_fmt", "yuv420p", path })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>Records a Failed conversion job owned by <paramref name="ownerId"/> the way the create endpoint would have.</summary>
    private async Task<string> SeedFailedJobAsync(string ownerId, bool withSelection = true, VideoConversionJobState finalState = VideoConversionJobState.Failed)
    {
        if (!File.Exists(VideoPath)) await CreateVideoAsync(VideoPath);
        var jobId = Guid.NewGuid().ToString("N");
        await _host.AsAsync(ownerId, async sp =>
        {
            var archive = sp.GetRequiredService<IArchiveService>();
            Assert.True(archive.TryResolveConvertibleVideo("videos", _host.ItemId(sp, "videos", "sample.mkv"), out var entry));
            var probe = new VideoConversionProbeResult("matroska", "h264", null, null, 640, 360, TimeSpan.FromSeconds(1));
            var job = new VideoConversionJob(jobId, entry!, MediaAction.FullTranscode, probe, null, ownerId);
            var payload = await sp.GetRequiredService<JobEnqueueService>().BuildConversionPayloadAsync(job, withSelection ? new VideoConversionSelectionDto("compatible") : null);
            var store = _host.Factory.Services.GetRequiredService<IVideoConversionJobStatusStore>();
            store.Seed(job, payload);
            if (finalState == VideoConversionJobState.Failed) store.Fail(jobId, "Interrupted by a restart.");
            if (finalState == VideoConversionJobState.Stopped) { store.Processing(jobId); store.Stop(jobId); }
        });
        return jobId;
    }

    private async Task<HttpClient> SignedInAsync(string name)
    {
        var client = _host.Factory.CreateClient(IdentityTestHost.NoRedirect);
        await IdentityTestHost.SignInAsync(client, name, "password1");
        return client;
    }

    [Fact]
    public async Task Owner_can_retry_a_failed_job_which_restarts_the_same_job_with_the_same_selection()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        var oldId = await SeedFailedJobAsync(owner);
        using var client = await SignedInAsync("owner");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{oldId}/retry");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<VideoConversionJobDto>())!;
        Assert.Equal(oldId, created.JobId);
        Assert.NotEqual("Interrupted by a restart.", created.Diagnostic);
        await _host.WithDbAsync(async (_, db) =>
        {
            // The same row was reused: still exactly one job, no longer Failed, owned by the retrier, selection kept, no paths.
            var row = await db.Jobs.SingleAsync();
            Assert.Equal(oldId, row.Id);
            Assert.NotEqual("Failed", row.State);
            Assert.Equal(owner, row.UserId);
            Assert.Contains("compatible", row.PayloadJson);
            Assert.DoesNotContain("sample", row.PayloadJson);
        });
    }

    [Fact]
    public async Task Only_failed_or_stopped_jobs_can_be_retried()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        var pendingId = await SeedFailedJobAsync(owner, finalState: VideoConversionJobState.Pending);
        using var client = await SignedInAsync("owner");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{pendingId}/retry");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Another_member_cannot_see_or_retry_the_job_but_an_admin_can()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        await _host.AddUserAsync("bob");
        await _host.AddUserAsync("boss", admin: true);
        var oldId = await SeedFailedJobAsync(owner);

        using var bob = await SignedInAsync("bob");
        using var denied = await IdentityTestHost.SendWithTokenAsync(bob, HttpMethod.Post, $"/api/dashboard/jobs/{oldId}/retry");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var boss = await SignedInAsync("boss");
        using var allowed = await IdentityTestHost.SendWithTokenAsync(boss, HttpMethod.Post, $"/api/dashboard/jobs/{oldId}/retry");
        Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    }

    [Fact]
    public async Task A_job_without_a_stored_selection_or_with_a_missing_source_is_refused()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        var legacyId = await SeedFailedJobAsync(owner, withSelection: false);
        var goneId = await SeedFailedJobAsync(owner);
        using var client = await SignedInAsync("owner");

        using var legacy = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{legacyId}/retry");
        Assert.Equal(HttpStatusCode.Conflict, legacy.StatusCode);

        File.Delete(VideoPath);
        using var gone = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{goneId}/retry");
        Assert.Equal(HttpStatusCode.Conflict, gone.StatusCode);
    }

    [Fact]
    public async Task Jobs_list_shows_the_starters_display_name_and_removed_account_after_deletion()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        await _host.AddUserAsync("boss", admin: true);
        var jobId = await SeedFailedJobAsync(owner);
        await _host.WithDbAsync(async (_, db) =>
        {
            (await db.Users.SingleAsync(user => user.Id == owner)).DisplayName = "Olivia Owner";
            await db.SaveChangesAsync();
        });
        using var boss = await SignedInAsync("boss");

        var listed = (await boss.GetFromJsonAsync<List<VideoConversionJobDto>>("/api/dashboard/jobs"))!.Single(job => job.JobId == jobId);
        Assert.Equal("Olivia Owner", listed.StartedBy);

        await _host.WithDbAsync(async (_, db) =>
        {
            db.Users.Remove(await db.Users.SingleAsync(user => user.Id == owner));
            await db.SaveChangesAsync();
        });
        var afterDelete = (await boss.GetFromJsonAsync<List<VideoConversionJobDto>>("/api/dashboard/jobs"))!.Single(job => job.JobId == jobId);
        Assert.Equal("Removed account", afterDelete.StartedBy);
    }

    [Fact]
    public async Task A_stopped_job_can_be_retried()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        var stoppedId = await SeedFailedJobAsync(owner, finalState: VideoConversionJobState.Stopped);
        using var client = await SignedInAsync("owner");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{stoppedId}/retry");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(stoppedId, (await response.Content.ReadFromJsonAsync<VideoConversionJobDto>())!.JobId);
    }

    [Fact]
    public async Task Retry_is_refused_while_another_conversion_of_the_same_file_is_active()
    {
        var owner = await _host.AddUserAsync("owner", admin: true);
        var failedId = await SeedFailedJobAsync(owner);
        await SeedFailedJobAsync(owner, finalState: VideoConversionJobState.Pending);
        using var client = await SignedInAsync("owner");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{failedId}/retry");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await _host.WithDbAsync(async (_, db) => Assert.Equal("Failed", (await db.Jobs.SingleAsync(job => job.Id == failedId)).State));
    }

    [Fact]
    public async Task Retry_is_refused_when_the_owner_has_lost_access_to_the_source_folder()
    {
        var bob = await _host.AddUserAsync("bob");
        var jobId = await SeedFailedJobAsync(bob);
        await _host.WithDbAsync(async (provider, db) =>
        {
            var library = await db.Folders.SingleAsync(folder => folder.RootKey == WebApp.Authorization.FolderLocator.LibraryRootKey && folder.RelativePath == "");
            library.AccessMode = FolderAccessMode.Private;
            await db.SaveChangesAsync();
            await provider.GetRequiredService<AuthzVersionStore>().BumpGlobalAsync();
        });
        using var client = await SignedInAsync("bob");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, $"/api/dashboard/jobs/{jobId}/retry");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _host.WithDbAsync(async (_, db) => Assert.Equal("Failed", (await db.Jobs.SingleAsync(job => job.Id == jobId)).State));
    }

    [Fact]
    public async Task Retry_of_an_unknown_job_is_not_found()
    {
        await _host.AddUserAsync("owner", admin: true);
        using var client = await SignedInAsync("owner");

        using var response = await IdentityTestHost.SendWithTokenAsync(client, HttpMethod.Post, "/api/dashboard/jobs/does-not-exist/retry");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Composition_job_list_shows_members_only_their_own_jobs_and_admins_all()
    {
        var bob = await _host.AddUserAsync("bob");
        var carol = await _host.AddUserAsync("carol");
        await _host.AddUserAsync("boss", admin: true);
        var store = _host.Factory.Services.GetRequiredService<ICompositionJobStatusStore>();
        store.Seed("bob-job", bob);
        store.Seed("carol-job", carol);

        using var bobClient = await SignedInAsync("bob");
        var mine = (await bobClient.GetFromJsonAsync<List<CompositionJobDto>>("/api/compositions/jobs"))!;
        Assert.Equal("bob-job", Assert.Single(mine).JobId);

        using var boss = await SignedInAsync("boss");
        var all = (await boss.GetFromJsonAsync<List<CompositionJobDto>>("/api/compositions/jobs"))!;
        Assert.Equal(["bob-job", "carol-job"], all.Select(job => job.JobId).OrderBy(id => id).ToArray());
    }
}
