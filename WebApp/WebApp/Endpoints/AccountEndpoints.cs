using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using WebApp.Identity;

namespace WebApp.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accountApi = app.MapGroup("/api/account").RequireAuthorization(policy => policy.RequireRole("Admin"));
        accountApi.MapGet("/users", async (UserManager<ApplicationUser> users) =>
            Results.Ok(users.Users.OrderBy(user => user.UserName).Select(user => new
            {
                user.UserName,
                user.DisplayName,
                user.IsActive,
                user.TwoFactorEnabled
            }).ToList()));
        app.MapGet("/account/login", (HttpContext context, IAntiforgery antiforgery, string? returnUrl) =>
                Results.Content(LoginPage(returnUrl, antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html"))
            .AllowAnonymous();
        app.MapPost("/account/login", async (HttpContext context, IAntiforgery antiforgery, ApplicationSignInManager signIn,
            [FromForm] string username, [FromForm] string password, [FromForm] string? authenticatorCode, [FromForm] string? returnUrl) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var result = await signIn.PasswordSignInAsync(username, password, authenticatorCode, false);
            return result.Succeeded
                ? Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/")
                : Results.Redirect("/account/login?error=1");
        }).AllowAnonymous();
        app.MapPost("/account/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Results.Redirect("/account/login");
        }).RequireAuthorization();
        app.MapGet("/account/change-password", (HttpContext context, IAntiforgery antiforgery) =>
                Results.Content(ChangePasswordPage(antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html"))
            .RequireAuthorization();
        app.MapPost("/account/change-password", async (HttpContext context, IAntiforgery antiforgery,
            UserManager<ApplicationUser> users, [FromForm] string currentPassword, [FromForm] string newPassword) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Redirect("/account/login");
            var result = await users.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded) return Results.Redirect("/account/change-password?error=1");
            user.MustChangePassword = false;
            user.TemporaryPasswordExpiresUtc = null;
            user.AuthzVersion++;
            await users.UpdateAsync(user);
            return Results.Redirect("/?passwordChanged=1");
        }).RequireAuthorization();
        app.MapGet("/account/preferences", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Redirect("/account/login") : Results.Content(PreferencesPage(antiforgery.GetAndStoreTokens(context).RequestToken!, user.DisplayName), "text/html");
        }).RequireAuthorization();
        app.MapPost("/account/preferences", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users, [FromForm] string displayName) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Redirect("/account/login");
            user.DisplayName = displayName.Trim();
            await users.UpdateAsync(user);
            return Results.Redirect("/family");
        }).RequireAuthorization();
        app.MapGet("/account/totp", (HttpContext context, IAntiforgery antiforgery) => Results.Content(TotpPage(antiforgery.GetAndStoreTokens(context).RequestToken!, null, []), "text/html")).RequireAuthorization();
        app.MapPost("/account/totp", async (HttpContext context, IAntiforgery antiforgery, UserManager<ApplicationUser> users) =>
        {
            await antiforgery.ValidateRequestAsync(context);
            var user = await users.GetUserAsync(context.User);
            if (user is null) return Results.Redirect("/account/login");
            await users.ResetAuthenticatorKeyAsync(user);
            await users.SetTwoFactorEnabledAsync(user, true);
            var key = await users.GetAuthenticatorKeyAsync(user);
            var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            return Results.Content(TotpPage(antiforgery.GetAndStoreTokens(context).RequestToken!, key, recoveryCodes ?? []), "text/html");
        }).RequireAuthorization();
        app.MapGet("/account/logout", (HttpContext context, IAntiforgery antiforgery) => Results.Content(LogoutPage(antiforgery.GetAndStoreTokens(context).RequestToken!), "text/html")).RequireAuthorization();
        return app;
    }

    private static bool IsLocal(string? value) => !string.IsNullOrWhiteSpace(value) && value.StartsWith('/') && !value.StartsWith("//");
    private static string LoginPage(string? returnUrl, string requestToken) => $$"""
        <!doctype html><html data-bs-theme="dark"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"><title>Sign in · PereneArchive</title></head>
        <body class="bg-body-tertiary"><main class="container py-5"><section class="card shadow-sm mx-auto" style="max-width:28rem"><div class="card-body p-4"><h1 class="h3">Sign in</h1><p class="text-body-secondary">Use your PereneArchive account.</p><form method="post" action="/account/login"><input name="__RequestVerificationToken" type="hidden" value="{{System.Net.WebUtility.HtmlEncode(requestToken)}}"><input type="hidden" name="returnUrl" value="{{System.Net.WebUtility.HtmlEncode(returnUrl ?? "/")}}"><div class="mb-3"><label class="form-label" for="username">Username</label><input class="form-control" id="username" name="username" autocomplete="username" required></div><div class="mb-3"><label class="form-label" for="password">Password</label><input class="form-control" id="password" name="password" type="password" autocomplete="current-password" required></div><div class="mb-3"><label class="form-label" for="authenticatorCode">Authenticator code <span class="text-body-secondary">(if enabled)</span></label><input class="form-control" id="authenticatorCode" name="authenticatorCode" inputmode="numeric" autocomplete="one-time-code"></div><button class="btn btn-primary w-100">Sign in</button></form></div></section></main></body></html>
        """;

    private static string ChangePasswordPage(string requestToken) => $$"""
        <!doctype html><html data-bs-theme="dark"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"><title>Change password · PereneArchive</title></head>
        <body class="bg-body-tertiary"><main class="container py-5"><section class="card shadow-sm mx-auto" style="max-width:28rem"><div class="card-body p-4"><h1 class="h3">Change password</h1><form method="post" action="/account/change-password"><input name="__RequestVerificationToken" type="hidden" value="{{System.Net.WebUtility.HtmlEncode(requestToken)}}"><div class="mb-3"><label class="form-label" for="currentPassword">Current password</label><input class="form-control" id="currentPassword" name="currentPassword" type="password" autocomplete="current-password" required></div><div class="mb-3"><label class="form-label" for="newPassword">New password</label><input class="form-control" id="newPassword" name="newPassword" type="password" autocomplete="new-password" required></div><button class="btn btn-primary w-100">Update password</button></form></div></section></main></body></html>
        """;

    private static string PreferencesPage(string requestToken, string displayName) => $$"""
        <!doctype html><html data-bs-theme="dark"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"><title>Preferences · PereneArchive</title></head><body class="bg-body-tertiary"><main class="container py-5"><section class="card shadow-sm mx-auto" style="max-width:28rem"><div class="card-body p-4"><h1 class="h3">Your preferences</h1><form method="post"><input name="__RequestVerificationToken" type="hidden" value="{{System.Net.WebUtility.HtmlEncode(requestToken)}}"><label class="form-label" for="displayName">Display name</label><input class="form-control mb-3" id="displayName" name="displayName" value="{{System.Net.WebUtility.HtmlEncode(displayName)}}" required><button class="btn btn-primary">Save</button></form></div></section></main></body></html>
        """;

    private static string TotpPage(string requestToken, string? key, IEnumerable<string> recoveryCodes) => $$"""
        <!doctype html><html data-bs-theme="dark"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"><title>Authenticator · PereneArchive</title></head><body class="bg-body-tertiary"><main class="container py-5"><section class="card shadow-sm mx-auto" style="max-width:34rem"><div class="card-body p-4"><h1 class="h3">Authenticator app</h1>{{(key is null ? "<p>Enable TOTP to obtain a new authenticator key.</p><form method=\"post\"><input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"" + System.Net.WebUtility.HtmlEncode(requestToken) + "\"><button class=\"btn btn-primary\">Enable TOTP</button></form>" : "<p>Add this key to your authenticator app. Save these recovery codes now; they will not be shown again.</p><code class=\"d-block p-3 bg-body-secondary mb-3\">" + System.Net.WebUtility.HtmlEncode(key) + "</code><ul>" + string.Join("", recoveryCodes.Select(code => "<li><code>" + System.Net.WebUtility.HtmlEncode(code) + "</code></li>")) + "</ul>")}}</div></section></main></body></html>
        """;

    private static string LogoutPage(string requestToken) => $$"""
        <!doctype html><html data-bs-theme="dark"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.8/dist/css/bootstrap.min.css" rel="stylesheet"><title>Sign out · PereneArchive</title></head><body class="bg-body-tertiary"><main class="container py-5"><section class="card shadow-sm mx-auto" style="max-width:28rem"><div class="card-body p-4"><h1 class="h3">Sign out</h1><form method="post" action="/account/logout"><input name="__RequestVerificationToken" type="hidden" value="{{System.Net.WebUtility.HtmlEncode(requestToken)}}"><button class="btn btn-outline-danger">Sign out</button></form></div></section></main></body></html>
        """;
}
