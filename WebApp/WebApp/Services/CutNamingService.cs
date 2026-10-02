using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed partial class CutNamingService(IOptions<VideoCutOptions> options, NamingCounterService counters)
{
    private readonly string _cutRoot = Path.GetFullPath(options.Value.Path);

    public string GetNextPath(string sourceFileName)
    {
        var prefix = GetPrefix(sourceFileName);
        return counters.AllocatePath("cut", _cutRoot, prefix, ".mp4",
            () => NamingCounterService.HighestOnDisk(_cutRoot, prefix, ".mp4"),
            next => Path.Combine(_cutRoot, $"{prefix} {next:0000}.mp4"));
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
