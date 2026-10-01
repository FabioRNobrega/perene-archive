using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveDownloadService
{
    /// <param name="canEnterFolder">When set, a sub-directory is left out of the ZIP unless it returns true for its physical path.</param>
    Task WriteFolderZipAsync(ArchiveItemEntry folder, Stream destination, CancellationToken cancellationToken, Func<string, bool>? canEnterFolder = null);
}
