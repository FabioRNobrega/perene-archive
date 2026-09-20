using WebApp.Models;

namespace WebApp.Services;

internal interface IComicBookService
{
    bool TryGetMetadata(ArchiveItemEntry item, out ComicBookMetadata? metadata);
    bool TryGetPage(ArchiveItemEntry item, int index, out ComicBookPage? page);
}

internal sealed record ComicBookMetadata(int PageCount);
internal sealed record ComicBookPage(byte[] Bytes, string ContentType);
