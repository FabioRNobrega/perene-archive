using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Identity;

namespace WebApp.Data;

/// <summary>Web-host-only startup work: validates the database location, migrates, and seeds the default Administrator once.</summary>
public static class StartupInitializer
{
    public const string DefaultAdminUserName = "admin";

    public static async Task InitializeAsync(IServiceProvider services)
    {
        // Resolving the options runs their validators now, before anything touches the database file.
        _ = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        // The queues are empty after a restart, so persisted Pending/Processing jobs can never run; fail them rather than replay.
        await Services.SqliteJobStore.MarkInterruptedAsync(db);

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync(AccountLifecycleService.AdminRole))
            await roles.CreateAsync(new IdentityRole(AccountLifecycleService.AdminRole));

        // Roots and the policy singleton are seeded once; existing members simply fall to the Shared defaults.
        await scope.ServiceProvider.GetRequiredService<Authorization.FolderCatalog>().SeedAsync();

        // Seed only when absent: an existing account's password, TOTP, recovery codes, roles and flags are never touched.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByNameAsync(DefaultAdminUserName) is not null) return;

        await using var transaction = await db.Database.BeginTransactionAsync();
        var admin = new ApplicationUser
        {
            UserName = DefaultAdminUserName,
            DisplayName = "Administrator",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
            MustChangePassword = true
        };
        var created = await users.CreateAsync(admin, "admin");
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Unable to create the default administrator account.");
        }

        var inRole = await users.AddToRoleAsync(admin, AccountLifecycleService.AdminRole);
        if (!inRole.Succeeded)
        {
            await transaction.RollbackAsync();
            throw new InvalidOperationException("Unable to assign the default administrator role.");
        }

        db.AuditEvents.Add(new AuditEvent { OccurredUtc = DateTimeOffset.UtcNow, Action = "user.seeded", TargetUserId = admin.Id, TargetUserName = admin.UserName, Detail = "default administrator" });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
