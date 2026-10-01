using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Identity;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Identity;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public async Task Unauthenticated_apis_return_401_and_pages_redirect_to_login()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        foreach (var api in new[] { "/api/videos", "/api/cuts", "/api/account/me", "/api/antiforgery" })
        {
            using var response = await client.GetAsync(api);
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{api} -> {(int)response.StatusCode}");
        }

        using var page = await client.GetAsync("/videos");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Contains("/account/login", page.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Only_the_sign_in_flow_and_static_assets_are_anonymous()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        using var login = await client.GetAsync("/account/login");
        var html = await login.Content.ReadAsStringAsync();
        var stylesheet = System.Text.RegularExpressions.Regex.Match(html, "href=\"([^\"]*app[^\"]*\\.css)\"").Groups[1].Value;
        using var asset = await client.GetAsync(stylesheet);
        using var logout = await client.GetAsync("/account/logout");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
    }

    [Fact]
    public async Task Failures_before_the_password_is_verified_share_one_generic_message()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        await CreateMemberAsync(factory, "sleeper", "sleeper-pass");
        using var admin = factory.CreateClient(NoRedirect);
        await SignInAsync(admin, "admin", "admin");
        using var deactivate = await SendWithTokenAsync(admin, HttpMethod.Post, "/api/account/users/sleeper/deactivate");
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        using var client = factory.CreateClient(NoRedirect);

        var unknown = await PasswordLoginAsync(client, "nobody", "whatever");
        var wrong = await PasswordLoginAsync(client, "member", "wrong-pass");
        var inactiveWrong = await PasswordLoginAsync(client, "sleeper", "wrong-pass");

        Assert.Equal(unknown.Headers.Location?.ToString(), wrong.Headers.Location?.ToString());
        Assert.Equal(unknown.Headers.Location?.ToString(), inactiveWrong.Headers.Location?.ToString());
        Assert.DoesNotContain("reason", wrong.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Inactive_accounts_cannot_sign_in_even_with_the_right_password()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "sleeper", "sleeper-pass");
        using var admin = factory.CreateClient(NoRedirect);
        await SignInAsync(admin, "admin", "admin");
        using var deactivate = await SendWithTokenAsync(admin, HttpMethod.Post, "/api/account/users/sleeper/deactivate");
        using var client = factory.CreateClient(NoRedirect);

        using var response = await PasswordLoginAsync(client, "sleeper", "sleeper-pass");

        Assert.Contains("error=1", response.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Expiry_is_disclosed_only_after_the_temporary_password_proves_valid()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "late", "temp-pass", mustChange: true, expiresUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = factory.CreateClient(NoRedirect);

        using var wrong = await PasswordLoginAsync(client, "late", "not-the-password");
        using var correct = await PasswordLoginAsync(client, "late", "temp-pass");

        Assert.DoesNotContain("expired", wrong.Headers.Location?.ToString());
        Assert.Contains("reason=expired", correct.Headers.Location?.ToString());
        var page = await client.GetStringAsync(correct.Headers.Location!.ToString());
        Assert.Contains("temporary password has expired", page);
    }

    [Fact]
    public async Task Totp_step_cannot_be_reached_without_first_proving_the_password()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var (code, _) = await EnrollAuthenticatorAsync(factory, "member");
        using var client = factory.CreateClient(NoRedirect);

        using var skipPassword = await PostLoginFormAsync(client, "/account/login", new()
        {
            ["username"] = "member", ["authenticatorCode"] = code, ["useTwoFactor"] = "true"
        });

        Assert.Contains("error=1", skipPassword.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Totp_sign_in_requires_the_password_then_an_authenticator_code()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var (code, _) = await EnrollAuthenticatorAsync(factory, "member");
        using var client = factory.CreateClient(NoRedirect);

        using var password = await PasswordLoginAsync(client, "member", "member-pass");
        Assert.Contains("step=totp", password.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);

        using var wrong = await PostLoginFormAsync(client, "/account/login", new() { ["authenticatorCode"] = "000000", ["useTwoFactor"] = "true" });
        Assert.Contains("error=1", wrong.Headers.Location?.ToString());

        using var verified = await PostLoginFormAsync(client, "/account/login", new() { ["authenticatorCode"] = code, ["useTwoFactor"] = "true" });
        Assert.DoesNotContain("error=1", verified.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_is_then_spent()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var (_, recovery) = await EnrollAuthenticatorAsync(factory, "member");
        var recoveryCode = recovery[0];

        using var first = factory.CreateClient(NoRedirect);
        await PasswordLoginAsync(first, "member", "member-pass");
        using var redeemed = await PostLoginFormAsync(first, "/account/login", new() { ["authenticatorCode"] = recoveryCode, ["useTwoFactor"] = "true" });
        Assert.DoesNotContain("error=1", redeemed.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/account/me")).StatusCode);

        using var second = factory.CreateClient(NoRedirect);
        await PasswordLoginAsync(second, "member", "member-pass");
        using var reused = await PostLoginFormAsync(second, "/account/login", new() { ["authenticatorCode"] = recoveryCode, ["useTwoFactor"] = "true" });
        Assert.Contains("error=1", reused.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Totp_and_recovery_paths_refuse_inactive_accounts()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        var (code, _) = await EnrollAuthenticatorAsync(factory, "member");
        using var client = factory.CreateClient(NoRedirect);
        await PasswordLoginAsync(client, "member", "member-pass");
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var member = (await users.FindByNameAsync("member"))!;
            member.IsActive = false;
            await users.UpdateAsync(member);
        }

        using var response = await PostLoginFormAsync(client, "/account/login", new() { ["authenticatorCode"] = code, ["useTwoFactor"] = "true" });

        Assert.Contains("error=1", response.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_a_temporary_password_for_an_authenticator_account_still_requires_the_code()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "temp-pass", mustChange: true);
        var (code, _) = await EnrollAuthenticatorAsync(factory, "member");
        using var client = factory.CreateClient(NoRedirect);

        using var changed = await PostLoginFormAsync(client, "/account/login/change-password", new()
        {
            ["username"] = "member", ["temporaryPassword"] = "temp-pass", ["newPassword"] = "brand-new1", ["confirmPassword"] = "brand-new1"
        });

        Assert.Contains("step=totp", changed.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/account/me")).StatusCode);
        using var verified = await PostLoginFormAsync(client, "/account/login", new() { ["authenticatorCode"] = code, ["useTwoFactor"] = "true" });
        Assert.DoesNotContain("error=1", verified.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account_out_without_revealing_it()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(NoRedirect);

        for (var i = 0; i < 5; i++) await PasswordLoginAsync(client, "member", "wrong-pass");
        using var locked = await PasswordLoginAsync(client, "member", "member-pass");

        Assert.Contains("error=1", locked.Headers.Location?.ToString());
        Assert.DoesNotContain("reason", locked.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Changing_your_own_password_keeps_the_session_signed_in()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "member", "member-pass");
        var stampBefore = (await FindUserAsync(factory, "member"))!.SecurityStamp;

        using var response = await PostAsync(client, "/api/account/change-password", new ChangePasswordDto("member-pass", "new-member-pass"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEqual(stampBefore, (await FindUserAsync(factory, "member"))!.SecurityStamp);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public async Task Cookie_security_stamp_validation_runs_every_minute()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);

        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SecurityStampValidatorOptions>>().Value;

        Assert.Equal(TimeSpan.FromMinutes(1), options.ValidationInterval);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Cookies_are_http_only_lax_and_same_as_request()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        using var response = await PasswordLoginAsync(client, "admin", "admin");
        using var change = await PostLoginFormAsync(client, "/account/login/change-password", new()
        {
            ["username"] = "admin", ["temporaryPassword"] = "admin", ["newPassword"] = "new-admin-pass", ["confirmPassword"] = "new-admin-pass"
        });

        var cookie = change.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secure", cookie.Replace("SameSite", string.Empty, StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Return_urls_must_stay_on_this_site()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(NoRedirect);

        using var response = await PostLoginFormAsync(client, "/account/login", new()
        {
            ["username"] = "member", ["password"] = "member-pass", ["useTwoFactor"] = "false", ["returnUrl"] = "https://evil.example/"
        });

        Assert.Equal("/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Admin_endpoints_reject_ordinary_members()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        await CreateMemberAsync(factory, "other", "other-pass");
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "member", "member-pass");

        foreach (var action in new[] { "reset-password", "reset-totp", "deactivate", "reactivate", "promote", "demote" })
        {
            using var response = await SendWithTokenAsync(client, HttpMethod.Post, $"/api/account/users/other/{action}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var delete = await SendWithTokenAsync(client, HttpMethod.Delete, "/api/account/users/other");
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.True((await FindUserAsync(factory, "other"))!.IsActive);
    }

    [Fact]
    public async Task Admin_lifecycle_endpoints_reset_deactivate_and_delete_with_last_admin_protection()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "admin", "admin");

        using var reset = await SendWithTokenAsync(client, HttpMethod.Post, "/api/account/users/member/reset-password");
        var temporary = (await reset.Content.ReadFromJsonAsync<TemporaryPasswordDto>())!.TemporaryPassword;
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(temporary));

        var list = await client.GetFromJsonAsync<List<AccountUserDto>>("/api/account/users");
        var row = list!.Single(user => user.UserName == "member");
        Assert.True(row.MustChangePassword);
        Assert.NotNull(row.TemporaryPasswordExpiresUtc);
        Assert.False(row.TemporaryPasswordExpired);

        using var demoteSelf = await SendWithTokenAsync(client, HttpMethod.Post, "/api/account/users/admin/demote");
        using var deactivateSelf = await SendWithTokenAsync(client, HttpMethod.Post, "/api/account/users/admin/deactivate");
        using var deleteSelf = await SendWithTokenAsync(client, HttpMethod.Delete, "/api/account/users/admin");
        Assert.Equal(HttpStatusCode.Conflict, demoteSelf.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, deactivateSelf.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deleteSelf.StatusCode);

        using var delete = await SendWithTokenAsync(client, HttpMethod.Delete, "/api/account/users/member");
        using var missing = await SendWithTokenAsync(client, HttpMethod.Post, "/api/account/users/member/deactivate");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task The_admin_list_marks_expired_temporary_passwords_without_secrets()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "late", "temp-pass", mustChange: true, expiresUtc: DateTimeOffset.UtcNow.AddHours(-1));
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "admin", "admin");

        var body = await client.GetStringAsync("/api/account/users");
        var list = System.Text.Json.JsonSerializer.Deserialize<List<AccountUserDto>>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.True(list!.Single(user => user.UserName == "late").TemporaryPasswordExpired);
        Assert.DoesNotContain("temp-pass", body);
        Assert.DoesNotContain("PasswordHash", body, StringComparison.OrdinalIgnoreCase);
    }
}
