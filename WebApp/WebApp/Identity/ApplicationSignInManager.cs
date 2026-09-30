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
            if (string.IsNullOrWhiteSpace(authenticatorCode) ||
                !await userManager.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
                    authenticatorCode.Replace(" ", string.Empty).Replace("-", string.Empty)))
                return SignInResult.Failed;
        }

        await httpContextAccessor.HttpContext!.SignInAsync(IdentityConstants.ApplicationScheme,
            await principalFactory.CreateAsync(user),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { IsPersistent = rememberMe });
        return SignInResult.Success;
    }
}
