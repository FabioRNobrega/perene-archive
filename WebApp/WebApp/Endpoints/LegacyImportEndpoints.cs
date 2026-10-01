using Microsoft.AspNetCore.Identity;
using WebApp.Client.Models;
using WebApp.Identity;
using WebApp.Security;
using WebApp.Services;

namespace WebApp.Endpoints;

/// <summary>
/// Admin-only operations for the per-user media data: the one-time legacy-file import and a media-identity reconciliation scan.
/// Both sit behind the Admin policy and the shared antiforgery filter; responses are counts only.
/// </summary>
internal static class LegacyImportEndpoints
{
    public static IEndpointRouteBuilder MapLegacyImportEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy.RequireRole(AccountLifecycleService.AdminRole))
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        admin.MapGet("/legacy-import", async (LegacyDataImporter importer, CancellationToken cancellationToken) =>
            await importer.LastRunAsync(cancellationToken) is { } report ? Results.Ok(report) : Results.NoContent());

        admin.MapPost("/legacy-import", async (HttpContext context, UserManager<ApplicationUser> users, LegacyDataImporter importer, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await importer.RunAsync(users.GetUserId(context.User)!, cancellationToken));
            }
            catch (LegacyImportBlockedException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        });

        admin.MapPost("/media/reconcile", async (MediaReconciliationService reconciliation, CancellationToken cancellationToken) =>
        {
            var report = await reconciliation.ReconcileAsync(cancellationToken);
            return Results.Ok(new MediaReconcileReportDto(report.Relinked, report.Reactivated, report.SentToReview, report.MarkedMissing));
        });

        return app;
    }
}
