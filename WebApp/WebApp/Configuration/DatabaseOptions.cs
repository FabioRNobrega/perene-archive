namespace WebApp.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string Path { get; init; } = "/appdata/perene.db";

    public static bool IsValid(DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Path) || !System.IO.Path.IsPathFullyQualified(options.Path)) return false;
        var directory = System.IO.Path.GetDirectoryName(options.Path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return false;
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
}
