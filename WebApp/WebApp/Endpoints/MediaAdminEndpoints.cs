using WebApp.Client.Models;
using WebApp.Identity;
using WebApp.Security;
using WebApp.Services;

namespace WebApp.Endpoints;

/// <summary>Admin-only media identity maintenance: a reconciliation scan that relinks moved files and marks vanished ones. Counts only.</summary>
internal static class MediaAdminEndpoints
{
    public static IEndpointRouteBuilder MapMediaAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .RequireAuthorization(policy => policy.RequireRole(AccountLifecycleService.AdminRole))
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

        admin.MapPost("/media/reconcile", async (MediaReconciliationService reconciliation, CancellationToken cancellationToken) =>
        {
            var report = await reconciliation.ReconcileAsync(cancellationToken);
            return Results.Ok(new MediaReconcileReportDto(report.Relinked, report.Reactivated, report.SentToReview, report.MarkedMissing));
        });

        return app;
    }
}
