using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace WebApp.Tests.Endpoints;

public sealed class AccountEndpointsTests
{
    [Theory]
    [InlineData("/account/login", "Welcome back")]
    [InlineData("/account/login?step=totp&username=admin", "Verify your sign-in")]
    public async Task Login_pages_render_a_static_dark_document_with_shared_assets(string path, string heading)
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"en\" data-bs-theme=\"dark\">", html);
        Assert.Contains("base href=\"/\"", html);
        Assert.Contains("bootstrap@5.3.8", html);
        Assert.Contains("bootstrap-icons@1.13.1", html);
        Assert.Contains("Montserrat", html);
        Assert.Contains("Zilla+Slab", html);
        Assert.Contains("app.css", html);
        Assert.Contains(heading, html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("_framework/blazor.web.js", html);
    }

    [Fact]
    public async Task Login_page_app_stylesheet_is_served_anonymously_as_css()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var html = await client.GetStringAsync("/account/login");
        var hrefs = System.Text.RegularExpressions.Regex
            .Matches(html, "<link rel=\"stylesheet\" href=\"([^\"]*app\\.css)\"")
            .Select(match => match.Groups[1].Value)
            .ToList();

        Assert.Single(hrefs);
        foreach (var href in hrefs)
        {
            using var response = await client.GetAsync(href);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{href} -> {(int)response.StatusCode} {response.Headers.Location}");
            Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Legacy_raw_html_account_routes_are_not_mapped()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient();

        var patterns = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        Assert.DoesNotContain("/account/change-password", patterns);
        Assert.DoesNotContain("/account/preferences", patterns);
        Assert.DoesNotContain("/account/totp", patterns);
        Assert.Contains("/account/login", patterns);
    }

    [Fact]
    public async Task Anonymous_callers_cannot_use_self_service_account_routes()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var me = await client.GetAsync("/api/account/me");

        Assert.NotEqual(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Members_can_read_their_own_state_but_not_the_family_member_list()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, "member", "member-pass");

        var me = await client.GetFromJsonAsync<AccountMeDto>("/api/account/me");
        using var users = await client.GetAsync("/api/account/users");

        Assert.Equal(new AccountMeDto("Member", false), me);
        Assert.NotEqual(HttpStatusCode.OK, users.StatusCode);
    }

    [Fact]
    public async Task Admins_can_list_family_members()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, "admin", "admin");

        using var users = await client.GetAsync("/api/account/users");

        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
    }

    [Fact]
    public async Task Disabling_the_authenticator_requires_the_current_password()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass", twoFactor: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, "member", "member-pass");
        await SetTwoFactorAsync(factory, "member", enabled: true);

        using var wrong = await PostAsync(client, "/api/account/totp/disable", new DisableTotpDto("wrong"));
        using var empty = await PostAsync(client, "/api/account/totp/disable", new DisableTotpDto(""));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.True((await client.GetFromJsonAsync<AccountMeDto>("/api/account/me"))!.TwoFactorEnabled);

        using var correct = await PostAsync(client, "/api/account/totp/disable", new DisableTotpDto("member-pass"));
        Assert.Equal(HttpStatusCode.NoContent, correct.StatusCode);
        Assert.False((await client.GetFromJsonAsync<AccountMeDto>("/api/account/me"))!.TwoFactorEnabled);
    }

    [Fact]
    public async Task Disabling_the_authenticator_rejects_requests_without_an_antiforgery_token()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, "member", "member-pass");
        await SetTwoFactorAsync(factory, "member", enabled: true);

        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await client.PostAsJsonAsync("/api/account/totp/disable", new DisableTotpDto("member-pass"));
            Assert.False(response.IsSuccessStatusCode);
        });

        Assert.True(failure is null or Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException or HttpRequestException, failure?.ToString());
        Assert.True((await client.GetFromJsonAsync<AccountMeDto>("/api/account/me"))!.TwoFactorEnabled);
    }

    [Fact]
    public async Task Admins_create_members_and_administrators_with_forced_password_change()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, "admin", "admin");

        using var member = await PostAsync(client, "/api/account/users", new CreateAccountDto(" new-member ", "New Member", "temp-pass", false));
        using var admin = await PostAsync(client, "/api/account/users", new CreateAccountDto("new-admin", "New Admin", "temp-pass", true));

        Assert.Equal(HttpStatusCode.Created, member.StatusCode);
        Assert.Equal(HttpStatusCode.Created, admin.StatusCode);
        var list = await client.GetFromJsonAsync<List<AccountUserDto>>("/api/account/users");
        Assert.False(list!.Single(user => user.UserName == "new-member").IsAdmin);
        Assert.True(list!.Single(user => user.UserName == "new-admin").IsAdmin);
        using var scope = factory.Services.CreateScope();
        var stored = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("new-member"))!;
        Assert.True(stored.MustChangePassword);
        Assert.InRange(stored.TemporaryPasswordExpiresUtc!.Value, DateTimeOffset.UtcNow.AddDays(6), DateTimeOffset.UtcNow.AddDays(8));
        Assert.NotNull(stored.CreatedByUserId);
    }

    [Fact]
    public async Task Creating_a_member_rejects_duplicates_weak_passwords_and_non_admins()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(adminClient, "admin", "admin");

        using var duplicate = await PostAsync(adminClient, "/api/account/users", new CreateAccountDto("member", "Again", "temp-pass", false));
        using var weak = await PostAsync(adminClient, "/api/account/users", new CreateAccountDto("weak-user", "Weak", "abc", false));
        using var blank = await PostAsync(adminClient, "/api/account/users", new CreateAccountDto("", "Blank", "temp-pass", false));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Null(await FindUserAsync(factory, "weak-user"));

        using var memberClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(memberClient, "member", "member-pass");
        using var forbidden = await PostAsync(memberClient, "/api/account/users", new CreateAccountDto("sneaky", "Sneaky", "temp-pass", true));
        Assert.NotEqual(HttpStatusCode.Created, forbidden.StatusCode);
        Assert.Null(await FindUserAsync(factory, "sneaky"));
    }

    [Fact]
    public async Task A_temporary_password_must_be_replaced_at_first_sign_in()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "fresh", "temp-pass", mustChange: true);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var login = await PostLoginFormAsync(client, "/account/login", new()
        {
            ["username"] = "fresh", ["password"] = "temp-pass", ["useTwoFactor"] = "false"
        });
        Assert.Contains("step=change", login.Headers.Location?.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/api/account/me")).StatusCode);

        using var mismatch = await PostLoginFormAsync(client, "/account/login/change-password", new()
        {
            ["username"] = "fresh", ["temporaryPassword"] = "temp-pass", ["newPassword"] = "brand-new1", ["confirmPassword"] = "different"
        });
        Assert.Contains("error=1", mismatch.Headers.Location?.ToString());
        Assert.True((await FindUserAsync(factory, "fresh"))!.MustChangePassword);

        using var changed = await PostLoginFormAsync(client, "/account/login/change-password", new()
        {
            ["username"] = "fresh", ["temporaryPassword"] = "temp-pass", ["newPassword"] = "brand-new1", ["confirmPassword"] = "brand-new1"
        });
        Assert.DoesNotContain("error=1", changed.Headers.Location?.ToString());
        Assert.Equal("Member", (await client.GetFromJsonAsync<AccountMeDto>("/api/account/me"))!.DisplayName);
        var stored = (await FindUserAsync(factory, "fresh"))!;
        Assert.False(stored.MustChangePassword);
        Assert.Null(stored.TemporaryPasswordExpiresUtc);

        using var returning = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(returning, "fresh", "brand-new1");
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var old = await PostLoginFormAsync(anonymous, "/account/login", new()
        {
            ["username"] = "fresh", ["password"] = "temp-pass", ["useTwoFactor"] = "false"
        });
        Assert.Contains("error=1", old.Headers.Location?.ToString());
    }

    [Fact]
    public async Task An_expired_temporary_password_cannot_sign_in_or_be_replaced()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "late", "temp-pass", mustChange: true, expiresUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var login = await PostLoginFormAsync(client, "/account/login", new()
        {
            ["username"] = "late", ["password"] = "temp-pass", ["useTwoFactor"] = "false"
        });
        using var change = await PostLoginFormAsync(client, "/account/login/change-password", new()
        {
            ["username"] = "late", ["temporaryPassword"] = "temp-pass", ["newPassword"] = "brand-new1", ["confirmPassword"] = "brand-new1"
        });

        Assert.Contains("error=1", login.Headers.Location?.ToString());
        Assert.Contains("error=1", change.Headers.Location?.ToString());
        Assert.True((await FindUserAsync(factory, "late"))!.MustChangePassword);
    }

    [Fact]
    public async Task Login_page_offers_the_password_toggle_and_serves_its_script_anonymously()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var html = await client.GetStringAsync("/account/login");
        var script = System.Text.RegularExpressions.Regex.Match(html, "<script src=\"([^\"]*passwordToggle[^\"]*\\.js)\"").Groups[1].Value;

        Assert.Contains("data-password-toggle=\"password\"", html);
        Assert.Contains("hidden", html);
        Assert.NotEmpty(script);
        using var response = await client.GetAsync(script);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("javascript", response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task<HttpResponseMessage> PostLoginFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<ApplicationUser?> FindUserAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(username);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AntiforgeryTokenDto>("/api/antiforgery"))!.RequestToken;

    private static async Task SignInAsync(HttpClient client, string username, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client);
        using var response = await client.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["username"] = username,
            ["password"] = password,
            ["useTwoFactor"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("error=1", response.Headers.Location?.ToString());
    }

    private static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", await GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task CreateMemberAsync(WebApplicationFactory<Program> factory, string username, string password, bool twoFactor = false, bool mustChange = false, DateTimeOffset? expiresUtc = null)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = await users.CreateAsync(new ApplicationUser
        {
            UserName = username,
            DisplayName = "Member",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
            MustChangePassword = mustChange,
            TemporaryPasswordExpiresUtc = expiresUtc ?? (mustChange ? DateTimeOffset.UtcNow.AddDays(7) : null),
            TwoFactorEnabled = twoFactor
        }, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
    }

    private static async Task SetTwoFactorAsync(WebApplicationFactory<Program> factory, string username, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync(username))!;
        Assert.True((await users.SetTwoFactorEnabledAsync(user, enabled)).Succeeded);
    }

    private sealed class AccountFactory(string rootPath) : WebApplicationFactory<Program>
    {
        private readonly string _previewPath = CreateDirectory("account-preview");
        private readonly string _cutPath = CreateDirectory("account-cuts");
        private readonly string _compositionPath = CreateDirectory("account-composition");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Program.cs reads Database:Path while building, before ConfigureAppConfiguration applies, so use host settings.
            builder.UseSetting("Database:Path", Path.Combine(rootPath, "account-tests.db"));
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ArchiveRoot:Path"] = rootPath,
                    ["VideoLibrary:Path"] = rootPath,
                    ["ThumbnailCache:Path"] = _previewPath,
                    ["VideoCut:Path"] = _cutPath,
                    ["VideoComposition:Path"] = _compositionPath,
                    ["Database:Path"] = Path.Combine(rootPath, "account-tests.db")
                }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;

            foreach (var path in new[] { _previewPath, _cutPath, _compositionPath })
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
        }

        private static string CreateDirectory(string prefix)
        {
            var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"account-api-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
