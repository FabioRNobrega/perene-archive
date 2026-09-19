using WebApp.Models;

namespace WebApp.Services;

internal interface IArchiveDownloadService
{
    Task WriteFolderZipAsync(ArchiveItemEntry folder, Stream destination, CancellationToken cancellationToken);
}
