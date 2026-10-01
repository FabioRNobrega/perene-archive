using Microsoft.Data.Sqlite;

namespace WebApp.Services;

public sealed record DatabaseBackupResult(string Directory, string DatabaseFile, string KeysDirectory, string MigrationId, int KeyFileCount);

/// <summary>
/// Creates a consistent online SQLite backup (never a raw file copy) plus the Data Protection keys, then verifies it:
/// the copy must pass <c>PRAGMA integrity_check</c> and report the same latest migration as the source.
/// </summary>
public sealed class DatabaseBackupService
{
    public async Task<DatabaseBackupResult> CreateAsync(string databasePath, string keysPath, string backupRoot, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath)) throw new InvalidOperationException("The database file does not exist.");
        if (!Directory.Exists(keysPath)) throw new InvalidOperationException("The Data Protection key directory does not exist.");
        Directory.CreateDirectory(backupRoot);

        var directory = Path.Combine(backupRoot, $"perene-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
        Directory.CreateDirectory(directory);
        try
        {
            var databaseFile = Path.Combine(directory, "perene.db");
            string sourceMigration;
            await using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
            {
                await source.OpenAsync(cancellationToken);
                sourceMigration = await LatestMigrationAsync(source, cancellationToken);
                await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Pooling = false }.ToString());
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
                // The copy inherits WAL mode from the source; switch it off so the backup is one self-contained file.
                await using var journal = destination.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode = DELETE";
                await journal.ExecuteNonQueryAsync(cancellationToken);
            }

            string backupMigration;
            await using (var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await verify.OpenAsync(cancellationToken);
                await using var integrity = verify.CreateCommand();
                integrity.CommandText = "PRAGMA integrity_check";
                if (!string.Equals((string?)await integrity.ExecuteScalarAsync(cancellationToken), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The backup failed the SQLite integrity check.");
                backupMigration = await LatestMigrationAsync(verify, cancellationToken);
            }

            if (!string.Equals(sourceMigration, backupMigration, StringComparison.Ordinal))
                throw new InvalidOperationException("The backup migration version does not match the source database.");

            var keysDirectory = Path.Combine(directory, "keys");
            Directory.CreateDirectory(keysDirectory);
            var copied = 0;
            foreach (var file in Directory.EnumerateFiles(keysPath))
            {
                File.Copy(file, Path.Combine(keysDirectory, Path.GetFileName(file)));
                copied++;
            }

            return new DatabaseBackupResult(directory, databaseFile, keysDirectory, backupMigration, copied);
        }
        catch
        {
            // A failed backup must never be left looking like a good one.
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            throw;
        }
    }

    private static async Task<string> LatestMigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1";
        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) as string
                ?? throw new InvalidOperationException("The database has no applied migrations.");
        }
        catch (SqliteException)
        {
            throw new InvalidOperationException("The database has no migration history.");
        }
    }
}
