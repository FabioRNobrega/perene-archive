using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Identity;

namespace WebApp.Tests;

/// <summary>A real-Identity host (no test authentication shortcut) over a private SQLite file and key ring.</summary>
public sealed class AccountFactory : WebApplicationFactory<Program>
{
    private readonly string _previewPath = CreateDirectory("account-preview");
    private readonly string _cutPath = CreateDirectory("account-cuts");
    private readonly string _compositionPath = CreateDirectory("account-composition");
    private readonly string _rootPath;
    private readonly Dictionary<string, string?> _extraSettings;

    public AccountFactory(string rootPath, Dictionary<string, string?>? extraSettings = null)
    {
        _rootPath = rootPath;
        _extraSettings = extraSettings ?? [];
    }

    /// <summary>Folder that holds the database and key ring; reuse the same root to simulate a restart.</summary>
    public string ArchivePath => Path.Combine(_rootPath, "archive");
    public string StatePath => Path.Combine(_rootPath, "state");
    public string DatabasePath => Path.Combine(StatePath, "account-tests.db");
    public string KeysPath => Path.Combine(StatePath, "keys");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(StatePath);
        Directory.CreateDirectory(ArchivePath);
        // Program.cs reads these while building, before ConfigureAppConfiguration applies, so use host settings.
        builder.UseSetting("Database:Path", DatabasePath);
        builder.UseSetting("DataProtection:KeysPath", KeysPath);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ArchiveRoot:Path"] = ArchivePath,
                ["VideoLibrary:Path"] = ArchivePath,
                ["ThumbnailCache:Path"] = _previewPath,
                ["VideoCut:Path"] = _cutPath,
                ["VideoComposition:Path"] = _compositionPath
            }.Concat(_extraSettings).ToDictionary(pair => pair.Key, pair => pair.Value)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        SqliteConnection.ClearAllPools();
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

public sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"account-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

/// <summary>Cookies shared by every <see cref="CookieJarHandler"/> given the same jar, so a session can outlive a host restart.</summary>
public sealed class CookieJar
{
    public Dictionary<string, string> Cookies { get; } = [];
}

/// <summary>Replays cookies by hand so a session can be carried from one host instance to a restarted one.</summary>
public sealed class CookieJarHandler(CookieJar jar) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("Cookie");
        if (jar.Cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", jar.Cookies.Select(pair => $"{pair.Key}={pair.Value}")));
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var header in setCookies)
            {
                var pair = header.Split(';')[0];
                var index = pair.IndexOf('=');
                if (index <= 0) continue;
                var name = pair[..index];
                var value = pair[(index + 1)..];
                if (value.Length == 0) jar.Cookies.Remove(name); else jar.Cookies[name] = value;
            }
        }

        return response;
    }
}

public static class IdentityTestHost
{
    public static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    public static async Task<ApplicationUser?> FindUserAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(username);
    }

    /// <summary>The anonymous form token embedded in the static sign-in page (the static forms use framework antiforgery).</summary>
    public static async Task<string> GetLoginFormTokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/account/login");
        return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    }

    /// <summary>The API request token; requires an authenticated session because tokens are bound to the caller.</summary>
    public static async Task<string> GetApiTokenAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AntiforgeryTokenDto>("/api/antiforgery"))!.RequestToken;

    public static async Task<HttpResponseMessage> PostLoginFormAsync(HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetLoginFormTokenAsync(client);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static async Task<HttpResponseMessage> PasswordLoginAsync(HttpClient client, string username, string password) =>
        await PostLoginFormAsync(client, "/account/login", new()
        {
            ["username"] = username,
            ["password"] = password,
            ["useTwoFactor"] = "false"
        });

    /// <summary>Signs in, completing the first-sign-in password change (to <c>password + "-changed"</c>) when one is required.</summary>
    public static async Task SignInAsync(HttpClient client, string username, string password)
    {
        using var response = await PasswordLoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("error=1", response.Headers.Location?.ToString());

        if (response.Headers.Location?.ToString().Contains("step=change", StringComparison.Ordinal) is true)
        {
            using var changed = await PostLoginFormAsync(client, "/account/login/change-password", new()
            {
                ["username"] = username,
                ["temporaryPassword"] = password,
                ["newPassword"] = password + "-changed",
                ["confirmPassword"] = password + "-changed"
            });
            Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
            Assert.DoesNotContain("error=1", changed.Headers.Location?.ToString());
        }
    }

    public static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", await GetApiTokenAsync(client));
        return await client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, HttpMethod method, string path)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-CSRF-TOKEN", await GetApiTokenAsync(client));
        return await client.SendAsync(request);
    }

    public static async Task CreateMemberAsync(WebApplicationFactory<Program> factory, string username, string password, bool twoFactor = false, bool mustChange = false, DateTimeOffset? expiresUtc = null, bool admin = false)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = username,
            DisplayName = "Member",
            CreatedUtc = DateTimeOffset.UtcNow,
            IsActive = true,
            MustChangePassword = mustChange,
            TemporaryPasswordExpiresUtc = expiresUtc ?? (mustChange ? DateTimeOffset.UtcNow.AddDays(7) : null),
            TwoFactorEnabled = twoFactor
        };
        var result = await users.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(error => error.Description)));
        if (admin) Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
    }

    public static async Task SetTwoFactorAsync(WebApplicationFactory<Program> factory, string username, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync(username))!;
        Assert.True((await users.SetTwoFactorEnabledAsync(user, enabled)).Succeeded);
    }

    /// <summary>Enables an authenticator for the user and returns the current valid code plus recovery codes.</summary>
    public static async Task<(string Code, IReadOnlyList<string> RecoveryCodes)> EnrollAuthenticatorAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync(username))!;
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        var recovery = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        return (CurrentTotpCode(key), recovery);
    }

    /// <summary>RFC 6238 (SHA-1, 30 s, 6 digits) for a base32 authenticator key; Identity cannot generate authenticator codes itself.</summary>
    public static string CurrentTotpCode(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var character in base32Key.Replace(" ", string.Empty).ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
        }

        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(counter);
        var hash = System.Security.Cryptography.HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }
}
