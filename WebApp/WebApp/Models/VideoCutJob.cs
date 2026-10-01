namespace WebApp.Models;

internal sealed record CutJob(string JobId, VideoFileEntry SourceEntry, TimeSpan Start, TimeSpan End, string? ActorUserId = null);
