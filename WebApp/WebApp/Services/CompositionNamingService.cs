using Microsoft.Extensions.Options;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed class CompositionNamingService(IOptions<VideoCompositionOptions> options, NamingCounterService counters)
{
    private readonly string _compositionRoot = Path.GetFullPath(options.Value.Path);

    public string GetNextPath(string firstSourceFileName)
    {
        var prefix = CutNamingService.GetPrefix(firstSourceFileName);
        var stem = $"{prefix} Composition";
        return counters.AllocatePath("composition", _compositionRoot, prefix, ".mp4",
            () => NamingCounterService.HighestOnDisk(_compositionRoot, stem, ".mp4"),
            next => Path.Combine(_compositionRoot, $"{stem} {next:0000}.mp4"));
    }
}
