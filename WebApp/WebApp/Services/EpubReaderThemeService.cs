using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed partial class EpubReaderThemeService(IOptions<ArchiveRootOptions> options) : IEpubReaderThemeService
{
    private const string FileName = "pereneArchiveReaderThemes.json";
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly string[] Fonts = ["Montserrat", "Arial", "Times New Roman"];
    private static readonly int[] Sizes = [14, 16, 18, 20, 22, 24, 28];
    private static readonly double[] LineHeights = [1.4, 1.75, 2.0, 2.4];
    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public static BookReaderThemeSettingsDto DefaultSettings { get; } = new("Montserrat", 18, 1.75, null, null, 5);

    public Task<BookReaderThemeLibraryDto> LoadAsync(CancellationToken cancellationToken) => MutateAsync(static document => document, false, cancellationToken);

    public Task<BookReaderThemeLibraryDto> SaveActiveAsync(BookReaderThemeSettingsDto settings, CancellationToken cancellationToken) =>
        MutateAsync(document =>
        {
            var valid = ValidateSettings(settings);
            document.ActiveSettings = valid;
            document.SelectedThemeId = document.Themes.Any(theme => theme.Id == document.SelectedThemeId && theme.Settings == valid)
                ? document.SelectedThemeId : null;
            return document;
        }, true, cancellationToken);

    public Task<BookReaderThemeLibraryDto> CreateAsync(CreateBookReaderThemeRequest request, CancellationToken cancellationToken) =>
        MutateAsync(document =>
        {
            var name = ValidateName(request.Name, document.Themes, null);
            var theme = new BookReaderThemeDto(Guid.NewGuid().ToString("N"), name, ValidateSettings(request.Settings));
            document.Themes.Add(theme); document.ActiveSettings = theme.Settings; document.SelectedThemeId = theme.Id;
            return document;
        }, true, cancellationToken);

    public async Task<BookReaderThemeLibraryDto?> UpdateAsync(string id, UpdateBookReaderThemeRequest request, CancellationToken cancellationToken)
    {
        BookReaderThemeLibraryDto? result = null;
        await MutateAsync(document =>
        {
            var index = document.Themes.FindIndex(theme => theme.Id == id);
            if (index < 0) return document;
            var old = document.Themes[index];
            var updated = new BookReaderThemeDto(id, request.Name is null ? old.Name : ValidateName(request.Name, document.Themes, id), ValidateSettings(request.Settings));
            document.Themes[index] = updated; document.ActiveSettings = updated.Settings; document.SelectedThemeId = id; result = ToDto(document);
            return document;
        }, true, cancellationToken);
        return result;
    }

    public async Task<BookReaderThemeLibraryDto?> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        BookReaderThemeLibraryDto? result = null;
        await MutateAsync(document =>
        {
            var removed = document.Themes.RemoveAll(theme => theme.Id == id) > 0;
            if (removed) { if (document.SelectedThemeId == id) document.SelectedThemeId = null; result = ToDto(document); }
            return document;
        }, true, cancellationToken);
        return result;
    }

    private async Task<BookReaderThemeLibraryDto> MutateAsync(Func<Document, Document> mutate, bool write, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try { var document = await ReadAsync(cancellationToken); document = mutate(document); if (write) await WriteAsync(document, cancellationToken); return ToDto(document); }
        finally { _fileLock.Release(); }
    }

    private async Task<Document> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(_archiveRootPath, "Books", "Notes", FileName);
            if (!File.Exists(path)) return new Document(DefaultSettings, null, []);
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<Document>(stream, cancellationToken: cancellationToken);
            return Normalize(document);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or FormatException or ArgumentException) { return new Document(DefaultSettings, null, []); }
    }

    private async Task WriteAsync(Document document, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(_archiveRootPath, "Books", "Notes"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName); var temp = Path.Combine(folder, $".{FileName}.{Guid.NewGuid():N}.tmp");
        await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, document, SerializerOptions, cancellationToken);
        File.Move(temp, path, true);
    }

    private static Document Normalize(Document? document)
    {
        if (document is null) return new(DefaultSettings, null, []);
        try
        {
            var themes = document.Themes?.Select(theme => new BookReaderThemeDto(theme.Id, ValidateName(theme.Name, [], null), ValidateSettings(theme.Settings))).ToList() ?? [];
            if (themes.Select(theme => theme.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != themes.Count || themes.Any(theme => string.IsNullOrWhiteSpace(theme.Id))) throw new FormatException();
            var active = ValidateSettings(document.ActiveSettings); var selected = themes.Any(theme => theme.Id == document.SelectedThemeId) ? document.SelectedThemeId : null;
            return new(active, selected, themes);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or NullReferenceException) { return new(DefaultSettings, null, []); }
    }

    private static BookReaderThemeSettingsDto ValidateSettings(BookReaderThemeSettingsDto settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Fonts.Contains(settings.FontFamily, StringComparer.Ordinal) || !Sizes.Contains(settings.FontSizePx) || !LineHeights.Contains(settings.LineHeight) || !ValidColor(settings.ForegroundColor) || !ValidColor(settings.BackgroundColor)) throw new ArgumentException("Unsupported reader theme settings.");
        if (settings.ContentPaddingPercent is not null && (settings.ContentPaddingPercent < 5 || settings.ContentPaddingPercent > 30 || settings.ContentPaddingPercent % 5 != 0)) throw new ArgumentException("Unsupported reader theme settings.");
        return settings with { ForegroundColor = NormalizeColor(settings.ForegroundColor), BackgroundColor = NormalizeColor(settings.BackgroundColor), ContentPaddingPercent = settings.ContentPaddingPercent ?? 5 };
    }
    private static bool ValidColor(string? color) => color is null || ColorPattern().IsMatch(color);
    private static string? NormalizeColor(string? color) => color?.ToUpperInvariant();
    private static string ValidateName(string? name, IEnumerable<BookReaderThemeDto> themes, string? exceptId)
    { var trimmed = name?.Trim(); if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 60) throw new ArgumentException("Theme names must be 1 to 60 characters."); if (themes.Any(theme => theme.Id != exceptId && string.Equals(theme.Name, trimmed, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("A theme with that name already exists."); return trimmed; }
    private static BookReaderThemeLibraryDto ToDto(Document document) => new(document.ActiveSettings, document.SelectedThemeId, document.Themes.ToArray());
    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")] private static partial Regex ColorPattern();
    private sealed class Document { public Document() : this(DefaultSettings, null, []) { } public Document(BookReaderThemeSettingsDto activeSettings, string? selectedThemeId, List<BookReaderThemeDto> themes) { ActiveSettings = activeSettings; SelectedThemeId = selectedThemeId; Themes = themes; } public BookReaderThemeSettingsDto ActiveSettings { get; set; } public string? SelectedThemeId { get; set; } public List<BookReaderThemeDto> Themes { get; set; } }
}
