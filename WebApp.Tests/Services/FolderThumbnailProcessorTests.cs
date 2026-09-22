using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class FolderThumbnailProcessorTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(200, 400)]
    [InlineData(10, 10)]
    public async Task Valid_image_is_resized_and_cropped_to_the_exact_output_size(int width, int height)
    {
        var processor = new FolderThumbnailProcessor();
        await using var source = await CreateFixturePngAsync(width, height);

        var result = await processor.ProcessAsync(source, CancellationToken.None);

        Assert.Equal(FolderThumbnailProcessStatus.Success, result.Status);
        Assert.NotNull(result.JpegBytes);
        using var output = Image.Load(result.JpegBytes!);
        Assert.Equal(FolderThumbnailProcessor.OutputWidth, output.Width);
        Assert.Equal(FolderThumbnailProcessor.OutputHeight, output.Height);
    }

    [Fact]
    public async Task Output_is_always_jpeg_regardless_of_source_format()
    {
        var processor = new FolderThumbnailProcessor();
        await using var source = await CreateFixturePngAsync(640, 360);

        var result = await processor.ProcessAsync(source, CancellationToken.None);

        Assert.Equal(FolderThumbnailProcessStatus.Success, result.Status);
        using var output = Image.Load(result.JpegBytes!);
        Assert.IsType<SixLabors.ImageSharp.Formats.Jpeg.JpegFormat>(output.Metadata.DecodedImageFormat);
    }

    [Fact]
    public async Task Non_image_content_is_rejected_as_invalid()
    {
        var processor = new FolderThumbnailProcessor();
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("this is not an image, just plain text content"));

        var result = await processor.ProcessAsync(source, CancellationToken.None);

        Assert.Equal(FolderThumbnailProcessStatus.InvalidImage, result.Status);
        Assert.Null(result.JpegBytes);
    }

    private static async Task<MemoryStream> CreateFixturePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }
}
