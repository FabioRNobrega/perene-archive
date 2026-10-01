namespace WebApp.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public const string DefaultPath = "/appdata/perene.db";
    public string Path { get; init; } = DefaultPath;

    public static bool IsValid(DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Path) || !System.IO.Path.IsPathFullyQualified(options.Path)) return false;
        var directory = System.IO.Path.GetDirectoryName(options.Path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
        if (Directory.Exists(options.Path)) return false;
        try
        {
            var probe = System.IO.Path.Combine(directory, $".perene-write-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// True when the database file does not live inside (or equal) any configured archive, cache, or output root,
    /// so that application state can never be reached through, or clobbered by, a media pipeline.
    /// </summary>
    public static bool IsDisjointFromRoots(DatabaseOptions options, IEnumerable<string?> roots)
    {
        if (string.IsNullOrWhiteSpace(options.Path) || !System.IO.Path.IsPathFullyQualified(options.Path)) return true;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var database = System.IO.Path.GetFullPath(options.Path);
        foreach (var rootPath in roots)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !System.IO.Path.IsPathFullyQualified(rootPath)) continue;
            var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(rootPath));
            if (string.Equals(database, root, comparison) ||
                database.StartsWith(root + System.IO.Path.DirectorySeparatorChar, comparison))
            {
                return false;
            }
        }

        return true;
    }
}
