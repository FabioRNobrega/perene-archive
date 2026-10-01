using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Identity;

namespace WebApp.Tests.Data;

public sealed class AppDbContextTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    public void Dispose() => _root.Dispose();

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={Path.Combine(_root.Path, "test.db")};Foreign Keys=True")
        .AddInterceptors(new SqliteConnectionInterceptor())
        .Options);

    [Fact]
    public async Task Migrations_create_a_real_database_with_no_pending_model_changes()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True(File.Exists(Path.Combine(_root.Path, "test.db")));
        Assert.Null(await DatabaseMigrationGuard.DescribeMismatchAsync(db));
    }

    [Fact]
    public async Task Every_connection_uses_wal_a_busy_timeout_and_foreign_keys()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.OpenConnectionAsync();
        var connection = db.Database.GetDbConnection();

        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode"));
        Assert.Equal("5000", await ScalarAsync(connection, "PRAGMA busy_timeout"));
        Assert.Equal("1", await ScalarAsync(connection, "PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Foreign_keys_are_enforced()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = "missing-user", RoleId = "missing-role" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Timestamps_round_trip_in_utc()
    {
        var created = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
            db.Users.Add(new ApplicationUser { UserName = "utc", NormalizedUserName = "UTC", DisplayName = "Utc", CreatedUtc = created, TemporaryPasswordExpiresUtc = created.AddDays(7) });
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var stored = await read.Users.SingleAsync(user => user.UserName == "utc");
        Assert.Equal(created, stored.CreatedUtc);
        Assert.Equal(TimeSpan.Zero, stored.CreatedUtc.Offset);
        Assert.Equal(created.AddDays(7), stored.TemporaryPasswordExpiresUtc);
    }

    [Fact]
    public async Task A_database_without_migrations_is_reported_as_a_mismatch()
    {
        await using var db = CreateContext();

        var mismatch = await DatabaseMigrationGuard.DescribeMismatchAsync(db);

        Assert.Contains("not been migrated", mismatch);
    }

    [Fact]
    public async Task A_database_migrated_by_a_newer_build_is_reported_as_a_mismatch()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('99990101000000_Future', '99.0.0')");

        Assert.Contains("newer build", await DatabaseMigrationGuard.DescribeMismatchAsync(db));
    }

    private static async Task<string?> ScalarAsync(System.Data.Common.DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
