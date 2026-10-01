using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Security;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Security;

public sealed class AntiforgeryTests
{
    private static readonly HashSet<string> UnsafeMethods = ["POST", "PUT", "PATCH", "DELETE"];

    [Fact]
    public async Task The_request_token_endpoint_requires_authentication_and_is_not_cacheable()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        using var anonymous = await client.GetAsync("/api/antiforgery");
        await SignInAsync(client, "admin", "admin");
        using var authenticated = await client.GetAsync("/api/antiforgery");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        Assert.Contains("no-store", authenticated.Headers.CacheControl?.ToString());
        Assert.False(string.IsNullOrWhiteSpace((await authenticated.Content.ReadFromJsonAsync<AntiforgeryTokenDto>())!.RequestToken));
    }

    [Fact]
    public async Task Every_unsafe_api_endpoint_rejects_a_request_without_a_token()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "admin", "admin");

        var endpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(UnsafeMethods.Contains)
                .Select(method => (Method: method, Pattern: endpoint.RoutePattern.RawText!)))
            .Distinct()
            .ToList();
        Assert.True(endpoints.Count > 40, $"Expected the unsafe API surface to be enumerated, found {endpoints.Count}.");

        var unprotected = new List<string>();
        foreach (var (method, pattern) in endpoints)
        {
            var path = System.Text.RegularExpressions.Regex.Replace(pattern, "\\{[^}:]+(:int)?\\}", match => match.Value.Contains(":int") ? "1" : "x");
            // The folder-thumbnail upload binds a multipart form before the filter runs, so it needs a form body to get that far.
            HttpContent body = pattern.EndsWith("/folder-thumbnail", StringComparison.Ordinal)
                ? new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "thumb.jpg" } }
                : JsonContent.Create(new { });
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = body };
            using var response = await client.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.BadRequest || !response.Headers.Contains(AntiforgeryEndpointFilter.FailureHeader))
                unprotected.Add($"{method} {pattern} -> {(int)response.StatusCode}");
        }

        Assert.True(unprotected.Count == 0, "Unsafe endpoints without antiforgery coverage:\n" + string.Join('\n', unprotected));
    }

    [Fact]
    public async Task A_valid_token_is_accepted_and_safe_methods_never_need_one()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "admin", "admin");

        using var get = await client.GetAsync("/api/account/me");
        using var post = await PostAsync(client, "/api/account/preferences", new AccountPreferencesDto("Renamed"));

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
    }

    [Fact]
    public async Task A_token_issued_to_another_user_is_rejected()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        await CreateMemberAsync(factory, "member", "member-pass");
        using var admin = factory.CreateClient(NoRedirect);
        using var member = factory.CreateClient(NoRedirect);
        await SignInAsync(admin, "admin", "admin");
        await SignInAsync(member, "member", "member-pass");
        var adminToken = await GetApiTokenAsync(admin);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/preferences") { Content = JsonContent.Create(new AccountPreferencesDto("Hijack")) };
        request.Headers.Add("X-CSRF-TOKEN", adminToken);
        using var response = await member.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Member", (await member.GetFromJsonAsync<AccountMeDto>("/api/account/me"))!.DisplayName);
    }

    [Fact]
    public async Task A_stale_token_from_before_sign_in_is_rejected_after_sign_in()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);
        var anonymousToken = await GetLoginFormTokenAsync(client);
        await SignInAsync(client, "admin", "admin");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/preferences") { Content = JsonContent.Create(new AccountPreferencesDto("Stale")) };
        request.Headers.Add("X-CSRF-TOKEN", anonymousToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/account/login")]
    [InlineData("/account/login/change-password")]
    public async Task Static_forms_reject_posts_without_the_form_token(string path)
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = "admin", ["password"] = "admin", ["useTwoFactor"] = "false",
                ["temporaryPassword"] = "admin", ["newPassword"] = "new-admin-pass", ["confirmPassword"] = "new-admin-pass"
            }));
            // A rejected token is a 400, which the status-code pages middleware turns into a redirect to /not-found (then login).
            var location = response.Headers.Location?.ToString() ?? string.Empty;
            Assert.True(!location.Contains("step=", StringComparison.Ordinal) && !location.EndsWith("localhost/", StringComparison.Ordinal) && location != "/", $"accepted the post: {response.Headers.Location}");
        });

        Assert.True(failure is null or Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException, failure?.ToString());
        Assert.True((await FindUserAsync(factory, "admin"))!.MustChangePassword);
    }

    [Fact]
    public async Task The_static_sign_out_form_rejects_a_post_without_the_form_token()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);
        await SignInAsync(client, "admin", "admin");

        var failure = await Record.ExceptionAsync(async () =>
        {
            using var response = await client.PostAsync("/account/logout", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        });

        Assert.True(failure is null or Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException, failure?.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/account/me")).StatusCode);
    }
}
