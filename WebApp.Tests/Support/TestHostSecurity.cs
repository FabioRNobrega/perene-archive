using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebApp.Data;
using WebApp.Identity;
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
            services.AddHostedService<TestAdminSeeder>();
        });
    }

    /// <summary>
    /// Folder access decisions read the account from the database (Admin membership included), so the fake signed-in admin
    /// needs a real row. It is created once the host starts, after migrations and role seeding have run.
    /// </summary>
    private sealed class TestAdminSeeder(IServiceScopeFactory scopes) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Users.AnyAsync(user => user.Id == AdminId, cancellationToken)) return;
            db.Users.Add(new ApplicationUser
            {
                Id = AdminId,
                UserName = "test-admin",
                NormalizedUserName = "TEST-ADMIN",
                DisplayName = "Test Admin",
                CreatedUtc = DateTimeOffset.UtcNow,
                IsActive = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid().ToString("N")
            });
            var adminRoleId = await db.Roles.Where(role => role.Name == "Admin").Select(role => role.Id).FirstAsync(cancellationToken);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = AdminId, RoleId = adminRoleId });
            await db.SaveChangesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public const string AdminId = "test-admin";

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
