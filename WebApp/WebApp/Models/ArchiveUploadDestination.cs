namespace WebApp.Models;

/// <summary>
/// Server-only, fully validated final destination for an upload session, resolved by
/// <c>IArchiveService</c> at both session creation and completion time. Never leaves the server.
/// </summary>
internal sealed record ArchiveUploadDestination(
    ArchiveCategory Category,
    ArchiveItemEntry Parent,
    string SafeName,
    string FinalPath);
