using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace WebApp.Authorization;

/// <summary>The single resource-based handler for every folder operation (<see cref="FolderOperations"/>).</summary>
public sealed class FolderPermissionAuthorizationHandler(IFolderAccessService access)
    : AuthorizationHandler<OperationAuthorizationRequirement, FolderOperationContext>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OperationAuthorizationRequirement requirement, FolderOperationContext resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null || !FolderOperations.TryParse(requirement.Name, out var operation)) return;

        // Read decisions may use the cached readable set; every other operation is re-evaluated fresh.
        var allowed = operation == FolderOperation.Read
            ? await access.CanReadAsync(userId, resource.Location)
            : await access.CheckAsync(userId, operation, resource.Location);
        if (allowed) context.Succeed(requirement);
    }
}

public enum AccessDecision { Allowed, NotFound, Forbidden }

/// <summary>
/// Maps handler outcomes to the endpoint matrix: unreadable is <see cref="AccessDecision.NotFound"/> (404, never revealing the
/// folder), readable but forbidden is <see cref="AccessDecision.Forbidden"/> (403). Anonymous callers are rejected earlier (401).
/// </summary>
public sealed class FolderAuthorizer(IAuthorizationService authorization, IFolderAccessService access)
{
    public async Task<AccessDecision> AuthorizeAsync(ClaimsPrincipal user, FolderOperation operation, FolderLocation location)
    {
        var resource = new FolderOperationContext(location);
        if (!(await authorization.AuthorizeAsync(user, resource, FolderOperations.Read)).Succeeded) return AccessDecision.NotFound;
        if (operation == FolderOperation.Read) return AccessDecision.Allowed;
        return (await authorization.AuthorizeAsync(user, resource, FolderOperations.Requirement(operation))).Succeeded
            ? AccessDecision.Allowed
            : AccessDecision.Forbidden;
    }

    /// <summary>The cached readable set of the caller, or null for an unknown/deactivated account.</summary>
    public Task<ReadableFolders?> GetReadableAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
        access.GetReadableAsync(user.FindFirstValue(ClaimTypes.NameIdentifier), cancellationToken);

    public static string? UserIdOf(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier);
}
