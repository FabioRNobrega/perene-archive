using System.Text.RegularExpressions;

namespace WebApp.Services;

internal sealed partial class ImageCropNamingService(NamingCounterService counters)
{
    public string GetNextPath(string outputDirectory, string sourceFileName, string extension)
    {
        var prefix = GetPrefix(sourceFileName);
        return counters.AllocatePath("image-crop", outputDirectory, prefix, extension,
            () => NamingCounterService.HighestOnDisk(outputDirectory, prefix, extension),
            next => Path.Combine(outputDirectory, $"{prefix} {next:0000}{extension}"));
    }

    internal static string GetPrefix(string sourceFileName)
    {
        var stem = Path.GetFileNameWithoutExtension(sourceFileName);
        var words = WhitespaceRegex().Split(stem.Trim())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Take(2)
            .ToArray();

        return words.Length == 0 ? "Cut" : string.Join(' ', words);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
