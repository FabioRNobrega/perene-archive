using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace WebApp.Data;

/// <summary>Applies the per-connection SQLite pragmas: WAL journaling, a busy timeout, and foreign-key enforcement.</summary>
public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private const int SqliteReadOnly = 8;
    private const string ConnectionPragmas = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
    private const string JournalPragma = "PRAGMA journal_mode = WAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Execute(connection, ConnectionPragmas);
        try { Execute(connection, JournalPragma); }
        // EF probes for a database with a read-only connection; WAL is a persistent file setting, so skipping it there is safe.
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteReadOnly) { }
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, ConnectionPragmas, cancellationToken);
        try { await ExecuteAsync(connection, JournalPragma, cancellationToken); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == SqliteReadOnly) { }
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
