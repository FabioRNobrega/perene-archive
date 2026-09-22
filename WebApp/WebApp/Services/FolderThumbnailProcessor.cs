using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class FolderThumbnailProcessor : IFolderThumbnailProcessor
{
    internal const int OutputWidth = 640;
    internal const int OutputHeight = 360;

    public async Task<FolderThumbnailProcessResult> ProcessAsync(Stream source, CancellationToken cancellationToken)
    {
        try
        {
            using var image = await Image.LoadAsync(source, cancellationToken);
            image.Mutate(context => context.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Size = new Size(OutputWidth, OutputHeight),
                Position = AnchorPositionMode.Center,
            }));

            using var output = new MemoryStream();
            await image.SaveAsync(output, new JpegEncoder(), cancellationToken);
            return FolderThumbnailProcessResult.Success(output.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return FolderThumbnailProcessResult.InvalidImage();
        }
    }
}
