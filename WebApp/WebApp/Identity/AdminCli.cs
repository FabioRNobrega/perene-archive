using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebApp.Configuration;
using WebApp.Data;
using WebApp.Security;
using WebApp.Services;

namespace WebApp.Identity;

/// <summary>
/// Offline operator commands dispatched before the web host is built. None of them migrates the database or
/// seeds data; they refuse a schema mismatch and never log a secret. A temporary password is printed exactly once.
/// </summary>
public static class AdminCli
{
    public const string RecoverCommand = "admin-recover";
    public const string BackupCommand = "db-backup";
    public const string UnlockCommand = "db-unlock-migration";
    public const string DefaultBackupPath = "/backups";

    public static bool IsCommand(string[] args) =>
        args.Length > 0 && args[0] is RecoverCommand or BackupCommand or UnlockCommand;

    public static Task<int> RunAsync(string[] args) =>
        RunAsync(args, new ConfigurationBuilder().AddEnvironmentVariables().Build(), Console.Out, Console.Error, TimeSpan.FromSeconds(30));

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter output, TextWriter error, TimeSpan migrationQuietPeriod)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (!DatabaseOptions.IsValid(options))
        {
            await error.WriteLineAsync("Database:Path must be an absolute path in an existing writable directory.");
            return 2;
        }

        try
        {
            return args[0] switch
            {
                RecoverCommand => await RecoverAsync(args, options, configuration, output, error),
                BackupCommand => await BackupAsync(args, options, configuration, output, error),
                UnlockCommand => await UnlockAsync(args, options, output, error, migrationQuietPeriod),
                _ => 2
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or SqliteException or IOException)
        {
            // Messages are written for the operator and never contain passwords, tokens, or key material.
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private static async Task<int> RecoverAsync(string[] args, DatabaseOptions options, IConfiguration configuration, TextWriter output, TextWriter error)
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            await error.WriteLineAsync("Usage: admin-recover <username>");
            return 2;
        }

        if (!File.Exists(options.Path))
        {
            await error.WriteLineAsync("The database does not exist yet. Start the web host once first.");
            return 1;
        }

        await using var db = CreateContext(options);
        if (await DatabaseMigrationGuard.DescribeMismatchAsync(db) is { } mismatch)
        {
            await error.WriteLineAsync(mismatch);
            return 1;
        }

        var normalized = args[1].Trim().ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(account => account.NormalizedUserName == normalized);
        if (user is null)
        {
            await error.WriteLineAsync("No account with that username exists.");
            return 1;
        }

        var days = (configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions()).TemporaryPasswordDays;
        var temporaryPassword = TemporaryPasswords.Generate();
        user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, temporaryPassword);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.IsActive = true;
        user.MustChangePassword = true;
        user.TemporaryPasswordExpiresUtc = DateTimeOffset.UtcNow.AddDays(days > 0 ? days : 7);
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.AuthzVersion++;
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredUtc = DateTimeOffset.UtcNow,
            Action = "user.recovered",
            TargetUserId = user.Id,
            TargetUserName = user.UserName,
            Detail = "offline admin-recover"
        });
        await db.SaveChangesAsync();

        await output.WriteLineAsync($"Temporary password for '{user.UserName}' (shown once, expires in {(days > 0 ? days : 7)} days):");
        await output.WriteLineAsync(temporaryPassword);
        return 0;
    }

    private static async Task<int> BackupAsync(string[] args, DatabaseOptions options, IConfiguration configuration, TextWriter output, TextWriter error)
    {
        var destination = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1]
            : configuration["Backup:Path"] is { Length: > 0 } configured ? configured : DefaultBackupPath;
        var keys = DataProtectionConfiguration.ResolveKeysPath(configuration);
        var result = await new DatabaseBackupService().CreateAsync(options.Path, keys, destination);
        await output.WriteLineAsync($"Backup verified (integrity ok, migration {result.MigrationId}, {result.KeyFileCount} key file(s)).");
        await output.WriteLineAsync($"Location: {result.Directory}");
        return 0;
    }

    private static async Task<int> UnlockAsync(string[] args, DatabaseOptions options, TextWriter output, TextWriter error, TimeSpan quietPeriod)
    {
        if (!args.Contains("--confirm", StringComparer.Ordinal))
        {
            await error.WriteLineAsync("Refusing to clear the migration lock without confirmation. Re-run with CONFIRM=yes only after confirming that no migration is running.");
            return 2;
        }

        if (!File.Exists(options.Path))
        {
            await error.WriteLineAsync("The database does not exist yet.");
            return 1;
        }

        // A migration in progress either holds the write lock or has just touched the database/WAL file.
        var lastWrite = new[] { options.Path, options.Path + "-wal" }
            .Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();
        if (DateTime.UtcNow - lastWrite < quietPeriod)
        {
            await error.WriteLineAsync("Database activity was detected moments ago; a migration may still be running. Wait and retry.");
            return 1;
        }

        // A short timeout (0 would mean "wait forever" to Microsoft.Data.Sqlite) so a held write lock is reported, not awaited.
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = options.Path, Pooling = false, DefaultTimeout = 1 }.ToString());
        await connection.OpenAsync();

        try
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            await using var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsLock'";
            if (Convert.ToInt64(await exists.ExecuteScalarAsync()) == 0)
            {
                await output.WriteLineAsync("No migration lock is present.");
                return 0;
            }

            await using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM __EFMigrationsLock";
            var removed = await delete.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            await output.WriteLineAsync(removed > 0 ? "Stale migration lock cleared." : "No migration lock was held.");
            return 0;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            await error.WriteLineAsync("The database is busy; a migration or write is in progress. Wait and retry.");
            return 1;
        }
    }

    private static AppDbContext CreateContext(DatabaseOptions options) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={options.Path};Foreign Keys=True")
            .AddInterceptors(new SqliteConnectionInterceptor())
            .Options);
}
