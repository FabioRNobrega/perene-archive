namespace WebApp.Client.Models;

public sealed record BookReaderThemeLibraryDto(BookReaderThemeSettingsDto ActiveSettings, string? SelectedThemeId, IReadOnlyList<BookReaderThemeDto> Themes);

public sealed record CreateBookReaderThemeRequest(string Name, BookReaderThemeSettingsDto Settings);

public sealed record UpdateBookReaderThemeRequest(string? Name, BookReaderThemeSettingsDto Settings);
