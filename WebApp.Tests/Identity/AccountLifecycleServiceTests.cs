using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Data;
using WebApp.Identity;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Identity;

public sealed class AccountLifecycleServiceTests
{
    private static async Task<T> WithServicesAsync<T>(AccountFactory factory, Func<AccountLifecycleService, UserManager<ApplicationUser>, AppDbContext, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;
        return await action(provider.GetRequiredService<AccountLifecycleService>(), provider.GetRequiredService<UserManager<ApplicationUser>>(), provider.GetRequiredService<AppDbContext>());
    }

    private static async Task<string> IdOfAsync(AccountFactory factory, string userName) => (await FindUserAsync(factory, userName))!.Id;

    [Fact]
    public async Task Default_administrator_is_seeded_once_with_a_forced_password_change()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);

        var admin = (await FindUserAsync(factory, "admin"))!;

        Assert.True(admin.MustChangePassword);
        Assert.True(admin.IsActive);
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().IsInRoleAsync(admin, "Admin"));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task Restarting_with_preserved_data_never_alters_the_existing_default_administrator()
    {
        using var root = new TemporaryDirectory();
        string passwordHash, stamp, authenticatorKey;
        using (var first = new AccountFactory(root.Path))
        {
            await WithServicesAsync(first, async (_, users, _) =>
            {
                var admin = (await users.FindByNameAsync("admin"))!;
                Assert.True((await users.ChangePasswordAsync(admin, "admin", "a-better-password")).Succeeded);
                admin.MustChangePassword = false;
                await users.UpdateAsync(admin);
                await users.ResetAuthenticatorKeyAsync(admin);
                await users.SetTwoFactorEnabledAsync(admin, true);
                await users.GenerateNewTwoFactorRecoveryCodesAsync(admin, 10);
                return 0;
            });
            var stored = (await FindUserAsync(first, "admin"))!;
            passwordHash = stored.PasswordHash!;
            stamp = stored.SecurityStamp!;
            using var scope = first.Services.CreateScope();
            authenticatorKey = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetAuthenticatorKeyAsync(stored))!;
        }

        using var restarted = new AccountFactory(root.Path);
        var admin = (await FindUserAsync(restarted, "admin"))!;

        Assert.Equal(passwordHash, admin.PasswordHash);
        Assert.Equal(stamp, admin.SecurityStamp);
        Assert.False(admin.MustChangePassword);
        Assert.True(admin.TwoFactorEnabled);
        using var scope2 = restarted.Services.CreateScope();
        var users = scope2.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(authenticatorKey, await users.GetAuthenticatorKeyAsync(admin));
        Assert.Equal(10, await users.CountRecoveryCodesAsync(admin));
        Assert.True(await users.IsInRoleAsync(admin, "Admin"));
        Assert.Equal(1, await scope2.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated_demoted_or_deleted()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "helper", "helper-pass", admin: true);
        var helperId = await IdOfAsync(factory, "helper");

        // helper is an active Admin, so admin can be changed freely...
        var demote = await WithServicesAsync(factory, (svc, _, _) => svc.SetAdministratorAsync(helperId, "admin", false));
        Assert.True(demote.Succeeded);

        // ...but now helper is the last active Admin and every removal path is refused.
        var adminId = await IdOfAsync(factory, "admin");
        var deactivate = await WithServicesAsync(factory, (svc, _, _) => svc.SetActiveAsync(adminId, "helper", false));
        var demoteLast = await WithServicesAsync(factory, (svc, _, _) => svc.SetAdministratorAsync(adminId, "helper", false));
        var delete = await WithServicesAsync(factory, (svc, _, _) => svc.DeleteAsync(adminId, "helper"));

        Assert.Equal(LifecycleFailure.LastAdministrator, deactivate.Failure);
        Assert.Equal(LifecycleFailure.LastAdministrator, demoteLast.Failure);
        Assert.Equal(LifecycleFailure.LastAdministrator, delete.Failure);
        var helper = (await FindUserAsync(factory, "helper"))!;
        Assert.True(helper.IsActive);
        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().IsInRoleAsync(helper, "Admin"));
    }

    [Fact]
    public async Task An_inactive_administrator_does_not_count_as_a_remaining_administrator()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "helper", "helper-pass", admin: true);
        var adminId = await IdOfAsync(factory, "admin");
        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.SetActiveAsync(adminId, "helper", false))).Succeeded);

        var result = await WithServicesAsync(factory, (svc, _, _) => svc.SetActiveAsync(adminId, "admin", false));

        Assert.Equal(LifecycleFailure.LastAdministrator, result.Failure);
    }

    [Fact]
    public async Task Password_reset_issues_a_one_time_temporary_password_and_invalidates_the_session()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var before = (await FindUserAsync(factory, "member"))!;
        var adminId = await IdOfAsync(factory, "admin");

        var result = await WithServicesAsync(factory, (svc, _, _) => svc.ResetPasswordAsync(adminId, "member"));

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.TemporaryPassword));
        var after = (await FindUserAsync(factory, "member"))!;
        Assert.True(after.MustChangePassword);
        Assert.NotNull(after.TemporaryPasswordExpiresUtc);
        Assert.Equal(before.AuthzVersion + 1, after.AuthzVersion);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False(await users.CheckPasswordAsync(after, "member-pass"));
        Assert.True(await users.CheckPasswordAsync(after, result.TemporaryPassword!));
    }

    [Fact]
    public async Task Password_reset_restores_access_for_an_expired_temporary_password()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "late", "temp-pass", mustChange: true, expiresUtc: DateTimeOffset.UtcNow.AddMinutes(-5));
        var adminId = await IdOfAsync(factory, "admin");
        using var client = factory.CreateClient(NoRedirect);
        using var expired = await PasswordLoginAsync(client, "late", "temp-pass");
        Assert.Contains("reason=expired", expired.Headers.Location?.ToString());

        var reset = await WithServicesAsync(factory, (svc, _, _) => svc.ResetPasswordAsync(adminId, "late"));
        using var retry = await PasswordLoginAsync(client, "late", reset.TemporaryPassword!);

        Assert.Contains("step=change", retry.Headers.Location?.ToString());
        Assert.True((await FindUserAsync(factory, "late"))!.TemporaryPasswordExpiresUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Totp_reset_disables_the_authenticator_and_discards_recovery_codes()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        await EnrollAuthenticatorAsync(factory, "member");
        var adminId = await IdOfAsync(factory, "admin");

        var result = await WithServicesAsync(factory, (svc, _, _) => svc.ResetTotpAsync(adminId, "member"));

        Assert.True(result.Succeeded);
        var member = (await FindUserAsync(factory, "member"))!;
        Assert.False(member.TwoFactorEnabled);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CountRecoveryCodesAsync(member));
    }

    [Fact]
    public async Task Deactivation_and_reactivation_round_trip_and_rotate_the_security_stamp()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var adminId = await IdOfAsync(factory, "admin");
        var original = (await FindUserAsync(factory, "member"))!;

        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.SetActiveAsync(adminId, "member", false))).Succeeded);
        var inactive = (await FindUserAsync(factory, "member"))!;
        Assert.False(inactive.IsActive);
        Assert.NotEqual(original.SecurityStamp, inactive.SecurityStamp);
        Assert.Equal(original.AuthzVersion + 1, inactive.AuthzVersion);

        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.SetActiveAsync(adminId, "member", true))).Succeeded);
        Assert.True((await FindUserAsync(factory, "member"))!.IsActive);
    }

    [Fact]
    public async Task Role_changes_promote_and_demote_ordinary_accounts()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var adminId = await IdOfAsync(factory, "admin");

        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.SetAdministratorAsync(adminId, "member", true))).Succeeded);
        Assert.True(await WithServicesAsync(factory, (_, users, _) => users.IsInRoleAsync(users.FindByNameAsync("member").Result!, "Admin")));
        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.SetAdministratorAsync(adminId, "member", false))).Succeeded);
        Assert.False(await WithServicesAsync(factory, (_, users, _) => users.IsInRoleAsync(users.FindByNameAsync("member").Result!, "Admin")));
    }

    [Fact]
    public async Task Delete_reassigns_accounts_created_by_the_deleted_user_to_the_actor()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "creator", "creator-pass", admin: true);
        var adminId = await IdOfAsync(factory, "admin");
        var creatorId = await IdOfAsync(factory, "creator");
        var created = await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(creatorId, "child", "Child", "temp-pass", false));
        Assert.True(created.Succeeded);
        Assert.Equal(creatorId, (await FindUserAsync(factory, "child"))!.CreatedByUserId);

        var deleted = await WithServicesAsync(factory, (svc, _, _) => svc.DeleteAsync(adminId, "creator"));

        Assert.True(deleted.Succeeded);
        Assert.Null(await FindUserAsync(factory, "creator"));
        Assert.Equal(adminId, (await FindUserAsync(factory, "child"))!.CreatedByUserId);
    }

    [Fact]
    public async Task Delete_refuses_self_and_unknown_accounts()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        var adminId = await IdOfAsync(factory, "admin");

        Assert.Equal(LifecycleFailure.Invalid, (await WithServicesAsync(factory, (svc, _, _) => svc.DeleteAsync(adminId, "admin"))).Failure);
        Assert.Equal(LifecycleFailure.NotFound, (await WithServicesAsync(factory, (svc, _, _) => svc.DeleteAsync(adminId, "nobody"))).Failure);
        Assert.Equal(LifecycleFailure.NotFound, (await WithServicesAsync(factory, (svc, _, _) => svc.ResetPasswordAsync(adminId, "nobody"))).Failure);
    }

    [Fact]
    public async Task Create_validates_input_and_forces_a_password_change_with_expiry()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        var adminId = await IdOfAsync(factory, "admin");

        Assert.Equal(LifecycleFailure.Invalid, (await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(adminId, " ", "Blank", "temp-pass", false))).Failure);
        Assert.Equal(LifecycleFailure.Conflict, (await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(adminId, "admin", "Again", "temp-pass", false))).Failure);
        Assert.Equal(LifecycleFailure.Invalid, (await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(adminId, "weak", "Weak", "abc", false))).Failure);
        Assert.Null(await FindUserAsync(factory, "weak"));

        Assert.True((await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(adminId, " fresh ", "Fresh", "temp-pass", false))).Succeeded);
        var fresh = (await FindUserAsync(factory, "fresh"))!;
        Assert.True(fresh.MustChangePassword);
        Assert.InRange(fresh.TemporaryPasswordExpiresUtc!.Value, DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(8));
        Assert.Equal(adminId, fresh.CreatedByUserId);
    }

    [Fact]
    public async Task Audit_events_record_actions_without_any_secret()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var adminId = await IdOfAsync(factory, "admin");
        var reset = await WithServicesAsync(factory, (svc, _, _) => svc.ResetPasswordAsync(adminId, "member"));
        await WithServicesAsync(factory, (svc, _, _) => svc.CreateAsync(adminId, "secret-user", "Secret", "very-secret-temp", false));

        var events = await WithServicesAsync(factory, (_, _, db) => db.AuditEvents.ToListAsync());

        Assert.Contains(events, audit => audit.Action == "user.seeded");
        Assert.Contains(events, audit => audit.Action == "user.password-reset" && audit.TargetUserName == "member" && audit.ActorUserId == adminId);
        Assert.Contains(events, audit => audit.Action == "user.created" && audit.TargetUserName == "secret-user");
        var everything = string.Join('\n', events.Select(audit => $"{audit.Action}|{audit.Detail}|{audit.TargetUserName}"));
        Assert.DoesNotContain(reset.TemporaryPassword!, everything);
        Assert.DoesNotContain("very-secret-temp", everything);
    }
}
