using WebApp.Services;

namespace WebApp.Endpoints;

/// <summary>
/// Per-user media services report an item the caller cannot use (unknown, unreadable, or signed out) as <see cref="ArchiveNotFoundException"/>;
/// this turns that into the same 404 every other protected route returns, so nothing about the item is revealed.
/// </summary>
internal sealed class ArchiveNotFoundEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ArchiveNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
