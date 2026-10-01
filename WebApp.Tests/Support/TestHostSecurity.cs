using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebApp.Tests;

/// <summary>
/// For tests that exercise media/archive endpoints rather than authentication: gives the host a private throwaway
/// database and key ring, signs every request in as an Admin, and accepts every antiforgery token. Authentication
/// behavior itself is covered by the real-Identity factories in the account/security tests.
/// </summary>
public static class TestHostSecurity
{
    public const string Scheme = "TestAdmin";

    public static void Apply(IWebHostBuilder builder)
    {
        // Not under the system temp folder: several suites use it as their archive root, and the database must stay outside every media root.
        var directory = Path.Combine(AppContext.BaseDirectory, "test-state", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        builder.UseSetting("Database:Path", Path.Combine(directory, "perene.db"));
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(directory, "keys"));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = Scheme;
                    options.DefaultChallengeScheme = Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAdminHandler>(Scheme, _ => { });
            services.RemoveAll<IAntiforgery>();
            services.AddSingleton<IAntiforgery, AcceptAllAntiforgery>();
        });
    }

    private sealed class TestAdminHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "test-admin"), new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Admin")],
                TestHostSecurity.Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestHostSecurity.Scheme)));
        }
    }

    private sealed class AcceptAllAntiforgery : IAntiforgery
    {
        private static readonly AntiforgeryTokenSet Tokens = new("request-token", "cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN");
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => Tokens;
        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => Tokens;
        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(true);
        public Task ValidateRequestAsync(HttpContext httpContext) => Task.CompletedTask;
        public void SetCookieTokenAndHeader(HttpContext httpContext) { }
    }
}
