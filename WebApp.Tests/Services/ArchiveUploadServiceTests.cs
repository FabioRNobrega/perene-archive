using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveUploadServiceTests
{
    [Fact]
    public async Task Create_append_and_complete_round_trips_bytes_into_a_nested_folder()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Reports"));
        var folder = archive.List("documents", null).Items.Single(item => item.Name == "Reports");
        var clock = new FakeClock();
        var service = CreateUploadService(root.Path, archive, clock);
        var payload = "hello archive upload"u8.ToArray();

        var session = service.Create("documents", folder.Id, "notes.txt", payload.Length);
        Assert.Equal(0, session.ReceivedBytes);
        Assert.True(session.ChunkSizeBytes > 0);

        var firstChunk = payload[..10];
        var afterFirst = await service.AppendChunkAsync(
            "documents", session.Id, 0, firstChunk.Length, new MemoryStream(firstChunk), CancellationToken.None);
        Assert.Equal(10, afterFirst.ReceivedBytes);

        var secondChunk = payload[10..];
        var afterSecond = await service.AppendChunkAsync(
            "documents", session.Id, 10, secondChunk.Length, new MemoryStream(secondChunk), CancellationToken.None);
        Assert.Equal(payload.Length, afterSecond.ReceivedBytes);

        // Reload against the same workspace, as a fresh instance would after an app restart.
        var reloadedService = CreateUploadService(root.Path, archive, clock);
        var status = reloadedService.GetStatus("documents", session.Id);
        Assert.Equal(payload.Length, status.ReceivedBytes);

        var listing = await reloadedService.CompleteAsync("documents", session.Id, CancellationToken.None);
        Assert.Contains(listing.Items, item => item.Name == "notes.txt");
        var finalPath = Path.Combine(root.Path, "Documents", "Reports", "notes.txt");
        Assert.True(File.Exists(finalPath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(finalPath));

        // The session's temporary artifacts are gone after completion.
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root.Path, ".uploads"), $"{session.Id}.*"));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() => Task.FromResult(reloadedService.GetStatus("documents", session.Id)));
    }

    [Theory]
    [InlineData(50L * 1024 * 1024, 5L * 1024 * 1024)]
    [InlineData(100L * 1024 * 1024, 5L * 1024 * 1024)]
    [InlineData(100L * 1024 * 1024 + 1, 20L * 1024 * 1024)]
    [InlineData(1024L * 1024 * 1024, 20L * 1024 * 1024)]
    [InlineData(1024L * 1024 * 1024 + 1, 64L * 1024 * 1024)]
    [InlineData(10L * 1024 * 1024 * 1024, 64L * 1024 * 1024)]
    [InlineData(10L * 1024 * 1024 * 1024 + 1, 128L * 1024 * 1024)]
    public void Create_selects_the_configured_adaptive_chunk_size_at_each_boundary(long declaredBytes, long expectedChunkSize)
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        var session = service.Create("documents", null, $"file-{declaredBytes}.txt", declaredBytes);

        Assert.Equal(expectedChunkSize, session.ChunkSizeBytes);
    }

    [Fact]
    public void Create_rejects_a_declared_size_above_the_configured_maximum()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveValidationException>(() => service.Create("documents", null, "huge.txt", 13L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void Create_rejects_zero_or_negative_declared_size()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveValidationException>(() => service.Create("documents", null, "empty.txt", 0));
        Assert.Throws<ArchiveValidationException>(() => service.Create("documents", null, "negative.txt", -1));
    }

    [Fact]
    public void Create_rejects_unsafe_names_and_unsupported_extensions()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveValidationException>(() => service.Create("documents", null, "../evil.txt", 10));
        Assert.Throws<ArchiveValidationException>(() => service.Create("documents", null, "malware.exe", 10));
    }

    [Fact]
    public void Create_is_forbidden_in_trash()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveForbiddenException>(() => service.Create("trash", null, "notes.txt", 10));
    }

    [Fact]
    public void Create_rejects_a_collision_with_an_existing_final_name()
    {
        using var root = CreateArchive();
        File.WriteAllText(Path.Combine(root.Path, "Documents", "notes.txt"), "existing");
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveConflictException>(() => service.Create("documents", null, "notes.txt", 10));
    }

    [Fact]
    public void Create_never_places_session_artifacts_under_a_category_folder()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        var session = service.Create("documents", null, "notes.txt", 10);

        Assert.True(Directory.Exists(Path.Combine(root.Path, ".uploads")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{session.Id}.part")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{session.Id}.json")));
        Assert.Empty(archive.List("documents", null).Items);
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_a_gap_offset()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 20);

        await Assert.ThrowsAsync<ArchiveConflictException>(() =>
            service.AppendChunkAsync("documents", session.Id, 5, 5, new MemoryStream(new byte[5]), CancellationToken.None));

        var status = service.GetStatus("documents", session.Id);
        Assert.Equal(0, status.ReceivedBytes);
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_a_duplicate_or_overlapping_offset()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 20);
        await service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None);

        await Assert.ThrowsAsync<ArchiveConflictException>(() =>
            service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None));
        await Assert.ThrowsAsync<ArchiveConflictException>(() =>
            service.AppendChunkAsync("documents", session.Id, 5, 10, new MemoryStream(new byte[10]), CancellationToken.None));

        Assert.Equal(10, service.GetStatus("documents", session.Id).ReceivedBytes);
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_a_length_that_exceeds_the_declared_total()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 10);

        await Assert.ThrowsAsync<ArchiveValidationException>(() =>
            service.AppendChunkAsync("documents", session.Id, 0, 20, new MemoryStream(new byte[20]), CancellationToken.None));
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_a_truncated_body_without_advancing_the_offset()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 20);

        await Assert.ThrowsAsync<ArchiveValidationException>(() =>
            service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[4]), CancellationToken.None));

        Assert.Equal(0, service.GetStatus("documents", session.Id).ReceivedBytes);
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_a_wrong_category()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 10);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            service.AppendChunkAsync("photos", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None));
    }

    [Fact]
    public async Task AppendChunkAsync_rejects_writes_to_a_completed_session()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 5);
        await service.AppendChunkAsync("documents", session.Id, 0, 5, new MemoryStream(new byte[5]), CancellationToken.None);
        await service.CompleteAsync("documents", session.Id, CancellationToken.None);

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            service.AppendChunkAsync("documents", session.Id, 0, 5, new MemoryStream(new byte[5]), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_rejects_an_incomplete_session()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 20);
        await service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None);

        await Assert.ThrowsAsync<ArchiveValidationException>(() => service.CompleteAsync("documents", session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_rejects_a_collision_that_appeared_after_creation()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var session = service.Create("documents", null, "notes.txt", 5);
        await service.AppendChunkAsync("documents", session.Id, 0, 5, new MemoryStream(new byte[5]), CancellationToken.None);
        File.WriteAllText(Path.Combine(root.Path, "Documents", "notes.txt"), "raced in");

        await Assert.ThrowsAsync<ArchiveConflictException>(() => service.CompleteAsync("documents", session.Id, CancellationToken.None));

        // The temporary session content survives the rejected completion so the user can retry/cancel.
        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{session.Id}.part")));
    }

    [Fact]
    public async Task CancelAsync_removes_only_that_sessions_temporary_artifacts()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var keep = service.Create("documents", null, "keep.txt", 10);
        var cancelled = service.Create("documents", null, "cancel.txt", 10);

        await service.CancelAsync("documents", cancelled.Id, CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(root.Path, ".uploads", $"{cancelled.Id}.part")));
        Assert.False(File.Exists(Path.Combine(root.Path, ".uploads", $"{cancelled.Id}.json")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{keep.Id}.part")));
        Assert.Empty(archive.List("documents", null).Items);
    }

    [Fact]
    public async Task CancelAsync_on_an_unknown_id_throws_not_found()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            service.CancelAsync("documents", Guid.NewGuid().ToString("N"), CancellationToken.None));
    }

    [Fact]
    public void GetStatus_rejects_an_id_shaped_like_a_path_escape()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());

        Assert.Throws<ArchiveNotFoundException>(() => service.GetStatus("documents", "../../etc/passwd"));
    }

    [Fact]
    public async Task GetStatus_and_AppendChunkAsync_reject_an_expired_session()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var clock = new FakeClock();
        var service = CreateUploadService(root.Path, archive, clock);
        var session = service.Create("documents", null, "notes.txt", 10);

        clock.Advance(TimeSpan.FromHours(25));

        Assert.Throws<ArchiveNotFoundException>(() => service.GetStatus("documents", session.Id));
        await Assert.ThrowsAsync<ArchiveNotFoundException>(() =>
            service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None));
    }

    [Fact]
    public async Task Activity_refresh_keeps_a_session_alive_past_the_original_creation_time_plus_ttl()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var clock = new FakeClock();
        var service = CreateUploadService(root.Path, archive, clock);
        var session = service.Create("documents", null, "notes.txt", 10);

        clock.Advance(TimeSpan.FromHours(23));
        await service.AppendChunkAsync("documents", session.Id, 0, 5, new MemoryStream(new byte[5]), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(23));

        // Still within 24h of the *refreshed* activity, so it must remain usable.
        var status = service.GetStatus("documents", session.Id);
        Assert.Equal(5, status.ReceivedBytes);
    }

    [Fact]
    public async Task ListActive_only_returns_sessions_for_the_matching_category_and_parent()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var service = CreateUploadService(root.Path, archive, new FakeClock());
        var inRoot = service.Create("documents", null, "root.txt", 10);
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Sub"));
        var subFolder = archive.List("documents", null).Items.Single(item => item.Name == "Sub");
        var inSub = service.Create("documents", subFolder.Id, "nested.txt", 10);
        var otherCategory = service.Create("photos", null, "photo.jpg", 10);
        await service.AppendChunkAsync("documents", inRoot.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None);
        await service.CompleteAsync("documents", inRoot.Id, CancellationToken.None);

        var rootSessions = service.ListActive("documents", null);
        var subSessions = service.ListActive("documents", subFolder.Id);

        Assert.DoesNotContain(rootSessions, session => session.Id == inRoot.Id);
        Assert.DoesNotContain(rootSessions, session => session.Id == otherCategory.Id);
        Assert.Contains(subSessions, session => session.Id == inSub.Id);
    }

    [Fact]
    public async Task CleanupExpiredSessionsAsync_removes_only_sessions_past_the_ttl()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var clock = new FakeClock();
        var service = CreateUploadService(root.Path, archive, clock);
        var stale = service.Create("documents", null, "stale.txt", 10);
        clock.Advance(TimeSpan.FromHours(25));
        var fresh = service.Create("documents", null, "fresh.txt", 10);

        await service.CleanupExpiredSessionsAsync(CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(root.Path, ".uploads", $"{stale.Id}.json")));
        Assert.False(File.Exists(Path.Combine(root.Path, ".uploads", $"{stale.Id}.part")));
        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{fresh.Id}.json")));
    }

    [Fact]
    public async Task Cleanup_does_not_remove_a_session_whose_activity_was_refreshed_before_the_ttl_elapsed()
    {
        using var root = CreateArchive();
        var archive = CreateArchiveService(root.Path);
        var clock = new FakeClock();
        var service = CreateUploadService(root.Path, archive, clock);
        var session = service.Create("documents", null, "concurrent.txt", 20);

        // A write just under the TTL boundary refreshes LastActivityAt (guarded by the same
        // per-session lock the cleanup worker uses), so a subsequent cleanup pass that only now
        // sees the *refreshed* timestamp must not delete it, even though the session is old
        // relative to its original creation time.
        clock.Advance(TimeSpan.FromHours(23));
        await service.AppendChunkAsync("documents", session.Id, 0, 10, new MemoryStream(new byte[10]), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(2));

        await service.CleanupExpiredSessionsAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(root.Path, ".uploads", $"{session.Id}.json")));
        Assert.Equal(10, service.GetStatus("documents", session.Id).ReceivedBytes);
    }

    private static ArchiveService CreateArchiveService(string path) =>
        new(Options.Create(new ArchiveRootOptions { Path = path }));

    private static ArchiveUploadService CreateUploadService(string path, ArchiveService archive, IArchiveUploadClock clock) =>
        new(
            Options.Create(new ArchiveRootOptions { Path = path }),
            Options.Create(new ArchiveUploadOptions()),
            archive,
            clock);

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        return root;
    }

    private sealed class FakeClock : IArchiveUploadClock
    {
        private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-archive-upload-{Guid.NewGuid():N}");
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
