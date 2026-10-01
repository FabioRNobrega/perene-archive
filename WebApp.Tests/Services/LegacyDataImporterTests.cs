using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class LegacyDataImporterTests : IDisposable
{
    private readonly string _backups = Path.Combine(Path.GetTempPath(), $"import-backups-{Guid.NewGuid():N}");
    private readonly MediaTestHost _host;

    public LegacyDataImporterTests()
    {
        Directory.CreateDirectory(_backups);
        _host = new MediaTestHost(new Dictionary<string, string?> { ["Backup:Path"] = _backups });
        Directory.CreateDirectory(_host.Factory.KeysPath);
    }

    public void Dispose()
    {
        _host.Dispose();
        if (Directory.Exists(_backups)) Directory.Delete(_backups, recursive: true);
    }

    private string NotesDirectory => Path.Combine(_host.BooksPath, "Notes");

    private static string Key(string category, string itemId, long? size, DateTime time) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{category}:{itemId}:{size ?? 0}:{time.Ticks}")))[..32].ToLowerInvariant();

    private sealed record Seeded(string BookId, string ComicId, string DocId, string BookKey, string ComicKey);

    private async Task<Seeded> SeedLegacyAsync()
    {
        var bookPath = EpubTestFixture.CreateMinimalEpub(Path.Combine(_host.BooksPath, "novel.epub"), "My Book", "My Author");
        var comicPath = Path.Combine(_host.BooksPath, "comic.cbz");
        using (var zip = ZipFile.Open(comicPath, ZipArchiveMode.Create))
        {
            foreach (var page in new[] { "p1.jpg", "p2.jpg" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(page).Open());
                writer.Write("fixture " + page);
            }
        }

        File.WriteAllText(Path.Combine(_host.ArchivePath, "Documents", "note.txt"), "text");
        Directory.CreateDirectory(NotesDirectory);
        Directory.CreateDirectory(Path.Combine(_host.ArchivePath, "Dashboard"));

        Seeded seeded = null!;
        await _host.AsAsync((await _host.AddUserAsync("boss", admin: true)), sp =>
        {
            var archive = sp.GetRequiredService<IArchiveService>();
            var book = archive.List("books", null).Items.Single(item => item.Name == "novel.epub");
            var comic = archive.List("books", null).Items.Single(item => item.Name == "comic.cbz");
            archive.TryResolveItem("books", book.Id, out var bookEntry);
            archive.TryResolveItem("books", comic.Id, out var comicEntry);
            var doc = archive.List("documents", null).Items.Single(item => item.Name == "note.txt");
            seeded = new Seeded(book.Id, comic.Id, doc.Id,
                Key("books", bookEntry!.Id, bookEntry.SizeBytes, bookEntry.LastWriteTimeUtc),
                Key("books", comicEntry!.Id, comicEntry.SizeBytes, comicEntry.LastWriteTimeUtc));
            return Task.CompletedTask;
        });

        var json = new JsonSerializerOptions { WriteIndented = true };
        var highlight = new BookHighlightDto("note-1", "0", 5, 18, "first chapter", "before", "after", DateTimeOffset.UtcNow);
        File.WriteAllText(Path.Combine(NotesDirectory, "pereneArchiveBookHighlights.json"),
            JsonSerializer.Serialize(new Dictionary<string, List<BookHighlightDto>>
            {
                [seeded.BookKey] = [highlight],
                ["0000000000000000000000000000dead"] = [highlight with { Id = "orphan" }]
            }, json));
        File.WriteAllText(Path.Combine(NotesDirectory, "pereneArchiveBookNotes.txt"), string.Join("", new[]
        {
            "My Book (My Author)\n- Your Highlight on Chapter 1 | Location 5-18 | Added on Monday\n- Perene Archive Note ID: note-1\n\nfirst chapter\nsecond line\n==========\n",
            "My Book\n- Your Highlight on Chapter 1 | Location 1000 | Added on Monday\n- Perene Archive Note ID: orphan\n\nlost\n==========\n"
        }));
        File.WriteAllText(Path.Combine(NotesDirectory, "pereneArchiveBookProgress.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { [seeded.BookKey] = new { ChapterId = "1", WordOffset = 750 }, ["feedfeedfeedfeedfeedfeedfeedfeed"] = new { ChapterId = "0", WordOffset = 1 } }, json));
        File.WriteAllText(Path.Combine(NotesDirectory, "pereneArchiveComicProgress.json"),
            JsonSerializer.Serialize(new Dictionary<string, object> { [seeded.ComicKey] = new { PageIndex = 1 } }, json));
        File.WriteAllText(Path.Combine(NotesDirectory, "pereneArchiveReaderThemes.json"), JsonSerializer.Serialize(new
        {
            ActiveSettings = new { FontFamily = "Arial", FontSizePx = 24, LineHeight = 2.0, ForegroundColor = "#112233", BackgroundColor = "#AABBCC", ContentPaddingPercent = 10 },
            SelectedThemeId = "theme-1",
            Themes = new[]
            {
                new { Id = "theme-1", Name = "Night", Settings = new { FontFamily = "Arial", FontSizePx = 24, LineHeight = 2.0, ForegroundColor = "#112233", BackgroundColor = "#AABBCC", ContentPaddingPercent = 10 } },
                new { Id = "theme-bad", Name = "Broken", Settings = new { FontFamily = "Nope", FontSizePx = 1, LineHeight = 9.0, ForegroundColor = "red", BackgroundColor = "red", ContentPaddingPercent = 10 } }
            }
        }, json));
        File.WriteAllText(Path.Combine(_host.ArchivePath, "Dashboard", "pereneArchiveFavorites.json"),
            JsonSerializer.Serialize(new[] { new { Category = "documents", ItemId = seeded.DocId }, new { Category = "documents", ItemId = "stale" } }, json));
        File.WriteAllText(Path.Combine(_host.ArchivePath, "Dashboard", "pereneArchiveCustomStorageViews.json"),
            JsonSerializer.Serialize(new[] { new { Id = "view-1", CategoryKey = (string?)null, FolderId = (string?)null, IsWholeArchive = true, Title = "Archive", MaxSizeBytes = 2048L } }, json));
        return seeded;
    }

    private Dictionary<string, string> SnapshotLegacyFiles() =>
        Directory.EnumerateFiles(_host.ArchivePath, "pereneArchive*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private Task<LegacyImportReportDto> RunImportAsync(string actorId) =>
        _host.AsAsync(actorId, sp => sp.GetRequiredService<LegacyDataImporter>().RunAsync(actorId));

    [Fact]
    public async Task Import_moves_every_resolvable_record_into_the_first_admin_and_reports_totals()
    {
        var seeded = await SeedLegacyAsync();
        var bossId = (await WebApp.Tests.IdentityTestHost.FindUserAsync(_host.Factory, "admin"))!.Id;
        var other = await _host.AddUserAsync("zed", admin: true);

        var report = await RunImportAsync(other);

        Assert.True(report.BackupVerified);
        Assert.True(report.Verified);
        Assert.Equal(1, report.ImportedByKind["favorites"]);
        Assert.Equal(1, report.ImportedByKind["highlights"]);
        Assert.Equal(1, report.ImportedByKind["notes"]);
        Assert.Equal(1, report.ImportedByKind["reading-progress"]);
        Assert.Equal(1, report.ImportedByKind["comic-progress"]);
        Assert.Equal(1, report.ImportedByKind["reader-themes"]);
        Assert.Equal(1, report.ImportedByKind["storage-views"]);
        Assert.Equal(1, report.SkippedByKind["favorites"]);
        Assert.Equal(1, report.SkippedByKind["highlights"]);
        Assert.Equal(1, report.SkippedByKind["notes"]);
        Assert.Equal(1, report.SkippedByKind["reading-progress"]);
        Assert.Equal(1, report.SkippedByKind["reader-themes"]);
        Assert.Equal(report.ImportedByKind.Values.Sum(), report.Imported);
        Assert.DoesNotContain(_host.ArchivePath, JsonSerializer.Serialize(report));

        await _host.WithDbAsync(async (_, db) =>
        {
            Assert.All(await db.BookHighlights.ToListAsync(), row => Assert.Equal(bossId, row.UserId));
            var note = await db.BookNotes.SingleAsync();
            Assert.Equal("first chapter\nsecond line", note.Content);
            Assert.Equal(bossId, note.UserId);
            Assert.Equal(0, note.ChapterIndex);
            Assert.Equal(5, note.TextOffsetStart);
            Assert.Equal(18, note.TextOffsetEnd);
            Assert.Equal(750, (await db.ReadingProgresses.SingleAsync()).WordOffset);
            Assert.Equal(1, (await db.ComicProgresses.SingleAsync()).PageIndex);
            Assert.Equal("Night", (await db.ReaderThemes.SingleAsync()).Name);
            var preference = await db.ReaderThemePreferences.SingleAsync();
            Assert.Equal("theme-1", preference.SelectedThemePublicId);
            Assert.Equal(24, preference.FontSizePx);
            Assert.Equal("view-1", (await db.CustomStorageViews.SingleAsync()).PublicId);
            Assert.Equal(1, await db.Favorites.CountAsync());
            var run = await db.LegacyImportRuns.SingleAsync();
            Assert.True(run.Verified && run.BackupVerified);
            Assert.Equal(other, run.RanByUserId);
            Assert.Contains(await db.AuditEvents.ToListAsync(), audit => audit.Action == "legacy-import.completed" && !audit.Detail!.Contains(_host.ArchivePath));
        });

        // The imported data is what the user now sees through the live services.
        await _host.AsAsync(bossId, async sp =>
        {
            Assert.Equal(750, (await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", seeded.BookId, CancellationToken.None))!.WordOffset);
            Assert.Single(await sp.GetRequiredService<IEpubHighlightService>().LoadHighlightsAsync("books", seeded.BookId, CancellationToken.None));
            Assert.Contains(seeded.DocId, await sp.GetRequiredService<IArchiveFavoritesService>().GetFavoriteIdsAsync("documents", [seeded.DocId], CancellationToken.None));
        });
    }

    [Fact]
    public async Task Import_is_idempotent_and_leaves_the_legacy_files_byte_for_byte_untouched()
    {
        await SeedLegacyAsync();
        var adminId = await _host.AddUserAsync("zed", admin: true);
        var before = SnapshotLegacyFiles();
        Assert.NotEmpty(before);

        var first = await RunImportAsync(adminId);
        var second = await RunImportAsync(adminId);

        Assert.True(first.Imported > 0);
        Assert.Equal(0, second.Imported);
        Assert.Equal(first.Imported + first.Skipped, second.Skipped);
        Assert.Equal(before, SnapshotLegacyFiles());
        await _host.WithDbAsync(async (_, db) =>
        {
            Assert.Equal(1, await db.BookNotes.CountAsync());
            Assert.Equal(1, await db.Favorites.CountAsync());
            Assert.Equal(2, await db.LegacyImportRuns.CountAsync());
        });
    }

    [Fact]
    public async Task Import_goes_to_the_first_admin_by_creation_time()
    {
        await SeedLegacyAsync();
        var bossId = (await WebApp.Tests.IdentityTestHost.FindUserAsync(_host.Factory, "admin"))!.Id;
        await _host.AddUserAsync("alice");
        var aliceId = (await WebApp.Tests.IdentityTestHost.FindUserAsync(_host.Factory, "alice"))!.Id;

        await RunImportAsync(aliceId);

        await _host.WithDbAsync(async (_, db) => Assert.All(await db.Favorites.ToListAsync(), row => Assert.Equal(bossId, row.UserId)));
    }

    [Fact]
    public async Task Nothing_is_imported_when_the_backup_cannot_be_verified()
    {
        // A file where the backup directory should be makes the backup impossible.
        var blocker = Path.Combine(_backups, "blocker");
        File.WriteAllText(blocker, "file");
        using var blocked = new MediaTestHost(new Dictionary<string, string?> { ["Backup:Path"] = blocker });
        var bossId = await blocked.AddUserAsync("boss", admin: true);
        Directory.CreateDirectory(blocked.Factory.KeysPath);

        await Assert.ThrowsAsync<LegacyImportBlockedException>(() =>
            blocked.AsAsync(bossId, sp => sp.GetRequiredService<LegacyDataImporter>().RunAsync(bossId)));

        await blocked.WithDbAsync(async (_, db) =>
        {
            Assert.Empty(await db.LegacyImportRuns.ToListAsync());
            Assert.DoesNotContain(await db.AuditEvents.ToListAsync(), audit => audit.Action == "legacy-import.completed");
        });
    }

    [Fact]
    public async Task Import_needs_an_active_admin()
    {
        var alice = await _host.AddUserAsync("alice");
        // The seeded initial administrator is the only Admin; with it deactivated nobody can receive the data.
        await _host.WithDbAsync(async (_, db) =>
        {
            (await db.Users.SingleAsync(user => user.UserName == "admin")).IsActive = false;
            await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<LegacyImportBlockedException>(() => RunImportAsync(alice));
    }

    [Fact]
    public async Task Import_with_no_legacy_files_succeeds_with_empty_totals()
    {
        var adminId = await _host.AddUserAsync("zed", admin: true);

        var report = await RunImportAsync(adminId);

        Assert.Equal(0, report.Imported);
        Assert.Equal(0, report.Skipped);
        Assert.True(report.Verified);
    }

    [Fact]
    public async Task The_last_run_is_reported_after_an_import()
    {
        var adminId = await _host.AddUserAsync("zed", admin: true);
        await _host.AsAsync(adminId, async sp => Assert.Null(await sp.GetRequiredService<LegacyDataImporter>().LastRunAsync()));

        await RunImportAsync(adminId);

        await _host.AsAsync(adminId, async sp =>
        {
            var last = await sp.GetRequiredService<LegacyDataImporter>().LastRunAsync();
            Assert.True(last!.Verified && last.BackupVerified);
        });
    }
}
