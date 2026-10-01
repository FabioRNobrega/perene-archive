using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Identity;

namespace WebApp.Security;

/// <summary>
/// Validates the antiforgery request token (header <c>X-CSRF-TOKEN</c>) on every unsafe HTTP method of the
/// route group it is applied to. Safe methods pass through untouched. A rejection is a 400 carrying
/// <see cref="FailureHeader"/> so the WebAssembly client can refresh its token and retry a buffered request once.
/// </summary>
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string FailureHeader = "X-Antiforgery-Failure";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
        {
            return await next(context);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            await AuditRejectionAsync(context.HttpContext);
            context.HttpContext.Response.Headers[FailureHeader] = "true";
            return Results.BadRequest(new[] { "The request could not be verified. Refresh and try again." });
        }

        return await next(context);
    }

    /// <summary>Records the rejection without secrets or paths: the actor and the route template only. Auditing never fails the request.</summary>
    private static async Task AuditRejectionAsync(HttpContext http)
    {
        try
        {
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var template = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unknown";
            db.AuditEvents.Add(new AuditEvent
            {
                OccurredUtc = DateTimeOffset.UtcNow,
                Action = "csrf.rejected",
                ActorUserId = http.User.FindFirstValue(ClaimTypes.NameIdentifier),
                Detail = $"{http.Request.Method} {template}"
            });
            await db.SaveChangesAsync();
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
        }
    }
}
