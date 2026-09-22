using WebApp.Models;

namespace WebApp.Services;

internal interface IFolderThumbnailProcessor
{
    Task<FolderThumbnailProcessResult> ProcessAsync(Stream source, CancellationToken cancellationToken);
}
