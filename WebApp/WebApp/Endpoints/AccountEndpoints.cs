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

namespace WebApp.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        // Self-service routes act only on the signed-in caller; only the member list is Admin-restricted.
        var accountApi = app.MapGroup("/api/account").RequireAuthorization();
        accountApi.MapGet("/users", async (UserManager<ApplicationUser> users) =>
        {
            var adminIds = (await users.GetUsersInRoleAsync("Admin")).Select(user => user.Id).ToHashSet();
            var members = await users.Users.OrderBy(user => user.UserName).ToListAsync();
            return Results.Ok(members.Select(user => new AccountUserDto(user.UserName!, user.DisplayName, user.IsActive, user.TwoFactorEnabled, adminIds.Contains(user.Id))).ToList());
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
        accountApi.MapPost("/users", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, CreateAccountDto request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var userName = request.UserName?.Trim() ?? string.Empty;
            var displayName = request.DisplayName?.Trim() ?? string.Empty;
            if (userName.Length == 0 || displayName.Length == 0 || string.IsNullOrEmpty(request.TemporaryPassword))
                return Results.BadRequest(new[] { "Username, display name and a temporary password are required." });
            if (await users.FindByNameAsync(userName) is not null)
                return Results.Conflict(new[] { "That username is already taken." });

            var creator = await users.GetUserAsync(context.User);
            var user = new ApplicationUser
            {
                UserName = userName,
                DisplayName = displayName,
                CreatedUtc = DateTimeOffset.UtcNow,
                CreatedByUserId = creator?.Id,
                IsActive = true,
                MustChangePassword = true,
                TemporaryPasswordExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            };
            var created = await users.CreateAsync(user, request.TemporaryPassword);
            if (!created.Succeeded)
                return Results.BadRequest(created.Errors.Select(error => error.Description).ToArray());
            if (request.IsAdmin && !(await users.AddToRoleAsync(user, "Admin")).Succeeded)
            {
                await users.DeleteAsync(user);
                return Results.BadRequest(new[] { "The administrator role could not be assigned." });
            }
            return Results.Created($"/api/account/users/{Uri.EscapeDataString(userName)}",
                new AccountUserDto(userName, displayName, true, false, request.IsAdmin));
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
        accountApi.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users) =>
            await users.GetUserAsync(context.User) is { } user
                ? Results.Ok(new AccountMeDto(user.DisplayName, user.TwoFactorEnabled))
                : Results.Unauthorized());
        app.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new AntiforgeryTokenDto(antiforgery.GetAndStoreTokens(context).RequestToken!))).AllowAnonymous();
        accountApi.MapPost("/preferences", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, AccountPreferencesDto request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            user.DisplayName = request.DisplayName.Trim();
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest();
        });
        accountApi.MapPost("/change-password", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, ChangePasswordDto request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            return result.Succeeded ? Results.NoContent() : Results.BadRequest();
        });
        accountApi.MapPost("/totp", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            await users.ResetAuthenticatorKeyAsync(user);
            var key = (await users.GetAuthenticatorKeyAsync(user))!;
            var uri = $"otpauth://totp/{Uri.EscapeDataString($"PereneArchive:{user.UserName}")}?secret={key}&issuer=PereneArchive&digits=6";
            var svg = QrCode.EncodeText(uri, QrCode.Ecc.Medium).ToSvgString(4);
            var qrCodeDataUrl = "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
            return Results.Ok(new TotpSetupDto(key, qrCodeDataUrl, []));
        });
        accountApi.MapPost("/totp/confirm", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, TotpConfirmationDto request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, request.Code.Replace(" ", string.Empty).Replace("-", string.Empty));
            if (!valid) return Results.BadRequest();
            await users.SetTwoFactorEnabledAsync(user, true);
            return Results.Ok((await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? []);
        });
        accountApi.MapPost("/totp/disable", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, DisableTotpDto request) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Unauthorized();
            if (string.IsNullOrEmpty(request.CurrentPassword) || !await users.CheckPasswordAsync(user, request.CurrentPassword))
                return Results.BadRequest();
            var disabled = await users.SetTwoFactorEnabledAsync(user, false);
            if (!disabled.Succeeded) return Results.BadRequest();
            var reset = await users.ResetAuthenticatorKeyAsync(user);
            if (!reset.Succeeded) return Results.BadRequest();
            user = (await users.GetUserAsync(context.User))!;
            user.AuthzVersion++;
            return (await users.UpdateAsync(user)).Succeeded ? Results.NoContent() : Results.BadRequest();
        });
        accountApi.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.NoContent();
        });
        app.MapGet("/account/login", (string? returnUrl, string? step, string? username, string? error, string? reason) =>
                new RazorComponentResult<AccountDocument>(new Dictionary<string, object?> { ["Page"] = "login", ["ReturnUrl"] = returnUrl, ["ShowTotp"] = step == "totp", ["ShowChange"] = step == "change", ["Username"] = username, ["HasError"] = error is not null, ["Reason"] = reason }))
            .AllowAnonymous();
        app.MapPost("/account/login", async (HttpContext context, IAntiforgery antiforgery, ApplicationSignInManager signIn,
            [FromForm] string username, [FromForm] string? password, [FromForm] string? authenticatorCode, [FromForm] bool useTwoFactor, [FromForm] string? returnUrl) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var result = useTwoFactor
                ? await signIn.TwoFactorSignInAsync(username, authenticatorCode ?? string.Empty)
                : await signIn.PasswordSignInAsync(username, password ?? string.Empty, null, false);
            if (result.IsNotAllowed && !useTwoFactor && await signIn.RequiresPasswordChangeAsync(username, password ?? string.Empty))
                return Results.Redirect($"/account/login?step=change&username={Uri.EscapeDataString(username)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            if (result.RequiresTwoFactor)
                return Results.Redirect($"/account/login?step=totp&username={Uri.EscapeDataString(username)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            return result.Succeeded
                ? Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/")
                : Results.Redirect("/account/login?error=1");
        }).AllowAnonymous();
        app.MapPost("/account/login/change-password", async (HttpContext context, IAntiforgery antiforgery, ApplicationSignInManager signIn,
            [FromForm] string username, [FromForm] string temporaryPassword, [FromForm] string newPassword, [FromForm] string confirmPassword, [FromForm] string? returnUrl) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var retry = $"/account/login?step=change&username={Uri.EscapeDataString(username)}&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}&error=1";
            if (newPassword != confirmPassword) return Results.Redirect(retry + "&reason=mismatch");
            var result = await signIn.ChangeTemporaryPasswordAsync(username, temporaryPassword, newPassword);
            return result.Succeeded ? Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/") : Results.Redirect(retry);
        }).AllowAnonymous();
        app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Redirect("/account/login");
        }).RequireAuthorization();
        app.MapGet("/account/logout", () => new RazorComponentResult<AccountDocument>(new Dictionary<string, object?> { ["Page"] = "logout" })).RequireAuthorization();
        return app;
    }

    private static bool IsLocal(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//");
}
