using System.Net;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Security;

public sealed class NoAuthBypassTests
{
    [Theory]
    [InlineData("Authentication:Enabled", "false")]
    [InlineData("Authentication:Disabled", "true")]
    [InlineData("Auth:Enabled", "false")]
    [InlineData("Auth:Disabled", "true")]
    [InlineData("DisableAuthentication", "true")]
    [InlineData("AllowAnonymous", "true")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Development")]
    public async Task No_configuration_value_turns_authentication_off(string key, string value)
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path, new() { [key] = value });
        using var client = factory.CreateClient(NoRedirect);

        using var api = await client.GetAsync("/api/videos");
        using var page = await client.GetAsync("/videos");

        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }

    [Fact]
    public async Task Unsafe_methods_are_not_reachable_anonymously()
    {
        using var root = new TemporaryDirectory();
        using var factory = new AccountFactory(root.Path);
        using var client = factory.CreateClient(NoRedirect);

        using var response = await client.PostAsync("/api/videos/scan", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
