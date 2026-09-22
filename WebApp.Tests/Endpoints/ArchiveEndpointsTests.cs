using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;
using WebApp.Tests.Services;

namespace WebApp.Tests.Endpoints;

public sealed class ArchiveEndpointsTests
{
    [Fact]
    public async Task Download_endpoint_returns_files_with_attachment_ranges_and_folders_as_streamed_zips()
    {
        using var root = CreateArchive();
        var source = "download me"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Downloads", "receipt.bin"), source);
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Reports", "Empty"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Downloads", "Reports", "q1.txt"), "quarter one");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var file = listing.Items.Single(item => item.Name == "receipt.bin");
        var folder = listing.Items.Single(item => item.Name == "Reports");

        using var fileResponse = await client.GetAsync($"/api/archive/downloads/items/{file.Id}/download");
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Contains("attachment", fileResponse.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("receipt.bin", fileResponse.Content.Headers.ContentDisposition.ToString());
        Assert.Equal(source, await fileResponse.Content.ReadAsByteArrayAsync());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/archive/downloads/items/{file.Id}/download");
        rangeRequest.Headers.Range = new RangeHeaderValue(0, 3);
        using var rangeResponse = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(source[..4], await rangeResponse.Content.ReadAsByteArrayAsync());

        using var folderResponse = await client.GetAsync($"/api/archive/downloads/items/{folder.Id}/download");
        Assert.Equal(HttpStatusCode.OK, folderResponse.StatusCode);
        Assert.Equal("application/zip", folderResponse.Content.Headers.ContentType!.MediaType);
        Assert.Contains("Reports.zip", folderResponse.Content.Headers.ContentDisposition!.ToString());
        await using var zipBytes = new MemoryStream(await folderResponse.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(zipBytes, ZipArchiveMode.Read);
        Assert.Equal(["Reports/", "Reports/Empty/", "Reports/q1.txt"], zip.Entries.Select(entry => entry.FullName).Order());
        Assert.DoesNotContain(root.Path, await folderResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Download_endpoint_supports_trash_and_rejects_invalid_or_cross_category_ids_without_path_disclosure()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Trash", "removed.txt"), "gone");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Downloads", "other.txt"), "other");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var trash = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/trash/items"))!;
        var downloads = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/archive/trash/items/{trash.Items.Single().Id}/download")).StatusCode);
        foreach (var url in new[]
        {
            "/api/archive/downloads/items/unknown/download",
            "/api/archive/downloads/items/%2Fetc%2Fpasswd/download",
            $"/api/archive/documents/items/{downloads.Items.Single().Id}/download"
        })
        {
            using var response = await client.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(root.Path, body);
        }
    }

    [Fact]
    public async Task Listing_returns_browser_safe_category_items()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Trips"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Pictures", "photo.jpg"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/archive/photos/items");
        var json = await response.Content.ReadAsStringAsync();
        var listing = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal("Photos", listing!.DisplayName);
        Assert.Contains(listing.Items, item => item.Name == "Trips" && item.Kind == ArchiveItemKind.Folder);
        Assert.Contains(listing.Items, item => item.Name == "photo.jpg" && item.Kind == ArchiveItemKind.File);
    }

    [Fact]
    public async Task Listing_marks_folders_with_nested_playable_media_without_exposing_paths()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Trip", "Raw"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Videos", "Trip", "Raw", "clip.mp4"), "content");
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Documents Only"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Videos", "Documents Only", "notes.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/archive/videos/items");
        var json = await response.Content.ReadAsStringAsync();
        var listing = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.True(listing!.Items.Single(item => item.Name == "Trip").HasPlayableMedia);
        Assert.False(listing.Items.Single(item => item.Name == "Documents Only").HasPlayableMedia);
    }

    [Fact]
    public async Task Playlist_endpoint_returns_nested_media_without_exposing_paths()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Trip", "Raw"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Videos", "Trip", "intro.mp4"), "content");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Videos", "Trip", "Raw", "clip.mp4"), "content");
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Videos", "Trip", "notes.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var rootListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/videos/items"))!;
        var folder = rootListing.Items.Single(item => item.Name == "Trip");

        using var response = await client.GetAsync($"/api/archive/videos/items/{folder.Id}/playlist");
        var json = await response.Content.ReadAsStringAsync();
        var playlist = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal(["intro.mp4", "clip.mp4"], playlist!.Items.Select(item => item.Name));
        Assert.All(playlist.Items, item => Assert.True(item.IsVideo));
    }

    [Fact]
    public async Task Listing_returns_archive_video_preview_contract_for_all_categories()
    {
        using var root = CreateArchive();
        var videoPath = Path.Combine(root.Path, "Downloads", "clip.mp4");
        var documentPath = Path.Combine(root.Path, "Downloads", "note.txt");
        byte[] videoFixture = [10, 20, 30, 40, 50];
        await File.WriteAllBytesAsync(videoPath, videoFixture);
        await File.WriteAllTextAsync(documentPath, "content");
        using var factory = new VideoManagerFactory(root.Path, hoverPreviewEnabled: true);
        using var client = factory.CreateClient();

        var pendingListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var pendingVideo = pendingListing.Items.Single(item => item.Name == "clip.mp4");
        var nonVideo = pendingListing.Items.Single(item => item.Name == "note.txt");

        Assert.True(pendingVideo.IsVideo);
        Assert.Equal(ThumbnailState.Pending, pendingVideo.ThumbnailState);
        Assert.Null(pendingVideo.ThumbnailUrl);
        Assert.Equal(HoverPreviewState.Pending, pendingVideo.HoverPreviewState);
        Assert.Null(pendingVideo.HoverPreviewUrl);
        Assert.Equal(SubtitleState.Unavailable, pendingVideo.SubtitleState);
        Assert.Null(pendingVideo.SubtitleUrl);
        Assert.Equal(305, pendingVideo.DurationSeconds);
        Assert.Equal(1920, pendingVideo.Width);
        Assert.Equal(1080, pendingVideo.Height);
        Assert.False(nonVideo.IsVideo);
        Assert.Equal(ThumbnailState.Unavailable, nonVideo.ThumbnailState);
        Assert.Null(nonVideo.ThumbnailUrl);
        Assert.Equal(HoverPreviewState.Unavailable, nonVideo.HoverPreviewState);
        Assert.Null(nonVideo.HoverPreviewUrl);
        Assert.Equal(SubtitleState.Unavailable, nonVideo.SubtitleState);
        Assert.Null(nonVideo.SubtitleUrl);
        Assert.Equal(".txt", nonVideo.Extension);

        var thumbnailCache = new ThumbnailCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var hoverCache = new HoverPreviewCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var timestamp = File.GetLastWriteTimeUtc(videoPath);
        var relativeIdentity = $"archive/downloads/{pendingVideo.Id}/clip.mp4";
        await File.WriteAllBytesAsync(thumbnailCache.GetFinalPath(thumbnailCache.ComputeKey(relativeIdentity, videoFixture.Length, timestamp)), [1, 2, 3]);
        await File.WriteAllBytesAsync(hoverCache.GetFinalPath(hoverCache.ComputeKey(relativeIdentity, videoFixture.Length, timestamp)), videoFixture);

        using var readyResponse = await client.GetAsync("/api/archive/downloads/items");
        var json = await readyResponse.Content.ReadAsStringAsync();
        var readyListing = await readyResponse.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var readyVideo = readyListing!.Items.Single(item => item.Name == "clip.mp4");

        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal(ThumbnailState.Ready, readyVideo.ThumbnailState);
        Assert.Equal(HoverPreviewState.Ready, readyVideo.HoverPreviewState);
        Assert.StartsWith("/api/archive/downloads/items/", readyVideo.ThumbnailUrl);
        Assert.StartsWith("/api/archive/downloads/items/", readyVideo.HoverPreviewUrl);

        using var thumbnailResponse = await client.GetAsync(readyVideo.ThumbnailUrl);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal("image/jpeg", thumbnailResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 1, 2, 3 }, await thumbnailResponse.Content.ReadAsByteArrayAsync());

        using var previewResponse = await client.GetAsync(readyVideo.HoverPreviewUrl);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Equal("video/mp4", previewResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(videoFixture, await previewResponse.Content.ReadAsByteArrayAsync());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, readyVideo.HoverPreviewUrl);
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(videoFixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Archive_subtitle_endpoint_serves_ready_vtt_and_listing_reports_state()
    {
        using var root = CreateArchive();
        var videoPath = Path.Combine(root.Path, "Downloads", "clip.mp4");
        var subtitlePath = Path.Combine(root.Path, "Downloads", "clip.srt");
        await File.WriteAllBytesAsync(videoPath, [1, 2, 3]);
        await File.WriteAllTextAsync(subtitlePath, "subtitle");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        var pendingListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var pending = pendingListing.Items.Single(item => item.Name == "clip.mp4");

        Assert.Equal(SubtitleState.Pending, pending.SubtitleState);
        Assert.Null(pending.SubtitleUrl);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/downloads/items/{pending.Id}/subtitle")).StatusCode);

        var cache = new SubtitleCache(Options.Create(new ThumbnailCacheOptions { Path = factory.PreviewPath }));
        var relativeIdentity = $"archive/downloads/{pending.Id}/clip.mp4";
        var entry = new WebApp.Models.VideoFileEntry(
            pending.Id,
            videoPath,
            relativeIdentity,
            "clip.mp4",
            ".mp4",
            3,
            File.GetLastWriteTimeUtc(videoPath));
        var subtitle = new WebApp.Models.SubtitleFileInfo(
            subtitlePath,
            new FileInfo(subtitlePath).Length,
            File.GetLastWriteTimeUtc(subtitlePath));
        await File.WriteAllTextAsync(cache.GetFinalPath(cache.ComputeKey(entry, subtitle)), "WEBVTT\n\n");

        var readyListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var ready = readyListing.Items.Single(item => item.Name == "clip.mp4");

        Assert.Equal(SubtitleState.Ready, ready.SubtitleState);
        Assert.StartsWith("/api/archive/downloads/items/", ready.SubtitleUrl);

        using var subtitleResponse = await client.GetAsync(ready.SubtitleUrl);
        Assert.Equal(HttpStatusCode.OK, subtitleResponse.StatusCode);
        Assert.Equal("text/vtt", subtitleResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("WEBVTT\n\n", await subtitleResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Archive_media_endpoints_reject_non_video_and_stale_ids()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path, hoverPreviewEnabled: true);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{item.Id}/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{item.Id}/preview")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{item.Id}/subtitle")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{Guid.NewGuid():N}/thumbnail")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/documents/items/%2Fetc%2Fpasswd/preview")).StatusCode);
    }

    [Fact]
    public async Task CreateFolder_rejects_trash_and_accepts_valid_category()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/archive/documents/folders", new CreateFolderRequest(null, "Projects"));
        using var rejected = await client.PostAsJsonAsync("/api/archive/trash/folders", new CreateFolderRequest(null, "Nope"));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "Documents", "Projects")));
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
    }

    [Fact]
    public async Task CreateFile_with_valid_markdown_name_appears_in_a_subsequent_listing()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var created = await client.PostAsJsonAsync("/api/archive/documents/files", new CreateFileRequest(null, "notes", ".md"));
        var listing = await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items");

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains(listing!.Items, item => item.Name == "notes.md" && item.Kind == ArchiveItemKind.File);
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "notes.md")));
    }

    [Fact]
    public async Task CreateFile_against_trash_returns_forbidden()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/archive/trash/files", new CreateFileRequest(null, "notes", ".txt"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_multipart_upload_route_no_longer_exists()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent("hello archive"u8.ToArray());
        content.Add(fileContent, "file", "notes.txt");

        using var response = await client.PostAsync("/api/archive/documents/upload", content);

        // The old singular "/upload" route no longer exists: routing either finds no endpoint at
        // all (404) or, since "/uploads" now shares the same prefix, matches nothing for POST on
        // this exact literal path and reports method-not-allowed (405). Either way, no multipart
        // upload is accepted through it anymore.
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected 404 or 405, got {response.StatusCode}.");
    }

    [Fact]
    public async Task Upload_session_create_chunk_status_and_complete_flow_publishes_exactly_one_item()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var payload = "hello archive upload session"u8.ToArray();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/archive/documents/uploads", new ArchiveUploadCreateRequest(null, "notes.txt", payload.Length));
        var createJson = await createResponse.Content.ReadAsStringAsync();
        var session = await createResponse.Content.ReadFromJsonAsync<ArchiveUploadSessionDto>();

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.DoesNotContain(root.Path, createJson);
        Assert.Equal(0, session!.ReceivedBytes);
        Assert.True(session.ChunkSizeBytes > 0);

        var firstLength = 10;
        using var chunkContent = new ByteArrayContent(payload[..firstLength]);
        using var chunkResponse = await client.PutAsync(
            $"/api/archive/documents/uploads/{session.Id}/chunk?offset=0&length={firstLength}", chunkContent);
        var afterFirst = await chunkResponse.Content.ReadFromJsonAsync<ArchiveUploadSessionDto>();

        Assert.Equal(HttpStatusCode.OK, chunkResponse.StatusCode);
        Assert.Equal(firstLength, afterFirst!.ReceivedBytes);

        var statusJson = await client.GetStringAsync($"/api/archive/documents/uploads/{session.Id}");
        Assert.DoesNotContain(root.Path, statusJson);
        var status = await client.GetFromJsonAsync<ArchiveUploadSessionDto>($"/api/archive/documents/uploads/{session.Id}");
        Assert.Equal(firstLength, status!.ReceivedBytes);

        var remainingLength = payload.Length - firstLength;
        using var secondChunkContent = new ByteArrayContent(payload[firstLength..]);
        using var secondChunkResponse = await client.PutAsync(
            $"/api/archive/documents/uploads/{session.Id}/chunk?offset={firstLength}&length={remainingLength}", secondChunkContent);
        Assert.Equal(HttpStatusCode.OK, secondChunkResponse.StatusCode);

        using var completeResponse = await client.PostAsync($"/api/archive/documents/uploads/{session.Id}/complete", content: null);
        var completeJson = await completeResponse.Content.ReadAsStringAsync();
        var listing = await completeResponse.Content.ReadFromJsonAsync<ArchiveListingDto>();

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.DoesNotContain(root.Path, completeJson);
        Assert.Contains(listing!.Items, item => item.Name == "notes.txt");
        var finalPath = Path.Combine(root.Path, "Documents", "notes.txt");
        Assert.True(File.Exists(finalPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(finalPath));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/archive/documents/uploads/{session.Id}")).StatusCode);
    }

    [Fact]
    public async Task Upload_session_survives_an_interruption_and_resumes_across_a_fresh_factory_over_the_same_root()
    {
        using var root = CreateArchive();
        var payload = new byte[64];
        Random.Shared.NextBytes(payload);
        string sessionId;

        using (var factory = new VideoManagerFactory(root.Path))
        using (var client = factory.CreateClient())
        {
            using var createResponse = await client.PostAsJsonAsync(
                "/api/archive/documents/uploads", new ArchiveUploadCreateRequest(null, "clip.txt", payload.Length));
            var session = await createResponse.Content.ReadFromJsonAsync<ArchiveUploadSessionDto>();
            sessionId = session!.Id;

            using var chunkContent = new ByteArrayContent(payload[..32]);
            using var chunkResponse = await client.PutAsync(
                $"/api/archive/documents/uploads/{sessionId}/chunk?offset=0&length=32", chunkContent);
            Assert.Equal(HttpStatusCode.OK, chunkResponse.StatusCode);
            // The client is dropped here to simulate an interrupted connection before completion.
        }

        using var resumedFactory = new VideoManagerFactory(root.Path);
        using var resumedClient = resumedFactory.CreateClient();

        var listSessions = await resumedClient.GetFromJsonAsync<List<ArchiveUploadSessionDto>>("/api/archive/documents/uploads");
        var recovered = Assert.Single(listSessions!);
        Assert.Equal(sessionId, recovered.Id);
        Assert.Equal(32, recovered.ReceivedBytes);

        using var remainingChunkContent = new ByteArrayContent(payload[32..]);
        using var remainingChunkResponse = await resumedClient.PutAsync(
            $"/api/archive/documents/uploads/{sessionId}/chunk?offset=32&length=32", remainingChunkContent);
        Assert.Equal(HttpStatusCode.OK, remainingChunkResponse.StatusCode);

        using var completeResponse = await resumedClient.PostAsync($"/api/archive/documents/uploads/{sessionId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(root.Path, "Documents", "clip.txt")));
    }

    [Fact]
    public async Task Upload_session_create_rejects_trash_unsupported_extension_and_collision_without_leaking_paths()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "existing.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var trashResponse = await client.PostAsJsonAsync(
            "/api/archive/trash/uploads", new ArchiveUploadCreateRequest(null, "notes.txt", 10));
        Assert.Equal(HttpStatusCode.Forbidden, trashResponse.StatusCode);

        using var unsupportedResponse = await client.PostAsJsonAsync(
            "/api/archive/documents/uploads", new ArchiveUploadCreateRequest(null, "malware.exe", 10));
        var unsupportedJson = await unsupportedResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedResponse.StatusCode);
        Assert.DoesNotContain(root.Path, unsupportedJson);

        using var collisionResponse = await client.PostAsJsonAsync(
            "/api/archive/documents/uploads", new ArchiveUploadCreateRequest(null, "existing.txt", 10));
        Assert.Equal(HttpStatusCode.Conflict, collisionResponse.StatusCode);

        var listing = await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items");
        Assert.DoesNotContain(listing!.Items, item => item.Name == "malware.exe");
    }

    [Fact]
    public async Task Upload_chunk_endpoint_rejects_gap_offsets_and_unknown_sessions_without_leaking_paths()
    {
        using var root = CreateArchive();
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync(
            "/api/archive/documents/uploads", new ArchiveUploadCreateRequest(null, "notes.txt", 20));
        var session = await createResponse.Content.ReadFromJsonAsync<ArchiveUploadSessionDto>();

        using var gapContent = new ByteArrayContent(new byte[5]);
        using var gapResponse = await client.PutAsync(
            $"/api/archive/documents/uploads/{session!.Id}/chunk?offset=5&length=5", gapContent);
        var gapJson = await gapResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, gapResponse.StatusCode);
        Assert.DoesNotContain(root.Path, gapJson);

        using var unknownContent = new ByteArrayContent(new byte[5]);
        using var unknownResponse = await client.PutAsync(
            $"/api/archive/documents/uploads/{Guid.NewGuid():N}/chunk?offset=0&length=5", unknownContent);
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);

        using var cancelResponse = await client.DeleteAsync($"/api/archive/documents/uploads/{session.Id}");
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/archive/documents/uploads/{session.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/archive/documents/uploads/{session.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_enqueues_a_move_to_trash_job_that_eventually_completes()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.DeleteAsync($"/api/archive/documents/items/{item.Id}");
        var job = await response.Content.ReadFromJsonAsync<ArchiveMutationJobDto>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(ArchiveMutationKind.MoveToTrash, job!.Kind);
        Assert.Equal(1, job.TotalItems);

        var completed = await PollUntilTerminalAsync(client, job.JobId);
        Assert.Equal(ArchiveMutationJobState.Completed, completed.State);
        Assert.True(File.Exists(Path.Combine(root.Path, "Trash", "note.txt")));
    }

    [Fact]
    public async Task Move_endpoint_enqueues_a_job_that_relocates_the_item_across_categories()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Downloads", "file.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/downloads/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.PatchAsJsonAsync(
            $"/api/archive/downloads/items/{item.Id}/location", new MoveArchiveItemRequest("videos", null));
        var job = await response.Content.ReadFromJsonAsync<ArchiveMutationJobDto>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(ArchiveMutationJobState.Pending, job!.State);

        var completed = await PollUntilTerminalAsync(client, job.JobId);
        Assert.Equal(ArchiveMutationJobState.Completed, completed.State);
        Assert.True(File.Exists(Path.Combine(root.Path, "Videos", "file.txt")));
        Assert.False(File.Exists(Path.Combine(root.Path, "Downloads", "file.txt")));
    }

    [Fact]
    public async Task EmptyTrash_permanently_deletes_items_in_trash_root()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Trash", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/archive/trash/items");
        var job = await response.Content.ReadFromJsonAsync<ArchiveMutationJobDto>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, job!.TotalItems);

        var completed = await PollUntilTerminalAsync(client, job.JobId);
        Assert.Equal(ArchiveMutationJobState.Completed, completed.State);
        Assert.False(File.Exists(Path.Combine(root.Path, "Trash", "note.txt")));
    }

    [Fact]
    public async Task EmptyTrash_on_non_trash_category_returns_forbidden()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync("/api/archive/documents/items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "note.txt")));
    }

    private static async Task<ArchiveMutationJobDto> PollUntilTerminalAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var jobs = await client.GetFromJsonAsync<List<ArchiveMutationJobDto>>("/api/archive/jobs");
            var job = jobs!.SingleOrDefault(candidate => candidate.JobId == jobId);
            if (job is { State: ArchiveMutationJobState.Completed or ArchiveMutationJobState.Failed })
            {
                return job;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Archive mutation job {jobId} did not reach a terminal state in time.");
    }

    [Fact]
    public async Task Stream_video_serves_supported_archive_file_without_exposing_paths()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Music", "clip.mp4"), "fake mp4");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/music/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.GetAsync($"/api/archive/music/items/{item.Id}/stream");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(root.Path, body);
    }

    [Fact]
    public async Task Stream_video_rejects_non_video_archive_file()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.GetAsync($"/api/archive/documents/items/{item.Id}/stream");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Music_listing_returns_audio_and_cover_contract_without_paths()
    {
        using var root = CreateArchive();
        var album = Path.Combine(root.Path, "Music", "Album");
        Directory.CreateDirectory(album);
        await File.WriteAllBytesAsync(Path.Combine(album, "02.wav"), [1, 2, 3, 4, 5]);
        await File.WriteAllBytesAsync(Path.Combine(album, "01.mp3"), [6, 7, 8, 9, 10]);
        await File.WriteAllBytesAsync(Path.Combine(album, "cover.jpg"), [11, 12, 13]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var rootListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/music/items"))!;
        var folder = rootListing.Items.Single(item => item.Name == "Album");

        using var response = await client.GetAsync($"/api/archive/music/items?folderId={folder.Id}");
        var json = await response.Content.ReadAsStringAsync();
        var listing = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var tracks = listing!.Items.Where(item => item.IsMusic).ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal(["01.mp3", "02.wav"], tracks.Select(track => track.Name));
        Assert.All(tracks, track =>
        {
            Assert.False(track.IsVideo);
            Assert.StartsWith("/api/archive/music/items/", track.AudioUrl);
            Assert.EndsWith("/audio", track.AudioUrl);
            Assert.Equal($"/api/archive/music/items/{folder.Id}/cover", track.AlbumCoverUrl);
            Assert.Equal(305, track.DurationSeconds);
        });
    }

    [Fact]
    public async Task Audio_endpoint_streams_music_with_range_processing()
    {
        using var root = CreateArchive();
        byte[] fixture = [10, 20, 30, 40, 50];
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Music", "song.mp3"), fixture);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/music/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.GetAsync(item.AudioUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await response.Content.ReadAsByteArrayAsync());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, item.AudioUrl);
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(fixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Audio_listing_and_endpoint_work_outside_music_category()
    {
        using var root = CreateArchive();
        var album = Path.Combine(root.Path, "Books", "Audiobook");
        Directory.CreateDirectory(album);
        byte[] fixture = [42, 43, 44, 45, 46];
        await File.WriteAllBytesAsync(Path.Combine(album, "chapter-01.mp3"), fixture);
        await File.WriteAllBytesAsync(Path.Combine(album, "cover.png"), [1, 2, 3]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var rootListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var folder = rootListing.Items.Single(item => item.Name == "Audiobook");

        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>($"/api/archive/books/items?folderId={folder.Id}"))!;
        var track = Assert.Single(listing.Items, item => item.IsMusic);

        Assert.True(track.IsMusic);
        Assert.False(track.IsVideo);
        Assert.Equal($"/api/archive/books/items/{track.Id}/audio", track.AudioUrl);
        Assert.Equal($"/api/archive/books/items/{folder.Id}/cover", track.AlbumCoverUrl);

        using var audioResponse = await client.GetAsync(track.AudioUrl);
        Assert.Equal(HttpStatusCode.OK, audioResponse.StatusCode);
        Assert.Equal("audio/mpeg", audioResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await audioResponse.Content.ReadAsByteArrayAsync());

        using var coverResponse = await client.GetAsync(track.AlbumCoverUrl);
        Assert.Equal(HttpStatusCode.OK, coverResponse.StatusCode);
        Assert.Equal("image/png", coverResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cover_endpoint_serves_first_image_and_rejects_invalid_ids()
    {
        using var root = CreateArchive();
        var album = Path.Combine(root.Path, "Music", "Album");
        Directory.CreateDirectory(album);
        await File.WriteAllBytesAsync(Path.Combine(album, "song.wav"), [1, 2, 3]);
        await File.WriteAllBytesAsync(Path.Combine(album, "zeta.png"), [4, 5, 6]);
        await File.WriteAllBytesAsync(Path.Combine(album, "alpha.jpeg"), [7, 8, 9]);
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Documents", "cover.jpg"), [10]);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var rootListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/music/items"))!;
        var folder = rootListing.Items.Single(item => item.Name == "Album");
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>($"/api/archive/music/items?folderId={folder.Id}"))!;
        var track = listing.Items.Single(item => item.IsMusic);

        using var response = await client.GetAsync(track.AlbumCoverUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 7, 8, 9 }, await response.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/music/items/{track.Id}/cover")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{folder.Id}/cover")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/music/items/%2Fetc%2Fpasswd/cover")).StatusCode);
    }

    [Fact]
    public async Task Image_listing_and_endpoint_work_in_any_category_without_exposing_paths()
    {
        using var root = CreateArchive();
        byte[] jpgFixture = [1, 2, 3, 4];
        byte[] pngFixture = [5, 6, 7, 8];
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", "photo.jpg"), jpgFixture);
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Documents", "scan.png"), pngFixture);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var picturesResponse = await client.GetAsync("/api/archive/photos/items");
        var picturesJson = await picturesResponse.Content.ReadAsStringAsync();
        var picturesListing = await picturesResponse.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var photo = Assert.Single(picturesListing!.Items);

        Assert.DoesNotContain(root.Path, picturesJson);
        Assert.True(photo.IsImage);
        Assert.False(photo.IsVideo);
        Assert.False(photo.IsMusic);
        Assert.StartsWith("/api/archive/photos/items/", photo.ImageUrl);
        Assert.EndsWith("/image", photo.ImageUrl);

        using var jpgResponse = await client.GetAsync(photo.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, jpgResponse.StatusCode);
        Assert.Equal("image/jpeg", jpgResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(jpgFixture, await jpgResponse.Content.ReadAsByteArrayAsync());

        var documentsListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var scan = documentsListing.Items.Single(item => item.Name == "scan.png");
        var note = documentsListing.Items.Single(item => item.Name == "note.txt");

        Assert.True(scan.IsImage);
        Assert.False(note.IsImage);
        Assert.Null(note.ImageUrl);

        using var pngResponse = await client.GetAsync(scan.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, pngResponse.StatusCode);
        Assert.Equal("image/png", pngResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pngFixture, await pngResponse.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("animation.gif", "image/gif")]
    [InlineData("photo.webp", "image/webp")]
    [InlineData("photo.avif", "image/avif")]
    [InlineData("photo.bmp", "image/bmp")]
    [InlineData("favicon.ico", "image/x-icon")]
    public async Task Added_raster_image_formats_are_listed_and_served_with_their_contract_media_type(string fileName, string contentType)
    {
        using var root = CreateArchive();
        byte[] fixture = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Pictures", fileName), fixture);
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var listingResponse = await client.GetAsync("/api/archive/photos/items");
        var json = await listingResponse.Content.ReadAsStringAsync();
        var listing = await listingResponse.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var item = Assert.Single(listing!.Items);

        Assert.True(item.IsImage);
        Assert.NotNull(item.ImageUrl);
        Assert.DoesNotContain(root.Path, json);

        using var imageResponse = await client.GetAsync(item.ImageUrl);
        Assert.Equal(HttpStatusCode.OK, imageResponse.StatusCode);
        Assert.Equal(contentType, imageResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(fixture, await imageResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Image_endpoint_returns_not_found_for_non_image_folder_and_unknown_ids()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Pictures", "notes.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/photos/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Album");
        var textFile = listing.Items.Single(item => item.Name == "notes.txt");

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/photos/items/{folder.Id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/photos/items/{textFile.Id}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/photos/items/{Guid.NewGuid():N}/image")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/photos/items/%2Fetc%2Fpasswd/image")).StatusCode);
    }

    [Fact]
    public async Task Book_listing_returns_browser_safe_epub_fields_without_paths()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"), "My Book", "My Author");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/archive/books/items");
        var json = await response.Content.ReadAsStringAsync();
        var listing = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var book = Assert.Single(listing!.Items);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.True(book.IsBook);
        Assert.Equal("My Book", book.BookTitle);
        Assert.Equal("My Author", book.BookAuthor);
        Assert.StartsWith("/api/archive/books/items/", book.BookCoverUrl);
        Assert.EndsWith("/book/cover", book.BookCoverUrl);
    }

    [Fact]
    public async Task Book_endpoint_returns_metadata_navigation_and_progress()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"), "My Book", "My Author");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var book = Assert.Single(listing.Items);

        using var response = await client.GetAsync($"/api/archive/books/items/{book.Id}/book");
        var json = await response.Content.ReadAsStringAsync();
        var bookDto = await response.Content.ReadFromJsonAsync<BookDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal("My Book", bookDto!.Title);
        Assert.Equal("My Author", bookDto.Author);
        Assert.True(bookDto.HasCover);
        Assert.Equal(2, bookDto.Navigation.Count);
        Assert.Equal(["0", "1"], bookDto.ChapterIds);
        Assert.Null(bookDto.Progress);
    }

    [Fact]
    public async Task Book_cover_endpoint_serves_embedded_cover_image()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var book = Assert.Single(listing.Items);

        using var response = await client.GetAsync($"/api/archive/books/items/{book.Id}/book/cover");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Book_chapter_endpoint_returns_sanitized_content_and_rejects_unknown_chapters()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var book = Assert.Single(listing.Items);

        using var response = await client.GetAsync($"/api/archive/books/items/{book.Id}/book/chapters/0");
        var chapter = await response.Content.ReadFromJsonAsync<BookChapterDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This is the first chapter", chapter!.ContentHtml);
        Assert.DoesNotContain("<script", chapter.ContentHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Null(chapter.PreviousChapterId);
        Assert.Equal("1", chapter.NextChapterId);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/books/items/{book.Id}/book/chapters/99")).StatusCode);
    }

    [Fact]
    public async Task Book_note_endpoint_appends_to_shared_notes_file_and_rejects_invalid_requests()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"), "My Book", "My Author");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var book = Assert.Single(listing.Items);
        var chapter = (await client.GetFromJsonAsync<BookChapterDto>($"/api/archive/books/items/{book.Id}/book/chapters/0"))!;
        const string selectedText = "This is the first chapter";
        var start = EpubChapterText.Normalize(chapter.ContentHtml).IndexOf(selectedText, StringComparison.Ordinal);

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/books/items/{book.Id}/book/notes",
            new BookNoteRequest("0", selectedText, start, start + selectedText.Length));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var content = await File.ReadAllTextAsync(notesPath);
        Assert.Contains("My Book (My Author)", content);
        Assert.Contains(selectedText, content);
        Assert.Contains("==========", content);

        using var emptySelection = await client.PostAsJsonAsync(
            $"/api/archive/books/items/{book.Id}/book/notes",
            new BookNoteRequest("0", "   ", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, emptySelection.StatusCode);

        using var unknownId = await client.PostAsJsonAsync(
            $"/api/archive/books/items/{Guid.NewGuid():N}/book/notes",
            new BookNoteRequest("0", "Text", 0, 4));
        Assert.Equal(HttpStatusCode.NotFound, unknownId.StatusCode);

        using var invalidRange = await client.PostAsJsonAsync(
            $"/api/archive/books/items/{book.Id}/book/notes",
            new BookNoteRequest("0", "Different text", 0, 14));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRange.StatusCode);

        var highlights = await client.GetFromJsonAsync<List<BookHighlightDto>>(
            $"/api/archive/books/items/{book.Id}/book/highlights");
        var highlight = Assert.Single(highlights!);
        Assert.Equal(selectedText, highlight.SelectedText);
        Assert.Equal(start, highlight.TextOffsetStart);
        Assert.True(File.Exists(Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookHighlights.json")));
    }

    [Fact]
    public async Task Book_progress_endpoint_round_trips_chapter_and_word_offset()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMinimalEpub(Path.Combine(root.Path, "Books", "novel.epub"));
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var book = Assert.Single(listing.Items);

        using var initial = await client.GetAsync($"/api/archive/books/items/{book.Id}/book/progress");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Null(await initial.Content.ReadFromJsonAsync<BookProgressDto>());

        using var saveResponse = await client.PutAsJsonAsync(
            $"/api/archive/books/items/{book.Id}/book/progress", new BookProgressDto("1", 750));
        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var reloaded = await client.GetFromJsonAsync<BookProgressDto>($"/api/archive/books/items/{book.Id}/book/progress");
        Assert.Equal("1", reloaded!.ChapterId);
        Assert.Equal(750, reloaded.WordOffset);

        var bookDtoAfterProgress = await client.GetFromJsonAsync<BookDto>($"/api/archive/books/items/{book.Id}/book");
        Assert.Equal("1", bookDtoAfterProgress!.Progress?.ChapterId);
    }

    [Fact]
    public async Task Book_endpoints_reject_malformed_epub_and_non_book_categories_without_leaking_paths()
    {
        using var root = CreateArchive();
        EpubTestFixture.CreateMalformedEpub(Path.Combine(root.Path, "Books", "broken.epub"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var booksListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/books/items"))!;
        var broken = Assert.Single(booksListing.Items);
        var documentsListing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var nonBookItem = Assert.Single(documentsListing.Items);

        using var brokenResponse = await client.GetAsync($"/api/archive/books/items/{broken.Id}/book");
        var brokenJson = await brokenResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, brokenResponse.StatusCode);
        Assert.DoesNotContain(root.Path, brokenJson);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{nonBookItem.Id}/book")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/books/items/{Guid.NewGuid():N}/book")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/books/items/%2Fetc%2Fpasswd/book")).StatusCode);
    }

    [Fact]
    public async Task Pdf_listing_and_endpoint_work_in_any_category_without_exposing_paths()
    {
        using var root = CreateArchive();
        byte[] pdfFixture = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];
        await File.WriteAllBytesAsync(Path.Combine(root.Path, "Documents", "report.pdf"), pdfFixture);
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "note.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/archive/documents/items");
        var json = await response.Content.ReadAsStringAsync();
        var listing = await response.Content.ReadFromJsonAsync<ArchiveListingDto>();
        var pdf = listing!.Items.Single(item => item.Name == "report.pdf");
        var note = listing.Items.Single(item => item.Name == "note.txt");

        Assert.DoesNotContain(root.Path, json);
        Assert.True(pdf.IsPdfDocument);
        Assert.False(pdf.IsVideo);
        Assert.False(pdf.IsImage);
        Assert.StartsWith("/api/archive/documents/items/", pdf.PdfUrl);
        Assert.EndsWith("/pdf", pdf.PdfUrl);
        Assert.False(note.IsPdfDocument);
        Assert.Null(note.PdfUrl);

        using var pdfResponse = await client.GetAsync(pdf.PdfUrl);
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(pdfFixture, await pdfResponse.Content.ReadAsByteArrayAsync());

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, pdf.PdfUrl);
        rangeRequest.Headers.Range = new RangeHeaderValue(1, 3);
        using var rangeResponse = await client.SendAsync(rangeRequest);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal(pdfFixture[1..4], await rangeResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Pdf_endpoint_returns_not_found_for_non_pdf_folder_and_unknown_ids()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Folder"));
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "notes.txt"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var folder = listing.Items.Single(item => item.Name == "Folder");
        var textFile = listing.Items.Single(item => item.Name == "notes.txt");

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{folder.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{textFile.Id}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{Guid.NewGuid():N}/pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/documents/items/%2Fetc%2Fpasswd/pdf")).StatusCode);
    }

    [Fact]
    public async Task Text_document_endpoint_loads_source_and_sanitized_markdown_preview_without_leaking_paths()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(
            Path.Combine(root.Path, "Documents", "notes.md"),
            "# Title\n\n<script>alert(1)</script>\n\n[safe](https://example.com)\n\n![no](https://example.com/pic.png)");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);
        Assert.True(item.IsTextDocument);

        using var response = await client.GetAsync($"/api/archive/documents/items/{item.Id}/text");
        var json = await response.Content.ReadAsStringAsync();
        var document = await response.Content.ReadFromJsonAsync<TextDocumentDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(root.Path, json);
        Assert.Equal(TextDocumentKind.Markdown, document!.DocumentKind);
        Assert.Contains("<h1", document.PreviewHtml);
        Assert.DoesNotContain("<script", document.PreviewHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", document.PreviewHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("href=\"https://example.com/\"", document.PreviewHtml);
        Assert.False(string.IsNullOrWhiteSpace(document.Revision));
    }

    [Fact]
    public async Task Text_document_preview_endpoint_renders_bounded_submitted_source()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "notes.md"), "original");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);

        using var response = await client.PostAsJsonAsync(
            $"/api/archive/documents/items/{item.Id}/text/preview", new TextDocumentPreviewRequest("**bold**"));
        var preview = await response.Content.ReadFromJsonAsync<TextDocumentPreviewDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<strong>bold</strong>", preview!.PreviewHtml);

        using var tooLarge = await client.PostAsJsonAsync(
            $"/api/archive/documents/items/{item.Id}/text/preview",
            new TextDocumentPreviewRequest(new string('a', TextDocumentService.MaxSourceBytes + 1)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
    }

    [Fact]
    public async Task Text_document_save_endpoint_writes_the_file_and_returns_a_new_revision()
    {
        using var root = CreateArchive();
        var path = Path.Combine(root.Path, "Documents", "notes.txt");
        await File.WriteAllTextAsync(path, "original");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);
        var loaded = await client.GetFromJsonAsync<TextDocumentDto>($"/api/archive/documents/items/{item.Id}/text");

        using var response = await client.PutAsJsonAsync(
            $"/api/archive/documents/items/{item.Id}/text", new TextDocumentSaveRequest("updated content", loaded!.Revision));
        var saved = await response.Content.ReadFromJsonAsync<TextDocumentDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("updated content", await File.ReadAllTextAsync(path));
        Assert.NotEqual(loaded.Revision, saved!.Revision);
    }

    [Fact]
    public async Task Text_document_save_endpoint_returns_conflict_and_preserves_the_disk_file_on_stale_revision()
    {
        using var root = CreateArchive();
        var path = Path.Combine(root.Path, "Documents", "notes.txt");
        await File.WriteAllTextAsync(path, "original");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var item = Assert.Single(listing.Items);
        var loaded = await client.GetFromJsonAsync<TextDocumentDto>($"/api/archive/documents/items/{item.Id}/text");

        await Task.Delay(10);
        await File.WriteAllTextAsync(path, "changed on disk");

        using var response = await client.PutAsJsonAsync(
            $"/api/archive/documents/items/{item.Id}/text", new TextDocumentSaveRequest("my draft", loaded!.Revision));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("changed on disk", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Text_document_endpoints_reject_non_text_items_and_unknown_ids_without_leaking_paths()
    {
        using var root = CreateArchive();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "Documents", "photo.jpg"), "content");
        using var factory = new VideoManagerFactory(root.Path);
        using var client = factory.CreateClient();
        var listing = (await client.GetFromJsonAsync<ArchiveListingDto>("/api/archive/documents/items"))!;
        var nonText = Assert.Single(listing.Items);

        using var wrongKindResponse = await client.GetAsync($"/api/archive/documents/items/{nonText.Id}/text");
        var wrongKindJson = await wrongKindResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, wrongKindResponse.StatusCode);
        Assert.DoesNotContain(root.Path, wrongKindJson);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/archive/documents/items/{Guid.NewGuid():N}/text")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/archive/documents/items/%2Fetc%2Fpasswd/text")).StatusCode);
    }

    [Fact]
    public void Archive_browser_markup_uses_unified_dropdown_cards_and_keeps_video_grid_specialized()
    {
        var archiveBrowser = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/ArchiveBrowser.razor"));
        var home = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Pages/UtilitiesPages/VideoComposition.razor"));

        Assert.Contains("bi-plus", archiveBrowser);
        Assert.Contains("data-bs-toggle=\"dropdown\"", archiveBrowser);
        Assert.Contains("dropdown-menu", archiveBrowser);
        Assert.Contains("ratio ratio-16x9", archiveBrowser);
        Assert.Contains("HoverPreviewUrl", archiveBrowser);
        Assert.Contains("AlbumCoverUrl", archiveBrowser);
        Assert.Contains("archive-cover-tile ratio ratio-1x1", archiveBrowser);
        Assert.Contains("archive-cover-overlay", archiveBrowser);
        Assert.Contains("ImageUrl", archiveBrowser);
        Assert.Contains("FormatDisplayName(item)", archiveBrowser);
        Assert.Contains("FormatDuration(item.DurationSeconds)", archiveBrowser);
        Assert.Contains("SelectMusic", archiveBrowser);
        Assert.Contains("\"pdf\"", archiveBrowser);
        Assert.Contains("\"mp4\"", archiveBrowser);
        Assert.Contains("bi-filetype-{type}", archiveBrowser);
        Assert.Contains("FormatDuration(item.DurationSeconds)", archiveBrowser);
        Assert.Contains("FormatResolution(item.Width, item.Height)", archiveBrowser);
        Assert.Contains("FormatDimensions(item.Width, item.Height)", archiveBrowser);
        Assert.Contains("bi-download", archiveBrowser);
        Assert.Contains("/download", archiveBrowser);
        Assert.DoesNotContain("card-footer d-flex gap-2 justify-content-center", archiveBrowser);
        Assert.Contains("<VideoGrid Items=\"_cuts\"", home);
        Assert.Contains("<VideoGrid Items=\"_compositions\"", home);

        var player = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/Player.razor"));
        var controls = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../WebApp/WebApp.Client/Components/MediaPlayerControls.razor"));

        Assert.Contains("<audio @key=\"Selected.Id\"", player);
        Assert.Contains("music-cover-stage", player);
        Assert.Contains("IsMusicMode=\"PlayerState.IsMusic\"", player);
        Assert.Contains("bi-chevron-compact-left", controls);
        Assert.Contains("bi-chevron-compact-right", controls);
        Assert.Contains("@if (!IsMusicMode)", controls);
    }

    private sealed class VideoManagerFactory : WebApplicationFactory<Program>
    {
        private readonly string _archiveRoot;
        private readonly bool _hoverPreviewEnabled;
        private readonly string _previewPath = CreateDirectory();

        public VideoManagerFactory(string archiveRoot, bool hoverPreviewEnabled = false)
        {
            _archiveRoot = archiveRoot;
            _hoverPreviewEnabled = hoverPreviewEnabled;
        }

        public string PreviewPath => _previewPath;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = _archiveRoot,
                    ["VideoLibrary:Path"] = Path.Combine(_archiveRoot, "Videos"),
                    ["ThumbnailCache:Path"] = _previewPath,
                    ["VideoCut:Path"] = Path.Combine(_archiveRoot, "Videos", "Cuts"),
                    ["VideoComposition:Path"] = Path.Combine(_archiveRoot, "Videos", "VideoComposition"),
                    ["HoverPreview:Enabled"] = _hoverPreviewEnabled.ToString()
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVideoDurationProbe>();
                services.RemoveAll<IVideoResolutionProbe>();
                services.AddSingleton<IVideoDurationProbe>(new FixedDurationProbe(TimeSpan.FromSeconds(305)));
                services.AddSingleton<IVideoResolutionProbe>(new FixedResolutionProbe(1920, 1080));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && Directory.Exists(_previewPath))
            {
                Directory.Delete(_previewPath, recursive: true);
            }
        }
    }

    private sealed class FixedDurationProbe(TimeSpan? duration) : IVideoDurationProbe
    {
        public Task<TimeSpan?> GetDurationAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult(duration);
    }

    private sealed class FixedResolutionProbe(int? width, int? height) : IVideoResolutionProbe
    {
        public Task<(int? Width, int? Height)> GetResolutionAsync(string physicalPath, CancellationToken cancellationToken) =>
            Task.FromResult((width, height));
    }

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Cuts"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "VideoComposition"));
        return root;
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"video-manager-archive-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-archive-endpoints-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
