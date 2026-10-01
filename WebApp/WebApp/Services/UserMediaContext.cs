using System.Security.Claims;
using WebApp.Authorization;
using WebApp.Data.Entities;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>The authenticated caller, read from the server principal only; never from a request field.</summary>
internal interface ICurrentUser
{
    /// <summary>Throws <see cref="ArchiveNotFoundException"/> (fail closed) when no one is signed in.</summary>
    string RequireUserId();
}

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string RequireUserId() =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id
            ? id
            : throw new ArchiveNotFoundException("The archive item does not exist.");
}

/// <summary>A resolved, readable media item together with the caller who may use it.</summary>
internal sealed record UserMedia(string UserId, MediaItem Item, ArchiveItemEntry Entry);

/// <summary>
/// Resolves an opaque archive item ID to the caller's per-user media context. Both conditions of the access rule live here: the caller
/// must be signed in and must have Read on the item's folder (an unreadable item is reported as not found, like everywhere else).
/// </summary>
internal interface IUserMediaContext
{
    string UserId { get; }
    Task<UserMedia> RequireAsync(string categoryKey, string itemId, CancellationToken cancellationToken);
}

internal sealed class UserMediaContext(
    ICurrentUser currentUser,
    IArchiveService archive,
    FolderLocator locator,
    IFolderAccessService access,
    MediaReconciliationService reconciliation) : IUserMediaContext
{
    public string UserId => currentUser.RequireUserId();

    public async Task<UserMedia> RequireAsync(string categoryKey, string itemId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        if (!archive.TryResolveItem(categoryKey, itemId, out var entry) || entry is null)
            throw new ArchiveNotFoundException("The archive item does not exist.");

        var location = locator.LocateContainer(entry.PhysicalPath);
        if (location is null || !await access.CanReadAsync(userId, location, cancellationToken))
            throw new ArchiveNotFoundException("The archive item does not exist.");

        var file = reconciliation.Describe(entry) ?? throw new ArchiveNotFoundException("The archive item does not exist.");
        var item = await reconciliation.EnsureAsync(file, cancellationToken) ?? throw new ArchiveNotFoundException("The archive item does not exist.");
        return new UserMedia(userId, item, entry);
    }
}
