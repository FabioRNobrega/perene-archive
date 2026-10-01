using System.Net;

namespace WebApp.Client.Models;

/// <summary>User-facing text for a rejected archive change, chosen from the HTTP status alone. Pure so it is unit-testable.</summary>
public static class ArchiveMutationMessages
{
    public const string Forbidden = "You don't have permission to make this change here. Ask an administrator for access to this folder.";
    public const string SessionExpired = "Your session has expired. Sign in again to continue.";

    // The server answers 404 for items the caller may not read, so the text must not reveal whether the item exists.
    public const string NotAvailable = "That item is no longer available, or you don't have access to it.";
    public const string Conflict = "That change conflicts with an existing item.";
    public const string Fallback = "The archive change could not be saved.";

    public static string For(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => Forbidden,
        HttpStatusCode.Unauthorized => SessionExpired,
        HttpStatusCode.NotFound => NotAvailable,
        HttpStatusCode.Conflict => Conflict,
        _ => Fallback
    };
}
