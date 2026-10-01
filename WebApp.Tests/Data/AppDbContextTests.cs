using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WebApp.Data;
using WebApp.Data.Entities;
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

    private static ApplicationUser NewUser(string name) => new()
    {
        UserName = name, NormalizedUserName = name.ToUpperInvariant(), DisplayName = name, CreatedUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Folder_permissions_are_unique_and_cascade_with_their_folder_and_user()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var alice = NewUser("alice");
        var folder = new Folder { RootKey = "books", Label = "Books" };
        db.Users.Add(alice);
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        db.FolderPermissions.Add(new FolderPermission { FolderId = folder.Id, UserId = alice.Id });
        await db.SaveChangesAsync();

        db.FolderPermissions.Add(new FolderPermission { FolderId = folder.Id, UserId = alice.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Users.Remove(await db.Users.SingleAsync(user => user.Id == alice.Id));
        await db.SaveChangesAsync();
        Assert.Empty(await db.FolderPermissions.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_folder_cascades_to_children_and_permissions()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var alice = NewUser("alice");
        var parent = new Folder { RootKey = "books", Label = "Books" };
        db.Users.Add(alice);
        db.Folders.Add(parent);
        await db.SaveChangesAsync();
        var child = new Folder { RootKey = "books", RelativePath = "Child", Label = "Child", ParentId = parent.Id };
        db.Folders.Add(child);
        await db.SaveChangesAsync();
        db.FolderPermissions.Add(new FolderPermission { FolderId = child.Id, UserId = alice.Id });
        await db.SaveChangesAsync();

        db.Folders.Remove(await db.Folders.SingleAsync(folder => folder.Id == parent.Id));
        await db.SaveChangesAsync();

        Assert.Empty(await db.Folders.ToListAsync());
        Assert.Empty(await db.FolderPermissions.ToListAsync());
    }

    [Fact]
    public async Task A_folder_owner_cannot_be_deleted_until_ownership_moves()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var owner = NewUser("owner");
        db.Users.Add(owner);
        await db.SaveChangesAsync();
        db.Folders.Add(new Folder { RootKey = "books", Label = "Books", OwnerUserId = owner.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.Users.Remove(await db.Users.SingleAsync(user => user.Id == owner.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_the_grantor_keeps_the_permission_and_clears_its_grantor()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        var alice = NewUser("alice");
        var grantor = NewUser("grantor");
        var folder = new Folder { RootKey = "books", Label = "Books" };
        db.Users.AddRange(alice, grantor);
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        db.FolderPermissions.Add(new FolderPermission { FolderId = folder.Id, UserId = alice.Id, GrantedByUserId = grantor.Id });
        await db.SaveChangesAsync();

        db.Users.Remove(await db.Users.SingleAsync(user => user.Id == grantor.Id));
        await db.SaveChangesAsync();

        Assert.Null((await db.FolderPermissions.AsNoTracking().SingleAsync()).GrantedByUserId);
    }

    [Fact]
    public async Task The_access_policy_only_allows_the_singleton_row()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        db.AccessPolicies.Add(new AccessPolicy { Id = 2 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Existing_personal_reader_themes_become_shared_when_the_owner_column_is_renamed()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261001191802_AddPerUserMediaData");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO AspNetUsers (Id, UserName, DisplayName, CreatedUtc, IsActive, MustChangePassword, AuthzVersion, AccessFailedCount, EmailConfirmed, LockoutEnabled, PhoneNumberConfirmed, TwoFactorEnabled) VALUES ('u1', 'u1', 'U', '2026-01-01', 1, 0, 0, 0, 0, 0, 0, 0)");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ReaderThemes (PublicId, UserId, Name, FontFamily, FontSizePx, LineHeight, ContentPaddingPercent, CreatedUtc) VALUES ('t1', 'u1', 'Night', 'Arial', 20, 1.4, 5, '2026-01-01')");

        await db.Database.MigrateAsync();

        var theme = await db.ReaderThemes.AsNoTracking().SingleAsync();
        Assert.Equal("u1", theme.CreatedByUserId);
        Assert.Equal("Night", theme.Name);
        db.Users.Remove(await db.Users.SingleAsync(user => user.Id == "u1"));
        await db.SaveChangesAsync();
        Assert.Null((await db.ReaderThemes.AsNoTracking().SingleAsync()).CreatedByUserId);
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
