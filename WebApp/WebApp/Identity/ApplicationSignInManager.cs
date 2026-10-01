using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;

namespace WebApp.Identity;

public sealed class ApplicationSignInManager(
    UserManager<ApplicationUser> userManager,
    IUserClaimsPrincipalFactory<ApplicationUser> principalFactory,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<SignInResult> PasswordSignInAsync(string userName, string password, string? authenticatorCode, bool rememberMe)
    {
        var user = await userManager.FindByNameAsync(userName);
        // Never distinguish a missing/inactive account before its password has been verified.
        if (user is null || !await userManager.CheckPasswordAsync(user, password)) return SignInResult.Failed;
        if (!user.IsActive || (user.TemporaryPasswordExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow))
            return SignInResult.NotAllowed;
        if (user.MustChangePassword) return SignInResult.NotAllowed;
        if (user.TwoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(authenticatorCode)) return SignInResult.TwoFactorRequired;
            if (!await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
                authenticatorCode.Replace(" ", string.Empty).Replace("-", string.Empty))) return SignInResult.Failed;
        }

        await httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme,
            await principalFactory.CreateAsync(user),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = rememberMe });
        return SignInResult.Success;
    }

    public async Task<SignInResult> TwoFactorSignInAsync(string userName, string code)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || !user.IsActive || !user.TwoFactorEnabled ||
            !await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code.Replace(" ", string.Empty).Replace("-", string.Empty)))
            return SignInResult.Failed;
        await httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme, await principalFactory.CreateAsync(user));
        return SignInResult.Success;
    }

    /// <summary>True only for an active account whose correct, unexpired temporary password must be replaced.</summary>
    public async Task<bool> RequiresPasswordChangeAsync(string userName, string password)
    {
        var user = await userManager.FindByNameAsync(userName);
        return user is not null && user.IsActive && user.MustChangePassword && !IsTemporaryPasswordExpired(user)
            && await userManager.CheckPasswordAsync(user, password);
    }

    public async Task<IdentityResult> ChangeTemporaryPasswordAsync(string userName, string temporaryPassword, string newPassword)
    {
        var user = await userManager.FindByNameAsync(userName);
        if (user is null || !user.IsActive || !user.MustChangePassword || IsTemporaryPasswordExpired(user)
            || !await userManager.CheckPasswordAsync(user, temporaryPassword))
            return IdentityResult.Failed(new IdentityError { Code = "InvalidTemporaryPassword", Description = "The temporary password is not valid." });

        var changed = await userManager.ChangePasswordAsync(user, temporaryPassword, newPassword);
        if (!changed.Succeeded) return changed;

        user.MustChangePassword = false;
        user.TemporaryPasswordExpiresUtc = null;
        user.AuthzVersion++;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded) return updated;

        await httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme, await principalFactory.CreateAsync(user));
        return IdentityResult.Success;
    }

    private static bool IsTemporaryPasswordExpired(ApplicationUser user) =>
        user.TemporaryPasswordExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow;
}
