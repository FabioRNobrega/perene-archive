using WebApp.Models;

namespace WebApp.Services;

internal sealed record AudioTrackRemuxResult(bool Succeeded, string? Diagnostic = null)
{
    public static AudioTrackRemuxResult Success() => new(true);

    public static AudioTrackRemuxResult Failed(string diagnostic) => new(false, diagnostic);
}

internal interface IAudioTrackRemuxer
{
    Task<AudioTrackRemuxResult> RemuxAsync(
        VideoFileEntry source,
        int trackIndex,
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken);
}
