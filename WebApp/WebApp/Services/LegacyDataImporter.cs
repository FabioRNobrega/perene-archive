using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Identity;
using WebApp.Security;

namespace WebApp.Services;

/// <summary>The import cannot run safely (no Admin, or the pre-import backup could not be verified); nothing was imported.</summary>
internal sealed class LegacyImportBlockedException(string message) : Exception(message);

/// <summary>
/// One idempotent Admin operation that moves the legacy shared JSON/text files into SQLite, attributing everything to the first Admin.
/// It first takes and verifies a database backup (no verified backup, no import), only ever opens the legacy files for reading, matches
/// every legacy record against current media, reports imported/skipped totals, and records the run. Re-running imports nothing twice.
/// </summary>
internal sealed partial class LegacyDataImporter(
    AppDbContext db,
    IOptions<ArchiveRootOptions> archiveOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IConfiguration configuration,
    IArchiveService archive,
    FolderLocator locator,
    MediaReconciliationService reconciliation)
{
    private const string NotesFileName = "pereneArchiveBookNotes.txt";
    private const string HighlightsFileName = "pereneArchiveBookHighlights.json";
    private const string BookProgressFileName = "pereneArchiveBookProgress.json";
    private const string ComicProgressFileName = "pereneArchiveComicProgress.json";
    private const string ThemesFileName = "pereneArchiveReaderThemes.json";
    private const string FavoritesFileName = "pereneArchiveFavorites.json";
    private const string ViewsFileName = "pereneArchiveCustomStorageViews.json";
    private const string NoteSeparator = "==========";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _root = Path.GetFullPath(archiveOptions.Value.Path);

    public async Task<LegacyImportReportDto?> LastRunAsync(CancellationToken cancellationToken = default)
    {
        var run = (await db.LegacyImportRuns.AsNoTracking().ToListAsync(cancellationToken)).OrderByDescending(item => item.Id).FirstOrDefault();
        return run is null ? null : new LegacyImportReportDto(run.RanUtc, run.BackupVerified, run.Verified, run.Imported, run.Skipped, new Dictionary<string, int>(), new Dictionary<string, int>());
    }

    public async Task<LegacyImportReportDto> RunAsync(string actorUserId, CancellationToken cancellationToken = default)
    {
        var target = await FirstAdminAsync(cancellationToken)
            ?? throw new LegacyImportBlockedException("An active administrator account is required to receive the imported data.");

        var backup = await TakeVerifiedBackupAsync(cancellationToken);
        var sources = ReadSources();
        var counts = new ImportCounts();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var candidates = BuildCandidates();
            var noteItems = new Dictionary<string, MediaItem>(StringComparer.Ordinal);
            await ImportFavoritesAsync(sources.Favorites, target.Id, counts, cancellationToken);
            await ImportHighlightsAsync(sources.Highlights, candidates, target.Id, noteItems, counts, cancellationToken);
            await ImportNotesAsync(sources.Notes, noteItems, target.Id, counts, cancellationToken);
            await ImportBookProgressAsync(sources.BookProgress, candidates, target.Id, counts, cancellationToken);
            await ImportComicProgressAsync(sources.ComicProgress, candidates, target.Id, counts, cancellationToken);
            await ImportThemesAsync(sources.Themes, target.Id, counts, cancellationToken);
            await ImportViewsAsync(sources.Views, target.Id, counts, cancellationToken);

            var verified = await VerifyAsync(sources, target.Id, counts, cancellationToken);
            var run = new LegacyImportRun
            {
                RanUtc = DateTimeOffset.UtcNow,
                RanByUserId = actorUserId,
                BackupName = Path.GetFileName(backup),
                BackupVerified = true,
                Imported = counts.Imported.Values.Sum(),
                Skipped = counts.Skipped.Values.Sum(),
                Verified = verified
            };
            db.LegacyImportRuns.Add(run);
            db.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = run.RanUtc,
                Action = "legacy-import.completed",
                ActorUserId = actorUserId,
                TargetUserId = target.Id,
                TargetUserName = target.UserName,
                Detail = $"imported={run.Imported}; skipped={run.Skipped}; verified={run.Verified}"
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LegacyImportReportDto(run.RanUtc, true, verified, run.Imported, run.Skipped, counts.Imported, counts.Skipped);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ApplicationUser?> FirstAdminAsync(CancellationToken cancellationToken) =>
        (await (from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                join user in db.Users on userRole.UserId equals user.Id
                where role.Name == AccountLifecycleService.AdminRole && user.IsActive
                select user).ToListAsync(cancellationToken)).OrderBy(user => user.CreatedUtc).ThenBy(user => user.UserName, StringComparer.Ordinal).FirstOrDefault();

    private async Task<string> TakeVerifiedBackupAsync(CancellationToken cancellationToken)
    {
        var destination = configuration["Backup:Path"] is { Length: > 0 } configured ? configured : AdminCli.DefaultBackupPath;
        try
        {
            var result = await new DatabaseBackupService().CreateAsync(
                databaseOptions.Value.Path, DataProtectionConfiguration.ResolveKeysPath(configuration), destination, cancellationToken);
            return result.Directory;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            throw new LegacyImportBlockedException("A verified backup could not be created, so nothing was imported.");
        }
    }

    // ---- reading the legacy files (read-only) ----------------------------------------------------------------------------

    private sealed record Sources(
        List<FavoriteRecord> Favorites,
        Dictionary<string, List<BookHighlightDto>> Highlights,
        List<LegacyNote> Notes,
        Dictionary<string, BookProgressRecord> BookProgress,
        Dictionary<string, ComicProgressRecord> ComicProgress,
        ThemeDocument? Themes,
        List<ViewRecord> Views);

    private Sources ReadSources() => new(
        ReadJson<List<FavoriteRecord>>(Path.Combine(_root, "Dashboard", FavoritesFileName)) ?? [],
        ReadJson<Dictionary<string, List<BookHighlightDto>>>(NotesPath(HighlightsFileName)) ?? [],
        ReadNotes(NotesPath(NotesFileName)),
        ReadJson<Dictionary<string, BookProgressRecord>>(NotesPath(BookProgressFileName)) ?? [],
        ReadJson<Dictionary<string, ComicProgressRecord>>(NotesPath(ComicProgressFileName)) ?? [],
        ReadJson<ThemeDocument>(NotesPath(ThemesFileName)),
        ReadJson<List<ViewRecord>>(Path.Combine(_root, "Dashboard", ViewsFileName)) ?? []);

    private string NotesPath(string fileName) => Path.Combine(_root, "Books", "Notes", fileName);

    private static T? ReadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static List<LegacyNote> ReadNotes(string path)
    {
        var notes = new List<LegacyNote>();
        if (!File.Exists(path)) return notes;
        string content;
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
            content = reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return notes;
        }

        foreach (var entry in content.Split(NoteSeparator, StringSplitOptions.None))
        {
            var lines = entry.Replace("\r\n", "\n").Split('\n').ToList();
            var markerIndex = lines.FindIndex(line => line.StartsWith("- Perene Archive Note ID: ", StringComparison.Ordinal));
            if (markerIndex < 0) continue;
            var noteId = lines[markerIndex]["- Perene Archive Note ID: ".Length..].Trim();
            var header = lines.Take(markerIndex).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            var title = header.FirstOrDefault()?.Trim() ?? string.Empty;
            var metadata = header.Skip(1).FirstOrDefault() ?? string.Empty;
            var text = string.Join('\n', lines.Skip(markerIndex + 1)).Trim('\n', '\r', ' ');
            if (noteId.Length == 0 || text.Length == 0) continue;

            var chapter = ChapterPattern().Match(metadata);
            var location = LocationPattern().Match(metadata);
            notes.Add(new LegacyNote(
                noteId, title,
                chapter.Success ? Math.Max(0, int.Parse(chapter.Groups[1].Value, CultureInfo.InvariantCulture) - 1) : 0,
                location.Success ? int.Parse(location.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                location.Success ? int.Parse(location.Groups[2].Value, CultureInfo.InvariantCulture) : null,
                text));
        }

        return notes;
    }

    [GeneratedRegex(@"Chapter (\d+)")]
    private static partial Regex ChapterPattern();

    [GeneratedRegex(@"Location (\d+)-(\d+)")]
    private static partial Regex LocationPattern();

    // ---- matching legacy keys to current media ---------------------------------------------------------------------------

    /// <summary>The legacy hash key of every current book/comic file, so keyed records resolve against the files that exist now.</summary>
    private Dictionary<string, ArchiveItemRef> BuildCandidates()
    {
        var candidates = new Dictionary<string, ArchiveItemRef>(StringComparer.Ordinal);
        foreach (var root in locator.Roots.Where(root => root.IsArchiveCategory && Directory.Exists(root.Path)))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var path in Directory.EnumerateFiles(root.Path, "*", options))
            {
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".epub", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".cbz", StringComparison.OrdinalIgnoreCase)) continue;
                var itemId = archive.ComputeItemId(root.Key, path);
                if (!archive.TryResolveItem(root.Key, itemId, out var entry) || entry is null) continue;
                candidates[LegacyKey(root.Key, entry.Id, entry.SizeBytes, entry.LastWriteTimeUtc)] = new ArchiveItemRef(root.Key, entry);
            }
        }

        return candidates;
    }

    private static string LegacyKey(string categoryKey, string itemId, long? sizeBytes, DateTime lastWriteTimeUtc) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{categoryKey}:{itemId}:{sizeBytes ?? 0}:{lastWriteTimeUtc.Ticks}")))[..32].ToLowerInvariant();

    private async Task<MediaItem?> EnsureAsync(WebApp.Models.ArchiveItemEntry entry, CancellationToken cancellationToken)
    {
        var file = reconciliation.Describe(entry);
        return file is null ? null : await reconciliation.EnsureAsync(file, cancellationToken);
    }

    // ---- importing each kind ---------------------------------------------------------------------------------------------

    private async Task ImportFavoritesAsync(List<FavoriteRecord> records, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            if (!archive.TryResolveItem(record.Category, record.ItemId, out var entry) || entry is null || await EnsureAsync(entry, cancellationToken) is not { } item)
            {
                counts.Skip("favorites");
                continue;
            }

            if (await db.Favorites.AnyAsync(favorite => favorite.UserId == userId && favorite.MediaItemId == item.Id, cancellationToken))
            {
                counts.Skip("favorites");
                continue;
            }

            db.Favorites.Add(new Favorite { UserId = userId, MediaItemId = item.Id, CreatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("favorites");
        }
    }

    private async Task ImportHighlightsAsync(
        Dictionary<string, List<BookHighlightDto>> highlights, Dictionary<string, ArchiveItemRef> candidates, string userId,
        Dictionary<string, MediaItem> noteItems, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var (key, list) in highlights)
        {
            MediaItem? item = null;
            if (candidates.TryGetValue(key, out var candidate)) item = await EnsureAsync(candidate.Entry, cancellationToken);
            foreach (var highlight in list)
            {
                if (item is null)
                {
                    counts.Skip("highlights");
                    continue;
                }

                noteItems[highlight.Id] = item;
                if (await db.BookHighlights.AnyAsync(row => row.UserId == userId && row.HighlightKey == highlight.Id, cancellationToken))
                {
                    counts.Skip("highlights");
                    continue;
                }

                db.BookHighlights.Add(new BookHighlight
                {
                    HighlightKey = highlight.Id, UserId = userId, MediaItemId = item.Id, ChapterId = highlight.ChapterId,
                    TextOffsetStart = highlight.TextOffsetStart, TextOffsetEnd = highlight.TextOffsetEnd, SelectedText = highlight.SelectedText,
                    ContextBefore = highlight.ContextBefore ?? string.Empty, ContextAfter = highlight.ContextAfter ?? string.Empty,
                    ContentRevision = item.ContentRevision, CreatedUtc = highlight.SavedAt
                });
                await db.SaveChangesAsync(cancellationToken);
                counts.Import("highlights");
            }
        }
    }

    private async Task ImportNotesAsync(List<LegacyNote> notes, Dictionary<string, MediaItem> noteItems, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var note in notes)
        {
            // A note belongs to the book its highlight belongs to; a note whose highlight cannot be resolved has no safe owner item.
            if (!noteItems.TryGetValue(note.NoteId, out var item))
            {
                counts.Skip("notes");
                continue;
            }

            if (await db.BookNotes.AnyAsync(row => row.UserId == userId && row.NoteKey == note.NoteId, cancellationToken))
            {
                counts.Skip("notes");
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            db.BookNotes.Add(new BookNote
            {
                NoteKey = note.NoteId, UserId = userId, MediaItemId = item.Id, BookTitle = note.Title.Length == 0 ? "Untitled" : note.Title,
                ChapterIndex = note.ChapterIndex, TextOffsetStart = note.Start, TextOffsetEnd = note.End, Content = note.Text, CreatedUtc = now, UpdatedUtc = now
            });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("notes");
        }
    }

    private async Task ImportBookProgressAsync(
        Dictionary<string, BookProgressRecord> records, Dictionary<string, ArchiveItemRef> candidates, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var (key, record) in records)
        {
            if (!candidates.TryGetValue(key, out var candidate) || await EnsureAsync(candidate.Entry, cancellationToken) is not { } item || string.IsNullOrWhiteSpace(record.ChapterId))
            {
                counts.Skip("reading-progress");
                continue;
            }

            if (await db.ReadingProgresses.AnyAsync(row => row.UserId == userId && row.MediaItemId == item.Id, cancellationToken))
            {
                counts.Skip("reading-progress");
                continue;
            }

            db.ReadingProgresses.Add(new ReadingProgress
            {
                UserId = userId, MediaItemId = item.Id, ChapterId = record.ChapterId, WordOffset = Math.Max(0, record.WordOffset ?? 0),
                ScrollFraction = record.ScrollFraction, ContentRevision = item.ContentRevision, UpdatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("reading-progress");
        }
    }

    private async Task ImportComicProgressAsync(
        Dictionary<string, ComicProgressRecord> records, Dictionary<string, ArchiveItemRef> candidates, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var (key, record) in records)
        {
            if (!candidates.TryGetValue(key, out var candidate) || await EnsureAsync(candidate.Entry, cancellationToken) is not { } item)
            {
                counts.Skip("comic-progress");
                continue;
            }

            if (await db.ComicProgresses.AnyAsync(row => row.UserId == userId && row.MediaItemId == item.Id, cancellationToken))
            {
                counts.Skip("comic-progress");
                continue;
            }

            db.ComicProgresses.Add(new ComicProgress { UserId = userId, MediaItemId = item.Id, PageIndex = Math.Max(0, record.PageIndex), UpdatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("comic-progress");
        }
    }

    private async Task ImportThemesAsync(ThemeDocument? document, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        if (document is null) return;
        var imported = false;
        foreach (var theme in document.Themes ?? [])
        {
            BookReaderThemeSettingsDto settings;
            try { settings = ReaderThemeRules.ValidateSettings(theme.Settings); }
            catch (ArgumentException) { counts.Skip("reader-themes"); continue; }

            var name = theme.Name?.Trim();
            if (string.IsNullOrWhiteSpace(theme.Id) || string.IsNullOrWhiteSpace(name) || name.Length > 60 ||
                await db.ReaderThemes.AnyAsync(row => row.PublicId == theme.Id, cancellationToken))
            {
                counts.Skip("reader-themes");
                continue;
            }

            db.ReaderThemes.Add(new ReaderTheme
            {
                PublicId = theme.Id, CreatedByUserId = userId, Name = name, FontFamily = settings.FontFamily, FontSizePx = settings.FontSizePx, LineHeight = settings.LineHeight,
                ForegroundColor = settings.ForegroundColor, BackgroundColor = settings.BackgroundColor, ContentPaddingPercent = settings.ContentPaddingPercent ?? 5,
                CreatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("reader-themes");
            imported = true;
        }

        if (document.ActiveSettings is not null && await db.ReaderThemePreferences.AnyAsync(row => row.UserId == userId, cancellationToken))
        {
            counts.Skip("reader-theme-settings");
        }
        else if (document.ActiveSettings is not null)
        {
            try
            {
                var active = ReaderThemeRules.ValidateSettings(document.ActiveSettings);
                var selected = document.SelectedThemeId is { } id && await db.ReaderThemes.AnyAsync(row => row.PublicId == id, cancellationToken) ? id : null;
                db.ReaderThemePreferences.Add(new ReaderThemePreference
                {
                    UserId = userId, FontFamily = active.FontFamily, FontSizePx = active.FontSizePx, LineHeight = active.LineHeight, ForegroundColor = active.ForegroundColor,
                    BackgroundColor = active.BackgroundColor, ContentPaddingPercent = active.ContentPaddingPercent ?? 5, SelectedThemePublicId = selected
                });
                await db.SaveChangesAsync(cancellationToken);
                if (imported || selected is not null) counts.Import("reader-theme-settings");
            }
            catch (ArgumentException)
            {
                counts.Skip("reader-theme-settings");
            }
        }
    }

    private async Task ImportViewsAsync(List<ViewRecord> records, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.Title) || record.MaxSizeBytes <= 0 ||
                await db.CustomStorageViews.AnyAsync(row => row.PublicId == record.Id, cancellationToken))
            {
                counts.Skip("storage-views");
                continue;
            }

            db.CustomStorageViews.Add(new CustomStorageView
            {
                PublicId = record.Id, UserId = userId, CategoryKey = record.CategoryKey, FolderId = record.FolderId, IsWholeArchive = record.IsWholeArchive,
                Title = record.Title, MaxSizeBytes = record.MaxSizeBytes, CreatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
            counts.Import("storage-views");
        }
    }

    /// <summary>Verified means every imported record is now readable from the database for the receiving user.</summary>
    private async Task<bool> VerifyAsync(Sources sources, string userId, ImportCounts counts, CancellationToken cancellationToken)
    {
        async Task<bool> AtLeast(string kind, Task<int> total) => counts.Imported.GetValueOrDefault(kind) <= await total;
        return await AtLeast("favorites", db.Favorites.CountAsync(row => row.UserId == userId, cancellationToken))
            && await AtLeast("highlights", db.BookHighlights.CountAsync(row => row.UserId == userId, cancellationToken))
            && await AtLeast("notes", db.BookNotes.CountAsync(row => row.UserId == userId, cancellationToken))
            && await AtLeast("reading-progress", db.ReadingProgresses.CountAsync(row => row.UserId == userId, cancellationToken))
            && await AtLeast("comic-progress", db.ComicProgresses.CountAsync(row => row.UserId == userId, cancellationToken))
            && await AtLeast("reader-themes", db.ReaderThemes.CountAsync(row => row.CreatedByUserId == userId, cancellationToken))
            && await AtLeast("storage-views", db.CustomStorageViews.CountAsync(row => row.UserId == userId, cancellationToken));
    }

    // ---- legacy record shapes --------------------------------------------------------------------------------------------

    private sealed record FavoriteRecord(string Category, string ItemId);
    private sealed record BookProgressRecord(string ChapterId, int? WordOffset = null, double? ScrollFraction = null);
    private sealed record ComicProgressRecord(int PageIndex);
    private sealed record ViewRecord(string Id, string? CategoryKey, string? FolderId, bool IsWholeArchive, string Title, long MaxSizeBytes);
    private sealed record LegacyNote(string NoteId, string Title, int ChapterIndex, int? Start, int? End, string Text);
    private sealed record ArchiveItemRef(string CategoryKey, WebApp.Models.ArchiveItemEntry Entry);

    private sealed class ThemeDocument
    {
        public BookReaderThemeSettingsDto? ActiveSettings { get; set; }
        public string? SelectedThemeId { get; set; }
        public List<BookReaderThemeDto>? Themes { get; set; }
    }

    private sealed class ImportCounts
    {
        public Dictionary<string, int> Imported { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Skipped { get; } = new(StringComparer.Ordinal);
        public void Import(string kind) => Imported[kind] = Imported.GetValueOrDefault(kind) + 1;
        public void Skip(string kind) => Skipped[kind] = Skipped.GetValueOrDefault(kind) + 1;
    }
}
