using Microsoft.AspNetCore.Identity;
using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Identity;

namespace WebApp.Endpoints;

/// <summary>
/// The folder-access editor API, mapped onto P1's Admin-restricted group (so it inherits the Admin policy and the antiforgery filter)
/// and addressed by <c>UserName</c> like every other Admin route. Delegation rules still run per change in
/// <see cref="PermissionWriteService"/> so a non-Admin actor could never exceed their own rights.
/// </summary>
internal static class AdminAccessEndpoints
{
    public static RouteGroupBuilder MapAdminAccessEndpoints(this RouteGroupBuilder adminApi)
    {
        adminApi.MapGet("/{userName}/access", async (HttpContext context, UserManager<ApplicationUser> users, FolderAccessEditorService editor, string userName) =>
        {
            var (result, failure) = await editor.GetAsync(users.GetUserId(context.User)!, userName, context.RequestAborted);
            return result is not null ? Results.Ok(result) : ToResult(failure!);
        });
        adminApi.MapPut("/{userName}/access", async (HttpContext context, UserManager<ApplicationUser> users, PermissionWriteService writer, string userName, FolderAccessSaveRequest request) =>
            ToResult(await writer.ApplyAsync(users.GetUserId(context.User)!, userName, request.Changes ?? [], context.RequestAborted)));
        adminApi.MapPost("/{userName}/access/preview", async (HttpContext context, UserManager<ApplicationUser> users, FolderAccessEditorService editor, string userName, FolderAccessSaveRequest request) =>
        {
            var (result, failure) = await editor.PreviewAsync(users.GetUserId(context.User)!, userName, request.Changes ?? [], context.RequestAborted);
            return result is not null ? Results.Ok(result) : ToResult(failure!);
        });
        return adminApi;
    }

    private static IResult ToResult(PermissionWriteResult result) => result.Outcome switch
    {
        PermissionWriteOutcome.Ok => Results.NoContent(),
        PermissionWriteOutcome.NotFound => Results.NotFound(result.Errors),
        PermissionWriteOutcome.Conflict => Results.Conflict(result.Errors),
        PermissionWriteOutcome.Forbidden => Results.Json(result.Errors, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.BadRequest(result.Errors)
    };
}
