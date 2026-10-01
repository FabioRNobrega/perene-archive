using Microsoft.EntityFrameworkCore;

namespace WebApp.Data;

/// <summary>Offline-command schema check: commands that never migrate must refuse any schema that is not exactly current.</summary>
public static class DatabaseMigrationGuard
{
    public static async Task<string?> DescribeMismatchAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        if (applied.Count == 0) return "The database has not been migrated yet. Start the web host once first.";
        if (applied.Any(id => !known.Contains(id))) return "The database was migrated by a newer build than this one.";
        if (known.Except(applied).Any()) return "The database has pending migrations. Start the web host to apply them first.";
        return null;
    }
}
