using WebApp.Models;

namespace WebApp.Services;

internal sealed class ImageCropService(
    IArchiveService archive,
    ImageCropNamingService namingService,
    IImageCropGenerator generator) : IImageCropService
{
    internal const string CutsFolderName = "cuts";
    private static readonly HashSet<string> CropSupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public async Task<ImageCropOutcome> CropAsync(
        string categoryKey, string itemId, int x, int y, int width, int height, CancellationToken cancellationToken)
    {
        if (!archive.TryResolveImage(categoryKey, itemId, out var item) || item is null || item.Extension is null)
        {
            return ImageCropOutcome.NotFound();
        }

        if (!CropSupportedExtensions.Contains(item.Extension))
        {
            return ImageCropOutcome.NotFound();
        }

        if (!File.Exists(item.PhysicalPath))
        {
            return ImageCropOutcome.NotFound();
        }

        if (width <= 0 || height <= 0)
        {
            return ImageCropOutcome.OutOfBounds("crop region must have a positive width and height");
        }

        var categoryRoot = archive.GetCategoryRootPath(categoryKey);
        var cutsDirectory = Path.Combine(categoryRoot, CutsFolderName);
        Directory.CreateDirectory(cutsDirectory);

        var destinationPath = namingService.GetNextPath(cutsDirectory, item.Name, item.Extension);
        var result = await generator.CropAsync(item.PhysicalPath, destinationPath, x, y, width, height, cancellationToken);

        return result.Status switch
        {
            ImageCropGenerationStatus.Success =>
                ImageCropOutcome.Success(archive.ComputeItemId(categoryKey, destinationPath), Path.GetFileName(destinationPath)),
            ImageCropGenerationStatus.OutOfBounds => ImageCropOutcome.OutOfBounds(result.Diagnostic),
            _ => ImageCropOutcome.WriteFailed(result.Diagnostic)
        };
    }
}
