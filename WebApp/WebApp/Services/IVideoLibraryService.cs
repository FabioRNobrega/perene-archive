using WebApp.Models;

namespace WebApp.Services;

internal interface IVideoLibraryService
{
    Task<IReadOnlyList<VideoFileEntry>> ScanAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<VideoFileEntry> GetCurrentSnapshot();

    bool TryResolve(string id, out VideoFileEntry? entry);

    Task<VideoFileEntry?> ResolveAsync(string id, CancellationToken cancellationToken = default);
}
