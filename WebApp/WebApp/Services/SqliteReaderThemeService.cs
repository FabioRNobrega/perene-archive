using Microsoft.EntityFrameworkCore;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

/// <summary>Reader themes are shared: everyone sees and can apply every theme, only the creator edits or deletes one, and names may repeat. Each user keeps their own active settings and selection.</summary>
internal sealed class SqliteReaderThemeService(AppDbContext db, ICurrentUser currentUser) : IEpubReaderThemeService
{
    public async Task<BookReaderThemeLibraryDto> LoadAsync(CancellationToken cancellationToken) =>
        await BuildAsync(currentUser.RequireUserId(), cancellationToken);

    public async Task<BookReaderThemeLibraryDto> SaveActiveAsync(BookReaderThemeSettingsDto settings, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var valid = ReaderThemeRules.ValidateSettings(settings);
        var themes = await VisibleThemesAsync(userId, cancellationToken);
        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        Apply(preference, valid);
        // Applying a saved theme sends its settings, so the selection follows the settings: keep the current one if it still matches,
        // otherwise pick the oldest shared theme with exactly these settings, otherwise none.
        preference.SelectedThemePublicId = themes.Any(theme => theme.Id == preference.SelectedThemePublicId && theme.Settings == valid)
            ? preference.SelectedThemePublicId
            : themes.FirstOrDefault(theme => theme.Settings == valid)?.Id;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildAsync(userId, cancellationToken);
    }

    public async Task<BookReaderThemeLibraryDto> CreateAsync(CreateBookReaderThemeRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var name = ReaderThemeRules.ValidateName(request.Name);
        var settings = ReaderThemeRules.ValidateSettings(request.Settings);
        var row = new ReaderTheme { PublicId = Guid.NewGuid().ToString("N"), CreatedByUserId = userId, Name = name, FontFamily = settings.FontFamily, CreatedUtc = DateTimeOffset.UtcNow };
        Apply(row, settings);
        db.ReaderThemes.Add(row);
        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        Apply(preference, settings);
        preference.SelectedThemePublicId = row.PublicId;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildAsync(userId, cancellationToken);
    }

    public async Task<BookReaderThemeLibraryDto?> UpdateAsync(string id, UpdateBookReaderThemeRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var row = await db.ReaderThemes.FirstOrDefaultAsync(theme => theme.PublicId == id && theme.CreatedByUserId == userId, cancellationToken);
        if (row is null) return null;

        var settings = ReaderThemeRules.ValidateSettings(request.Settings);
        if (request.Name is not null) row.Name = ReaderThemeRules.ValidateName(request.Name);
        Apply(row, settings);
        var preference = await GetOrCreatePreferenceAsync(userId, cancellationToken);
        Apply(preference, settings);
        preference.SelectedThemePublicId = id;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildAsync(userId, cancellationToken);
    }

    public async Task<BookReaderThemeLibraryDto?> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var row = await db.ReaderThemes.FirstOrDefaultAsync(theme => theme.PublicId == id && theme.CreatedByUserId == userId, cancellationToken);
        if (row is null) return null;

        db.ReaderThemes.Remove(row);
        var preference = await db.ReaderThemePreferences.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preference?.SelectedThemePublicId == id) preference.SelectedThemePublicId = null;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildAsync(userId, cancellationToken);
    }

    private async Task<List<BookReaderThemeDto>> VisibleThemesAsync(string userId, CancellationToken cancellationToken)
    {
        var rows = await db.ReaderThemes.AsNoTracking().ToListAsync(cancellationToken);
        // SQLite cannot order by DateTimeOffset, so order after loading.
        return rows.OrderBy(theme => theme.CreatedUtc).ThenBy(theme => theme.Id)
            .Select(row => new BookReaderThemeDto(row.PublicId, row.Name, ToSettings(row), row.CreatedByUserId == userId)).ToList();
    }

    private async Task<BookReaderThemeLibraryDto> BuildAsync(string userId, CancellationToken cancellationToken)
    {
        var themes = await VisibleThemesAsync(userId, cancellationToken);
        var preference = await db.ReaderThemePreferences.AsNoTracking().FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preference is null) return new BookReaderThemeLibraryDto(ReaderThemeRules.DefaultSettings, null, themes);
        var selected = themes.Any(theme => theme.Id == preference.SelectedThemePublicId) ? preference.SelectedThemePublicId : null;
        return new BookReaderThemeLibraryDto(ToSettings(preference), selected, themes);
    }

    private async Task<ReaderThemePreference> GetOrCreatePreferenceAsync(string userId, CancellationToken cancellationToken)
    {
        var preference = await db.ReaderThemePreferences.FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (preference is not null) return preference;
        preference = new ReaderThemePreference { UserId = userId, FontFamily = ReaderThemeRules.DefaultSettings.FontFamily };
        db.ReaderThemePreferences.Add(preference);
        return preference;
    }

    private static BookReaderThemeSettingsDto ToSettings(ReaderTheme row) =>
        new(row.FontFamily, row.FontSizePx, row.LineHeight, row.ForegroundColor, row.BackgroundColor, row.ContentPaddingPercent);

    private static BookReaderThemeSettingsDto ToSettings(ReaderThemePreference row) =>
        new(row.FontFamily, row.FontSizePx, row.LineHeight, row.ForegroundColor, row.BackgroundColor, row.ContentPaddingPercent);

    private static void Apply(ReaderTheme row, BookReaderThemeSettingsDto settings)
    {
        row.FontFamily = settings.FontFamily;
        row.FontSizePx = settings.FontSizePx;
        row.LineHeight = settings.LineHeight;
        row.ForegroundColor = settings.ForegroundColor;
        row.BackgroundColor = settings.BackgroundColor;
        row.ContentPaddingPercent = settings.ContentPaddingPercent ?? 5;
    }

    private static void Apply(ReaderThemePreference row, BookReaderThemeSettingsDto settings)
    {
        row.FontFamily = settings.FontFamily;
        row.FontSizePx = settings.FontSizePx;
        row.LineHeight = settings.LineHeight;
        row.ForegroundColor = settings.ForegroundColor;
        row.BackgroundColor = settings.BackgroundColor;
        row.ContentPaddingPercent = settings.ContentPaddingPercent ?? 5;
    }
}
