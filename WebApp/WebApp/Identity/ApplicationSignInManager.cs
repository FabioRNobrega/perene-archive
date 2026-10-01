using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace WebApp.Identity;

/// <summary>
/// Sign-in rules shared by the password, authenticator-code, and recovery-code paths: inactive and expired-temporary
/// accounts never receive a session, a wrong password never reveals whether the account exists, and the account
/// state (expired, must change) is disclosed only once the password has proven valid.
/// </summary>
public sealed class ApplicationSignInManager(
    UserManager<ApplicationUser> userManager,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<SignInResult> PasswordSignInAsync(string userName, string password)
    {
        var user = await userManager.FindByNameAsync(userName);
        // Never distinguish a missing/locked/wrong-password account before the password has been verified.
        if (user is null || await userManager.IsLockedOutAsync(user)) return SignInResult.Failed;
        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return SignInResult.Failed;
        }

        if (!user.IsActive || IsTemporaryPasswordExpired(user) || user.MustChangePassword) return SignInResult.NotAllowed;
        if (user.TwoFactorEnabled)
        {
            await StoreTwoFactorUserAsync(user);
            return SignInResult.TwoFactorRequired;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        await SignInAsync(user);
        return SignInResult.Success;
    }

    /// <summary>Completes a password sign-in with an authenticator code or an unused recovery code.</summary>
    public async Task<SignInResult> TwoFactorSignInAsync(string code)
    {
        var context = httpContextAccessor.HttpContext!;
        var pending = await context.AuthenticateAsync(IdentityConstants.TwoFactorUserIdScheme);
        var userId = pending.Principal?.FindFirstValue(ClaimTypes.Name);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null || !user.TwoFactorEnabled || await userManager.IsLockedOutAsync(user)) return SignInResult.Failed;

        // Authenticator codes are digits only; recovery codes keep their dash, so strip just the spaces for those.
        var recoveryCode = code.Replace(" ", string.Empty);
        var authenticatorCode = recoveryCode.Replace("-", string.Empty);
        var valid = recoveryCode.Length > 0 && (
            await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, authenticatorCode) ||
            (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode)).Succeeded);
        if (!valid)
        {
            await userManager.AccessFailedAsync(user);
            return SignInResult.Failed;
        }

        // The password was already proven, so disclosing the account state here is safe; the generic failure still applies.
        if (!user.IsActive || IsTemporaryPasswordExpired(user) || user.MustChangePassword) return SignInResult.NotAllowed;

        await userManager.ResetAccessFailedCountAsync(user);
        await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await SignInAsync(user);
        return SignInResult.Success;
    }

    /// <summary>True only for an active account whose correct, unexpired temporary password must be replaced.</summary>
    public async Task<bool> RequiresPasswordChangeAsync(string userName, string password)
    {
        var user = await userManager.FindByNameAsync(userName);
        return user is not null && user.IsActive && user.MustChangePassword && !IsTemporaryPasswordExpired(user)
            && await userManager.CheckPasswordAsync(user, password);
    }

    /// <summary>True only after the supplied password proves valid for an active account whose temporary password has expired.</summary>
    public async Task<bool> IsTemporaryPasswordExpiredAsync(string userName, string password)
    {
        var user = await userManager.FindByNameAsync(userName);
        return user is not null && user.IsActive && IsTemporaryPasswordExpired(user)
            && await userManager.CheckPasswordAsync(user, password);
    }

    /// <summary>
    /// Replaces a temporary password, atomically clearing the forced-change state with the new password hash.
    /// Accounts with an authenticator must still complete that step before receiving a session.
    /// </summary>
    public async Task<(IdentityResult Result, bool RequiresTwoFactor)> ChangeTemporaryPasswordAsync(string userName, string temporaryPassword, string newPassword)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || !user.IsActive || !user.MustChangePassword || IsTemporaryPasswordExpired(user)
            || await userManager.IsLockedOutAsync(user) || !await userManager.CheckPasswordAsync(user, temporaryPassword))
            return (IdentityResult.Failed(new IdentityError { Code = "InvalidTemporaryPassword", Description = "The temporary password is not valid." }), false);

        // ChangePasswordAsync saves the whole user record in one update, so the new hash and the cleared
        // forced-change state are persisted together or not at all.
        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresUtc = null;
        user.AuthzVersion++;
        var changed = await userManager.ChangePasswordAsync(user, temporaryPassword, newPassword);
        if (!changed.Succeeded) return (changed, false);

        if (user.TwoFactorEnabled)
        {
            await StoreTwoFactorUserAsync(user);
            return (IdentityResult.Success, true);
        }

        await SignInAsync(user);
        return (IdentityResult.Success, false);
    }

    /// <summary>Issues a fresh session for an already-authenticated user after their security stamp changed.</summary>
    public Task RefreshSessionAsync(ApplicationUser user) => SignInAsync(user);

    public Task SignOutAsync() => httpContextAccessor.HttpContext!.SignOutAsync(IdentityConstants.ApplicationScheme);

    private async Task SignInAsync(ApplicationUser user) =>
        await httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme, await principalFactory.CreateAsync(user));

    private Task StoreTwoFactorUserAsync(ApplicationUser user)
    {
        var identity = new ClaimsIdentity(IdentityConstants.TwoFactorUserIdScheme);
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Id));
        return httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.TwoFactorUserIdScheme, new ClaimsPrincipal(identity));
    }

    private static bool IsTemporaryPasswordExpired(ApplicationUser user) =>
        user.TemporaryPasswordExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow;
}
