namespace WebApp.Models;

internal enum FolderThumbnailProcessStatus
{
    Success,
    InvalidImage
}

internal sealed record FolderThumbnailProcessResult(FolderThumbnailProcessStatus Status, byte[]? JpegBytes = null)
{
    public static FolderThumbnailProcessResult Success(byte[] jpegBytes) =>
        new(FolderThumbnailProcessStatus.Success, jpegBytes);

    public static FolderThumbnailProcessResult InvalidImage() => new(FolderThumbnailProcessStatus.InvalidImage);
}
