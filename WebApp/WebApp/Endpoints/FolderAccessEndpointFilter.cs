using WebApp.Authorization;

namespace WebApp.Endpoints;

/// <summary>One folder operation a request needs. A null <see cref="Location"/> means the target could not be mapped to a folder, which is denied.</summary>
internal readonly record struct AccessRequirement(FolderOperation Operation, FolderLocation? Location, bool ForbidWhenUnreadable = false);

/// <summary>Computes the folder requirements of a request. An empty list means the target did not resolve and the handler decides (404/400).</summary>
internal delegate ValueTask<IReadOnlyList<AccessRequirement>> AccessRule(EndpointFilterInvocationContext context);

/// <summary>
/// The one endpoint gate in front of archive, video, cut, and composition routes. After the opaque ID resolves server-side it maps
/// to a folder and asks the resource-based handler: unreadable is 404, readable but forbidden is 403. Admins skip rule evaluation.
/// </summary>
internal sealed class FolderAccessEndpointFilter(AccessRule rule) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var user = context.HttpContext.User;
        var access = services.GetRequiredService<FolderAccessService>();
        var actor = await access.GetActorAsync(FolderAuthorizer.UserIdOf(user), context.HttpContext.RequestAborted);
        if (actor is null) return Results.Unauthorized();
        if (actor.IsAdmin) return await next(context);

        var authorizer = services.GetRequiredService<FolderAuthorizer>();
        foreach (var requirement in await rule(context))
        {
            if (requirement.Location is null) return Results.NotFound();
            var decision = await authorizer.AuthorizeAsync(user, requirement.Operation, requirement.Location);
            if (decision == AccessDecision.Allowed) continue;
            return decision == AccessDecision.NotFound && !requirement.ForbidWhenUnreadable
                ? Results.NotFound()
                : Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}

internal static class FolderAccessEndpointExtensions
{
    public static TBuilder RequireFolderAccess<TBuilder>(this TBuilder builder, AccessRule rule)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new FolderAccessEndpointFilter(rule));
}
