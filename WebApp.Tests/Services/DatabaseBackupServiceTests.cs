using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Identity;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class DatabaseBackupServiceTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    public void Dispose() => _root.Dispose();

    private string DatabasePath => Path.Combine(_root.Path, "live.db");
    private string KeysPath => Path.Combine(_root.Path, "keys");
    private string BackupRoot => Path.Combine(_root.Path, "backups");

    private async Task CreateLiveDatabaseAsync()
    {
        Directory.CreateDirectory(KeysPath);
        await File.WriteAllTextAsync(Path.Combine(KeysPath, "key-1.xml"), "<key id=\"1\" />");
        await File.WriteAllTextAsync(Path.Combine(KeysPath, "key-2.xml"), "<key id=\"2\" />");
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={DatabasePath};Foreign Keys=True").AddInterceptors(new SqliteConnectionInterceptor()).Options);
        await db.Database.MigrateAsync();
        db.Users.Add(new ApplicationUser { UserName = "kept", NormalizedUserName = "KEPT", DisplayName = "Kept", CreatedUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Backup_copies_the_database_online_and_the_data_protection_keys()
    {
        await CreateLiveDatabaseAsync();
        // A second connection keeps the database open (and in WAL mode) while the backup runs.
        await using var writer = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await writer.OpenAsync();

        var result = await new DatabaseBackupService().CreateAsync(DatabasePath, KeysPath, BackupRoot);

        Assert.True(File.Exists(result.DatabaseFile));
        Assert.Equal(["keys", "perene.db"], Directory.GetFileSystemEntries(result.Directory).Select(Path.GetFileName).Order().ToArray());
        Assert.Equal(2, result.KeyFileCount);
        Assert.Equal(2, Directory.GetFiles(result.KeysDirectory).Length);
        Assert.EndsWith("AddFolderAccessControl", result.MigrationId);
        await using var restored = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={result.DatabaseFile};Pooling=False").Options);
        Assert.Equal("Kept", (await restored.Users.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task Each_backup_gets_its_own_directory()
    {
        await CreateLiveDatabaseAsync();
        var service = new DatabaseBackupService();

        var first = await service.CreateAsync(DatabasePath, KeysPath, BackupRoot);
        var second = await service.CreateAsync(DatabasePath, KeysPath, BackupRoot);

        Assert.NotEqual(first.Directory, second.Directory);
        Assert.Equal(2, Directory.GetDirectories(BackupRoot).Length);
    }

    [Fact]
    public async Task A_database_without_migration_history_is_rejected_and_leaves_no_partial_backup()
    {
        Directory.CreateDirectory(KeysPath);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE t (id INTEGER)";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseBackupService().CreateAsync(DatabasePath, KeysPath, BackupRoot));

        Assert.Empty(Directory.GetDirectories(BackupRoot));
    }

    [Fact]
    public async Task Missing_inputs_are_rejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseBackupService().CreateAsync(DatabasePath, KeysPath, BackupRoot));
        await CreateLiveDatabaseAsync();
        Directory.Delete(KeysPath, recursive: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DatabaseBackupService().CreateAsync(DatabasePath, KeysPath, BackupRoot));
    }
}
