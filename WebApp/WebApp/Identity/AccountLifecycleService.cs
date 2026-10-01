using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Data;

namespace WebApp.Identity;

public enum LifecycleFailure { None, Invalid, NotFound, Conflict, LastAdministrator }

public sealed record LifecycleResult(LifecycleFailure Failure, IReadOnlyList<string> Errors, string? TemporaryPassword = null)
{
    public bool Succeeded => Failure == LifecycleFailure.None;
    public static LifecycleResult Ok(string? temporaryPassword = null) => new(LifecycleFailure.None, [], temporaryPassword);
    public static LifecycleResult Fail(LifecycleFailure failure, params string[] errors) => new(failure, errors);
}

/// <summary>
/// Owns every Admin account lifecycle rule: creation, password/TOTP reset, deactivation, role changes, and deletion.
/// Each operation runs in one transaction, bumps <see cref="ApplicationUser.AuthzVersion"/> and the security stamp
/// together, and records an audit event that never contains a secret.
/// </summary>
public sealed class AccountLifecycleService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IOptions<AuthOptions> authOptions)
{
    public const string AdminRole = "Admin";

    public async Task<LifecycleResult> CreateAsync(string actorId, string userName, string displayName, string temporaryPassword, bool isAdmin)
    {
        userName = userName.Trim();
        displayName = displayName.Trim();
        if (userName.Length == 0 || displayName.Length == 0 || string.IsNullOrEmpty(temporaryPassword))
            return LifecycleResult.Fail(LifecycleFailure.Invalid, "Username, display name and a temporary password are required.");
        if (await users.FindByNameAsync(userName) is not null)
            return LifecycleResult.Fail(LifecycleFailure.Conflict, "That username is already taken.");

        return await InTransactionAsync(async () =>
        {
            var user = new ApplicationUser
            {
                UserName = userName,
                DisplayName = displayName,
                CreatedUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = actorId,
                IsActive = true,
                MustChangePassword = true,
                TemporaryPasswordExpiresUtc = NewExpiry()
            };
            var created = await users.CreateAsync(user, temporaryPassword);
            if (!created.Succeeded) return Rejected(created);
            if (isAdmin)
            {
                var role = await users.AddToRoleAsync(user, AdminRole);
                if (!role.Succeeded) return Rejected(role);
            }

            Audit("user.created", actorId, user, isAdmin ? "role=Admin" : "role=Member");
            await db.SaveChangesAsync();
            return LifecycleResult.Ok();
        });
    }

    public async Task<LifecycleResult> ResetPasswordAsync(string actorId, string userName)
    {
        var temporaryPassword = TemporaryPasswords.Generate();
        var result = await MutateAsync(actorId, userName, "user.password-reset", async user =>
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, temporaryPassword);
            if (!reset.Succeeded) return Rejected(reset);
            user.MustChangePassword = true;
            user.TemporaryPasswordExpiresUtc = NewExpiry();
            return null;
        });
        return result.Succeeded ? LifecycleResult.Ok(temporaryPassword) : result;
    }

    public Task<LifecycleResult> ResetTotpAsync(string actorId, string userName) =>
        MutateAsync(actorId, userName, "user.totp-reset", async user =>
        {
            var disabled = await users.SetTwoFactorEnabledAsync(user, false);
            if (!disabled.Succeeded) return Rejected(disabled);
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) return Rejected(reset);
            // Replacing the codes with an empty set invalidates any recovery codes the user still held.
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
            return null;
        });

    public Task<LifecycleResult> SetActiveAsync(string actorId, string userName, bool active) =>
        MutateAsync(actorId, userName, active ? "user.reactivated" : "user.deactivated", async user =>
        {
            if (!active && await IsLastActiveAdministratorAsync(user))
                return LifecycleResult.Fail(LifecycleFailure.LastAdministrator, "The last active administrator cannot be deactivated.");
            user.IsActive = active;
            return null;
        });

    public Task<LifecycleResult> SetAdministratorAsync(string actorId, string userName, bool isAdmin) =>
        MutateAsync(actorId, userName, isAdmin ? "user.promoted" : "user.demoted", async user =>
        {
            var inRole = await users.IsInRoleAsync(user, AdminRole);
            if (inRole == isAdmin) return null;
            if (!isAdmin && await IsLastActiveAdministratorAsync(user))
                return LifecycleResult.Fail(LifecycleFailure.LastAdministrator, "The last active administrator cannot be demoted.");
            var change = isAdmin ? await users.AddToRoleAsync(user, AdminRole) : await users.RemoveFromRoleAsync(user, AdminRole);
            return change.Succeeded ? null : Rejected(change);
        });

    /// <summary>Deletes an account after reassigning every account it created to <paramref name="actorId"/>.</summary>
    public async Task<LifecycleResult> DeleteAsync(string actorId, string userName)
    {
        var target = await users.FindByNameAsync(userName);
        if (target is null) return LifecycleResult.Fail(LifecycleFailure.NotFound, "That account no longer exists.");
        if (target.Id == actorId) return LifecycleResult.Fail(LifecycleFailure.Invalid, "You cannot delete your own account.");

        return await InTransactionAsync(async () =>
        {
            if (await IsLastActiveAdministratorAsync(target))
                return LifecycleResult.Fail(LifecycleFailure.LastAdministrator, "The last active administrator cannot be deleted.");
            await db.Users.Where(user => user.CreatedByUserId == target.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(user => user.CreatedByUserId, actorId));
            var deleted = await users.DeleteAsync(target);
            if (!deleted.Succeeded) return Rejected(deleted);
            Audit("user.deleted", actorId, target, $"reassigned-to={actorId}");
            await db.SaveChangesAsync();
            return LifecycleResult.Ok();
        });
    }

    /// <summary>True when <paramref name="user"/> is an active Admin and no other active Admin exists.</summary>
    public async Task<bool> IsLastActiveAdministratorAsync(ApplicationUser user)
    {
        if (!user.IsActive || !await users.IsInRoleAsync(user, AdminRole)) return false;
        var otherActiveAdmins = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            join account in db.Users on userRole.UserId equals account.Id
            where role.Name == AdminRole && account.IsActive && account.Id != user.Id
            select account.Id).CountAsync();
        return otherActiveAdmins == 0;
    }

    private async Task<LifecycleResult> MutateAsync(string actorId, string userName, string action, Func<ApplicationUser, Task<LifecycleResult?>> change)
    {
        var target = await users.FindByNameAsync(userName);
        if (target is null) return LifecycleResult.Fail(LifecycleFailure.NotFound, "That account no longer exists.");

        return await InTransactionAsync(async () =>
        {
            // Re-read inside the transaction so the guard and the write see the same snapshot.
            target = (await users.FindByIdAsync(target.Id))!;
            if (await change(target) is { } failure) return failure;
            target.AuthzVersion++;
            var updated = await users.UpdateAsync(target);
            if (!updated.Succeeded) return Rejected(updated);
            var stamped = await users.UpdateSecurityStampAsync(target);
            if (!stamped.Succeeded) return Rejected(stamped);
            Audit(action, actorId, target, null);
            await db.SaveChangesAsync();
            return LifecycleResult.Ok();
        });
    }

    private async Task<LifecycleResult> InTransactionAsync(Func<Task<LifecycleResult>> operation)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var result = await operation();
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                return result;
            }

            await transaction.CommitAsync();
            return result;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return LifecycleResult.Fail(LifecycleFailure.Conflict, "The account changed while saving. Reload and try again.");
        }
    }

    private void Audit(string action, string? actorId, ApplicationUser target, string? detail) =>
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow,
            Action = action,
            ActorUserId = actorId,
            TargetUserId = target.Id,
            TargetUserName = target.UserName,
            Detail = detail
        });

    private DateTimeOffset NewExpiry() => DateTimeOffset.UtcNow.AddDays(authOptions.Value.TemporaryPasswordDays);

    private static LifecycleResult Rejected(IdentityResult result) =>
        LifecycleResult.Fail(LifecycleFailure.Invalid, result.Errors.Select(error => error.Description).ToArray());
}
