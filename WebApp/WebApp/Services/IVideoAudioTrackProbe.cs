using WebApp.Models;

namespace WebApp.Services;

internal interface IVideoAudioTrackProbe
{
    Task<IReadOnlyList<AudioTrackProbeResult>> GetAudioTracksAsync(string physicalPath, CancellationToken cancellationToken);
}
