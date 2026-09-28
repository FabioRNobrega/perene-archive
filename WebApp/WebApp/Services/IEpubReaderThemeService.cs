using WebApp.Client.Models;

namespace WebApp.Services;

internal interface IEpubReaderThemeService
{
    Task<BookReaderThemeLibraryDto> LoadAsync(CancellationToken cancellationToken);
    Task<BookReaderThemeLibraryDto> SaveActiveAsync(BookReaderThemeSettingsDto settings, CancellationToken cancellationToken);
    Task<BookReaderThemeLibraryDto> CreateAsync(CreateBookReaderThemeRequest request, CancellationToken cancellationToken);
    Task<BookReaderThemeLibraryDto?> UpdateAsync(string id, UpdateBookReaderThemeRequest request, CancellationToken cancellationToken);
    Task<BookReaderThemeLibraryDto?> DeleteAsync(string id, CancellationToken cancellationToken);
}
