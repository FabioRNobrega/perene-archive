using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using WebApp.Client.Models;
using WebApp.Identity;
using Net.Codecrete.QrCodeGenerator;
using Microsoft.AspNetCore.Http.HttpResults;
using WebApp.Components.Account;
using WebApp.Security;

namespace WebApp.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Self-service routes act only on the signed-in caller; the member-management routes are Admin-restricted.
        // One group filter validates the antiforgery token on every unsafe method below.
        var accountApi = app.MapGroup("/api/account").RequireAuthorization().AddEndpointFilter<AntiforgeryEndpointFilter>();
        var adminApi = accountApi.MapGroup("/users").RequireAuthorization(policy => policy.RequireRole(AccountLifecycleService.AdminRole));

        adminApi.MapGet("", async (UserManager<ApplicationUser> users) =>
        {
            var adminIds = (await users.GetUsersInRoleAsync(AccountLifecycleService.AdminRole)).Select(user => user.Id).ToHashSet();
            var members = await users.Users.OrderBy(user => user.UserName).ToListAsync();
            return Results.Ok(members.Select(user => new AccountUserDto(
                user.UserName!, user.DisplayName, user.IsActive, user.TwoFactorEnabled, adminIds.Contains(user.Id),
                user.MustChangePassword, user.TemporaryPasswordExpiresUtc,
                user.TemporaryPasswordExpiresUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)).ToList());
        });
        adminApi.MapPost("", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, CreateAccountDto request) =>
        {
            var userName = request.UserName?.Trim() ?? string.Empty;
            var result = await lifecycle.CreateAsync(users.GetUserId(context.User)!, userName, request.DisplayName ?? string.Empty,
                request.TemporaryPassword, request.IsAdmin);
            return result.Succeeded
                ? Results.Created($"/api/account/users/{Uri.EscapeDataString(userName)}",
                    new AccountUserDto(userName, request.DisplayName!.Trim(), true, false, request.IsAdmin, true, null, false))
                : ToResult(result);
        });
        adminApi.MapPost("/{userName}/reset-password", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
        {
            var result = await lifecycle.ResetPasswordAsync(users.GetUserId(context.User)!, userName);
            return result.Succeeded ? Results.Ok(new TemporaryPasswordDto(result.TemporaryPassword!)) : ToResult(result);
        });
        adminApi.MapPost("/{userName}/reset-totp", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.ResetTotpAsync(users.GetUserId(context.User)!, userName)));
        adminApi.MapPost("/{userName}/deactivate", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.SetActiveAsync(users.GetUserId(context.User)!, userName, false)));
        adminApi.MapPost("/{userName}/reactivate", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.SetActiveAsync(users.GetUserId(context.User)!, userName, true)));
        adminApi.MapPost("/{userName}/promote", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.SetAdministratorAsync(users.GetUserId(context.User)!, userName, true)));
        adminApi.MapPost("/{userName}/demote", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.SetAdministratorAsync(users.GetUserId(context.User)!, userName, false)));
        adminApi.MapDelete("/{userName}", async (HttpContext context, UserManager<ApplicationUser> users, AccountLifecycleService lifecycle, string userName) =>
            ToResult(await lifecycle.DeleteAsync(users.GetUserId(context.User)!, userName)));

        accountApi.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users) =>
            await users.GetUserAsync(context.User) is { } user
                ? Results.Ok(new AccountMeDto(user.DisplayName, user.TwoFactorEnabled))
                : Results.Unauthorized());
        // Authenticated and uncacheable: a token is bound to the caller's identity, so it must never be shared or reused.
        app.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new AntiforgeryTokenDto(antiforgery.GetAndStoreTokens(context).RequestToken!));
        }).RequireAuthorization();
        accountApi.MapPost("/preferences", async (HttpContext context, UserManager<ApplicationUser> users, AccountPreferencesDto request) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (displayName.Length == 0 || displayName.Length > 128) return Results.BadRequest(new[] { "Enter a display name of up to 128 characters." });
            user.DisplayName = displayName;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest(Errors(result));
        });
        accountApi.MapPost("/change-password", async (HttpContext context, UserManager<ApplicationUser> users, ApplicationSignInManager signIn, ChangePasswordDto request) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            // Clearing the forced-change state rides in the same save as the new hash, then the session is re-issued
            // because the password change rotates the security stamp.
            user.MustChangePassword = false;
            user.TemporaryPasswordExpiresUtc = null;
            user.AuthzVersion++;
            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded) return Results.BadRequest(Errors(result));
            await signIn.RefreshSessionAsync(user);
            return Results.NoContent();
        });
        accountApi.MapPost("/totp", async (HttpContext context, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Results.Conflict(new[] { "Disable the current authenticator before setting up a new one." });
            await users.ResetAuthenticatorKeyAsync(user);
            var key = (await users.GetAuthenticatorKeyAsync(user))!;
            var uri = $"otpauth://totp/{Uri.EscapeDataString($"PereneArchive:{user.UserName}")}?secret={key}&issuer=PereneArchive&digits=6";
            var svg = QrCode.EncodeText(uri, QrCode.Ecc.Medium).ToSvgString(4);
            var qrCodeDataUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            return Results.Ok(new TotpSetupDto(key, qrCodeDataUrl, []));
        });
        accountApi.MapPost("/totp/confirm", async (HttpContext context, UserManager<ApplicationUser> users, ApplicationSignInManager signIn, TotpConfirmationDto request) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", string.Empty).Replace("-", string.Empty));
            if (!valid) return Results.BadRequest(new[] { "That code was not accepted." });
            await users.SetTwoFactorEnabledAsync(user, true);
            var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? [];
            await signIn.RefreshSessionAsync(user);
            return Results.Ok(codes);
        });
        accountApi.MapPost("/totp/disable", async (HttpContext context, UserManager<ApplicationUser> users, ApplicationSignInManager signIn, DisableTotpDto request) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (string.IsNullOrEmpty(request.CurrentPassword) || !await users.CheckPasswordAsync(user, request.CurrentPassword))
                return Results.BadRequest(new[] { "The password was not accepted." });
            var disabled = await users.SetTwoFactorEnabledAsync(user, false);
            if (!disabled.Succeeded) return Results.BadRequest(Errors(disabled));
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) return Results.BadRequest(Errors(reset));
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
            user = (await users.GetUserAsync(context.User))!;
            user.AuthzVersion++;
            if (!(await users.UpdateAsync(user)).Succeeded) return Results.BadRequest();
            await signIn.RefreshSessionAsync(user);
            return Results.NoContent();
        });
        accountApi.MapPost("/logout", async (ApplicationSignInManager signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });
        app.MapGet("/account/login", (string? returnUrl, string? step, string? username, string? error, string? reason) =>
                new RazorComponentResult<AccountDocument>(new Dictionary<string, object?> { ["Page"] = "login", ["ReturnUrl"] = returnUrl, ["ShowTotp"] = step == "totp", ["ShowChange"] = step == "change", ["Username"] = username, ["HasError"] = error is not null, ["Reason"] = reason }))
            .AllowAnonymous();
        app.MapPost("/account/login", async (HttpContext context, IAntiforgery antiforgery, ApplicationSignInManager signIn,
            [FromForm] string? username, [FromForm] string? password, [FromForm] string? authenticatorCode, [FromForm] bool useTwoFactor, [FromForm] string? returnUrl) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            username ??= string.Empty;
            var next = Uri.EscapeDataString(IsLocal(returnUrl) ? returnUrl! : "/");
            var result = useTwoFactor
                ? await signIn.TwoFactorSignInAsync(authenticatorCode ?? string.Empty)
                : await signIn.PasswordSignInAsync(username, password ?? string.Empty);
            if (result.IsNotAllowed && !useTwoFactor)
            {
                if (await signIn.RequiresPasswordChangeAsync(username, password ?? string.Empty))
                    return Results.Redirect($"/account/login?step=change&username={Uri.EscapeDataString(username)}&returnUrl={next}");
                if (await signIn.IsTemporaryPasswordExpiredAsync(username, password ?? string.Empty))
                    return Results.Redirect($"/account/login?error=1&reason=expired&returnUrl={next}");
            }
            if (result.RequiresTwoFactor)
                return Results.Redirect($"/account/login?step=totp&returnUrl={next}");
            if (result.Succeeded)
                return Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/");
            return Results.Redirect(useTwoFactor ? $"/account/login?step=totp&error=1&returnUrl={next}" : $"/account/login?error=1&returnUrl={next}");
        }).AllowAnonymous();
        app.MapPost("/account/login/change-password", async (HttpContext context, IAntiforgery antiforgery, ApplicationSignInManager signIn,
            [FromForm] string username, [FromForm] string temporaryPassword, [FromForm] string newPassword, [FromForm] string confirmPassword, [FromForm] string? returnUrl) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var retry = $"/account/login?step=change&username={Uri.EscapeDataString(username)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}&error=1";
            if (newPassword != confirmPassword) return Results.Redirect(retry + "&reason=mismatch");
            var (result, requiresTwoFactor) = await signIn.ChangeTemporaryPasswordAsync(username, temporaryPassword, newPassword);
            if (!result.Succeeded) return Results.Redirect(retry);
            return requiresTwoFactor
                ? Results.Redirect($"/account/login?step=totp&returnUrl={Uri.EscapeDataString(IsLocal(returnUrl) ? returnUrl! : "/")}")
                : Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/");
        }).AllowAnonymous();
        app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            await context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            return Results.Redirect("/account/login");
        }).RequireAuthorization();
        app.MapGet("/account/logout", () => new RazorComponentResult<AccountDocument>(new Dictionary<string, object?> { ["Page"] = "logout" })).RequireAuthorization();
        return app;
    }

    private static bool IsLocal(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//") && !value.StartsWith("/\\");

    private static string[] Errors(IdentityResult result) => result.Errors.Select(error => error.Description).ToArray();

    private static IResult ToResult(LifecycleResult result) => result.Failure switch
    {
        LifecycleFailure.None => Results.NoContent(),
        LifecycleFailure.NotFound => Results.NotFound(result.Errors),
        LifecycleFailure.Conflict or LifecycleFailure.LastAdministrator => Results.Conflict(result.Errors),
        _ => Results.BadRequest(result.Errors)
    };
}
