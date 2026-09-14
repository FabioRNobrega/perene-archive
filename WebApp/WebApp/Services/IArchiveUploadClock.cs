namespace WebApp.Services;

/// <summary>Testable indirection over the wall clock used by upload session TTL/activity tracking.</summary>
internal interface IArchiveUploadClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemArchiveUploadClock : IArchiveUploadClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
