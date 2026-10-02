using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

/// <summary>
/// Hands out output-name numbers atomically. Each series (kind, output directory, prefix, extension) keeps its last value in
/// SQLite, seeded once from the files already on disk, so numbers are unique and consecutive and never regress after deletion.
/// </summary>
internal sealed class NamingCounterService(IServiceScopeFactory scopes)
{
    private readonly object _gate = new();

    /// <summary>
    /// Returns the next number for the series. <paramref name="seed"/> runs only the first time a series is seen and returns the
    /// highest number already present on disk.
    /// </summary>
    public int Allocate(string kind, string directory, string prefix, string extension, Func<int> seed)
    {
        var normalized = Normalize(directory);
        prefix = prefix.ToLowerInvariant(); // name matching on disk is case-insensitive
        extension = extension.ToLowerInvariant();
        lock (_gate)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var transaction = db.Database.BeginTransaction();
            var counter = db.NamingCounters.FirstOrDefault(c =>
                c.Kind == kind && c.Directory == normalized && c.Prefix == prefix && c.Extension == extension);
            if (counter is null)
            {
                counter = new NamingCounter { Kind = kind, Directory = normalized, Prefix = prefix, Extension = extension, LastValue = Math.Max(0, seed()) };
                db.NamingCounters.Add(counter);
            }

            counter.LastValue++;
            db.SaveChanges();
            transaction.Commit();
            return counter.LastValue;
        }
    }

    /// <summary>
    /// Allocates a number and returns the first path built from it that is not already taken. A file that appeared outside the app
    /// after seeding simply makes the allocator skip ahead; the counter never moves backwards.
    /// </summary>
    public string AllocatePath(string kind, string directory, string prefix, string extension, Func<int> seed, Func<int, string> build)
    {
        while (true)
        {
            var path = build(Allocate(kind, directory, prefix, extension, seed));
            if (!File.Exists(path)) return path;
        }
    }

    internal static string Normalize(string directory) => Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>The highest 4-digit counter among files named "<paramref name="stem"/> NNNN" with the given extension.</summary>
    internal static int HighestOnDisk(string directory, string stem, string extension)
    {
        if (!Directory.Exists(directory)) return 0;
        var max = 0;
        foreach (var path in Directory.EnumerateFiles(directory, $"*{extension}", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith(stem + " ", StringComparison.OrdinalIgnoreCase)) continue;
            var text = name[(stem.Length + 1)..];
            if (text.Length == 4 && int.TryParse(text, out var value) && value > max) max = value;
        }

        return max;
    }
}
