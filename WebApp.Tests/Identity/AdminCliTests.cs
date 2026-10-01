using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Data;
using WebApp.Identity;
using static WebApp.Tests.IdentityTestHost;

namespace WebApp.Tests.Identity;

public sealed class AdminCliTests
{
    private sealed record CliRun(int ExitCode, string Output, string Error);

    private static async Task<CliRun> RunAsync(string rootPath, string[] args, TimeSpan? quiet = null, Dictionary<string, string?>? extra = null)
    {
        var state = Path.Combine(rootPath, "state");
        var settings = new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(state, "account-tests.db"),
            ["DataProtection:KeysPath"] = Path.Combine(state, "keys"),
            ["Backup:Path"] = Path.Combine(rootPath, "backups")
        };
        foreach (var pair in extra ?? []) settings[pair.Key] = pair.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await AdminCli.RunAsync(args, configuration, output, error, quiet ?? TimeSpan.Zero);
        SqliteConnection.ClearAllPools();
        return new CliRun(code, output.ToString(), error.ToString());
    }

    /// <summary>Starts the real host once (migrate + seed), then stops it, leaving a current database behind.</summary>
    private static void PrepareDatabase(string rootPath)
    {
        using var factory = new AccountFactory(rootPath);
        using var client = factory.CreateClient();
    }

    [Theory]
    [InlineData("admin-recover", true)]
    [InlineData("db-backup", true)]
    [InlineData("db-unlock-migration", true)]
    [InlineData("--urls", false)]
    [InlineData("anything-else", false)]
    public void Only_the_documented_commands_are_dispatched_offline(string first, bool expected)
    {
        Assert.Equal(expected, AdminCli.IsCommand([first]));
        Assert.False(AdminCli.IsCommand([]));
    }

    [Fact]
    public async Task Recover_prints_a_one_time_temporary_password_that_forces_a_change()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        using (var factory = new AccountFactory(root.Path))
        {
            await CreateMemberAsync(factory, "member", "member-pass");
            await EnrollAuthenticatorAsync(factory, "member");
            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var member = (await users.FindByNameAsync("member"))!;
            member.IsActive = false;
            await users.UpdateAsync(member);
        }

        var run = await RunAsync(root.Path, ["admin-recover", "MEMBER"]);

        Assert.Equal(0, run.ExitCode);
        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var temporary = lines[^1];
        Assert.True(temporary.Length >= 12);
        Assert.Equal(1, run.Output.Split(temporary).Length - 1);
        using var restarted = new AccountFactory(root.Path);
        var member2 = (await FindUserAsync(restarted, "member"))!;
        Assert.True(member2.IsActive);
        Assert.True(member2.MustChangePassword);
        Assert.NotNull(member2.TemporaryPasswordExpiresUtc);
        Assert.True(member2.TwoFactorEnabled); // recovery resets the password, not the authenticator
        using var client = restarted.CreateClient(NoRedirect);
        using var login = await PasswordLoginAsync(client, "member", temporary);
        Assert.Contains("step=change", login.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Recover_clears_a_lockout_and_records_the_event_without_the_secret()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        using (var factory = new AccountFactory(root.Path))
        {
            await CreateMemberAsync(factory, "member", "member-pass");
            using var client = factory.CreateClient(NoRedirect);
            for (var i = 0; i < 5; i++) await PasswordLoginAsync(client, "member", "wrong-pass");
            Assert.NotNull((await FindUserAsync(factory, "member"))!.LockoutEnd);
        }

        var run = await RunAsync(root.Path, ["admin-recover", "member"]);

        Assert.Equal(0, run.ExitCode);
        using var restarted = new AccountFactory(root.Path);
        var member = (await FindUserAsync(restarted, "member"))!;
        Assert.Null(member.LockoutEnd);
        Assert.Equal(0, member.AccessFailedCount);
        using var scope = restarted.Services.CreateScope();
        var events = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.Where(audit => audit.Action == "user.recovered").ToListAsync();
        var audit = Assert.Single(events);
        Assert.Equal("member", audit.TargetUserName);
        var password = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        Assert.DoesNotContain(password, audit.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Recover_reports_unknown_accounts_and_missing_arguments()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);

        var unknown = await RunAsync(root.Path, ["admin-recover", "nobody"]);
        var missing = await RunAsync(root.Path, ["admin-recover"]);

        Assert.Equal(1, unknown.ExitCode);
        Assert.Contains("No account", unknown.Error);
        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("Usage", missing.Error);
    }

    [Fact]
    public async Task Recover_never_creates_or_migrates_the_database()
    {
        using var root = new TemporaryDirectory();
        var state = Path.Combine(root.Path, "state");
        Directory.CreateDirectory(state);
        var databasePath = Path.Combine(state, "account-tests.db");

        var missing = await RunAsync(root.Path, ["admin-recover", "admin"]);
        Assert.Equal(1, missing.ExitCode);
        Assert.False(File.Exists(databasePath));

        // An empty (never migrated) database file is refused and left untouched.
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE marker (id INTEGER)";
            await command.ExecuteNonQueryAsync();
        }

        var unmigrated = await RunAsync(root.Path, ["admin-recover", "admin"]);

        Assert.Equal(1, unmigrated.ExitCode);
        Assert.True(unmigrated.Error.Contains("not been migrated"), unmigrated.Error);
        await using var check = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await check.OpenAsync();
        await using var tables = check.CreateCommand();
        tables.CommandText = "SELECT group_concat(name) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
        Assert.Equal("marker", await tables.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Recover_refuses_a_schema_with_pending_migrations()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        var state = Path.Combine(root.Path, "state");
        await using (var connection = new SqliteConnection($"Data Source={Path.Combine(state, "account-tests.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%AddAuditEvents'";
            await command.ExecuteNonQueryAsync();
        }

        var run = await RunAsync(root.Path, ["admin-recover", "admin"]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("pending migrations", run.Error);
    }

    [Fact]
    public async Task Backup_command_writes_a_verified_backup_beside_a_running_database()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);

        var run = await RunAsync(root.Path, ["db-backup"]);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("integrity ok", run.Output);
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(root.Path, "backups")));
        Assert.True(File.Exists(Path.Combine(backup, "perene.db")));
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(backup, "keys")));
    }

    [Fact]
    public async Task Unlock_refuses_without_confirmation()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);

        var run = await RunAsync(root.Path, ["db-unlock-migration"]);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("confirmation", run.Error);
    }

    [Fact]
    public async Task Unlock_refuses_while_recent_database_activity_is_detected()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        await AddMigrationLockAsync(root.Path);

        var run = await RunAsync(root.Path, ["db-unlock-migration", "--confirm"], TimeSpan.FromHours(1));

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("activity", run.Error);
        Assert.Equal(1, await CountLocksAsync(root.Path));
    }

    [Fact]
    public async Task Unlock_refuses_while_another_connection_holds_the_write_lock()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        await AddMigrationLockAsync(root.Path);
        var databasePath = Path.Combine(root.Path, "state", "account-tests.db");
        await using var writer = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await writer.OpenAsync();
        await using var begin = writer.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE";
        await begin.ExecuteNonQueryAsync();

        var run = await RunAsync(root.Path, ["db-unlock-migration", "--confirm"]);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("busy", run.Error);
    }

    [Fact]
    public async Task Unlock_clears_a_stale_migration_lock_once_confirmed_and_quiet()
    {
        using var root = new TemporaryDirectory();
        PrepareDatabase(root.Path);
        await AddMigrationLockAsync(root.Path);

        var run = await RunAsync(root.Path, ["db-unlock-migration", "--confirm"]);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("cleared", run.Output);
        Assert.Equal(0, await CountLocksAsync(root.Path));
    }

    [Fact]
    public async Task Commands_reject_an_invalid_database_location()
    {
        using var root = new TemporaryDirectory();

        var run = await RunAsync(root.Path, ["db-backup"], extra: new() { ["Database:Path"] = "relative.db" });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Database:Path", run.Error);
    }

    private static async Task AddMigrationLockAsync(string rootPath)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(rootPath, "state", "account-tests.db")};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS __EFMigrationsLock (Id INTEGER NOT NULL PRIMARY KEY, Timestamp TEXT NOT NULL); INSERT OR REPLACE INTO __EFMigrationsLock VALUES (1, '2026-01-01')";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountLocksAsync(string rootPath)
    {
        await using var connection = new SqliteConnection($"Data Source={Path.Combine(rootPath, "state", "account-tests.db")};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsLock";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
