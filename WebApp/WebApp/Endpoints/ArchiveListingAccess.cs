using System.Security.Claims;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Models;

namespace WebApp.Endpoints;

/// <summary>Filters archive output by what the caller may read: hidden folders vanish, and only legal name-only pass-through stubs cross an unreadable ancestor.</summary>
internal static class ArchiveListingAccess
{
    /// <returns>The listing the caller may see, or null when the listed folder is neither readable nor a legal pass-through (404).</returns>
    public static async Task<ArchiveListing?> FilterAsync(ArchiveListing listing, HttpContext http)
    {
        var readable = await http.RequestServices.GetRequiredService<FolderAuthorizer>().GetReadableAsync(http.User, http.RequestAborted);
        if (readable is null) return null;
        if (readable.IsAdmin) return listing;

        var locator = http.RequestServices.GetRequiredService<FolderLocator>();
        if (locator.LocateDirectory(listing.CurrentFolder.PhysicalPath) is not { } current) return null;
        var currentReadable = readable.IsReadable(current);
        if (!currentReadable && !readable.IsPassThrough(current)) return null;

        var items = listing.Items.Where(item =>
        {
            // A pass-through stub shows only the folders that lead to something readable, never its own files.
            if (item.Kind == ArchiveItemKind.File) return currentReadable;
            return locator.LocateDirectory(item.PhysicalPath) is { } child && (readable.IsReadable(child) || readable.IsPassThrough(child));
        }).ToList();
        return listing with { Items = items };
    }

    /// <summary>A predicate over physical directories for recursive walks (playlists, ZIP downloads); null for Admins, who see everything.</summary>
    public static async Task<Func<string, bool>?> CanEnterAsync(HttpContext http)
    {
        var readable = await http.RequestServices.GetRequiredService<FolderAuthorizer>().GetReadableAsync(http.User, http.RequestAborted);
        if (readable is null) return _ => false;
        if (readable.IsAdmin) return null;
        var locator = http.RequestServices.GetRequiredService<FolderLocator>();
        return path => locator.LocateDirectory(path) is { } location && readable.IsReadable(location);
    }

    public static string? UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    public static async Task<bool> IsAdminAsync(HttpContext http) =>
        (await http.RequestServices.GetRequiredService<FolderAccessService>().GetActorAsync(UserId(http), http.RequestAborted))?.IsAdmin == true;
}
