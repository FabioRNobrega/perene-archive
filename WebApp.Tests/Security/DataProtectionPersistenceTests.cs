using System.Net;
using System.Net.Http.Json;
using WebApp.Client.Models;
using WebApp.Security;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Security;

public sealed class DataProtectionPersistenceTests
{
    [Fact]
    public async Task Keys_are_persisted_and_cookies_and_tokens_survive_a_restart()
    {
        using var root = new TemporaryDirectory();
        var jar = new CookieJar();
        string token;
        using (var first = new AccountFactory(root.Path))
        {
            using var client = first.CreateDefaultClient(new CookieJarHandler(jar));
            await SignInAsync(client, "admin", "admin");
            token = await GetApiTokenAsync(client);
            Assert.NotEmpty(Directory.GetFiles(first.KeysPath, "*.xml"));
        }

        using var restarted = new AccountFactory(root.Path);
        using var after = restarted.CreateDefaultClient(new CookieJarHandler(jar));

        using var me = await after.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/account/preferences") { Content = JsonContent.Create(new AccountPreferencesDto("Still valid")) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var post = await after.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, post.StatusCode);
    }

    [Fact]
    public async Task Without_the_preserved_keys_an_old_cookie_is_no_longer_valid()
    {
        using var firstRoot = new TemporaryDirectory();
        using var secondRoot = new TemporaryDirectory();
        var jar = new CookieJar();
        using (var first = new AccountFactory(firstRoot.Path))
        {
            using var client = first.CreateDefaultClient(new CookieJarHandler(jar));
            await SignInAsync(client, "admin", "admin");
        }

        using var other = new AccountFactory(secondRoot.Path);
        using var after = other.CreateDefaultClient(new CookieJarHandler(jar));

        Assert.Equal(HttpStatusCode.Unauthorized, (await after.GetAsync("/api/account/me")).StatusCode);
    }

    [Fact]
    public void The_key_ring_uses_the_documented_application_name_and_default_location()
    {
        Assert.Equal("PereneArchive", DataProtectionConfiguration.ApplicationName);
        Assert.Equal("/appdata/keys", DataProtectionConfiguration.DefaultKeysPath);
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        Assert.Equal("/appdata/keys", DataProtectionConfiguration.ResolveKeysPath(configuration));
    }
}
