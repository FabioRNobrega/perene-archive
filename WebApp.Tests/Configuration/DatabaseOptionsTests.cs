using WebApp.Configuration;

namespace WebApp.Tests.Configuration;

public sealed class DatabaseOptionsTests
{
    [Fact]
    public void Defaults_to_the_appdata_volume()
    {
        Assert.Equal("/appdata/perene.db", new DatabaseOptions().Path);
    }

    [Fact]
    public void Accepts_an_absolute_file_in_a_writable_directory()
    {
        using var root = new TemporaryDirectory();

        Assert.True(DatabaseOptions.IsValid(new DatabaseOptions { Path = Path.Combine(root.Path, "perene.db") }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/perene.db")]
    [InlineData("perene.db")]
    public void Rejects_missing_or_relative_paths(string path)
    {
        Assert.False(DatabaseOptions.IsValid(new DatabaseOptions { Path = path }));
    }

    [Fact]
    public void Rejects_a_missing_directory()
    {
        using var root = new TemporaryDirectory();

        Assert.False(DatabaseOptions.IsValid(new DatabaseOptions { Path = Path.Combine(root.Path, "missing", "perene.db") }));
    }

    [Fact]
    public void Rejects_a_path_that_is_a_directory()
    {
        using var root = new TemporaryDirectory();

        Assert.False(DatabaseOptions.IsValid(new DatabaseOptions { Path = root.Path }));
    }

    [Fact]
    public void Rejects_an_unwritable_directory()
    {
        if (Environment.UserName == "root") return; // root ignores directory permissions

        using var root = new TemporaryDirectory();
        var locked = Path.Combine(root.Path, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.False(DatabaseOptions.IsValid(new DatabaseOptions { Path = Path.Combine(locked, "perene.db") }));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData("/archive/perene.db", "/archive", false)]
    [InlineData("/archive/state/perene.db", "/archive", false)]
    [InlineData("/archive", "/archive/", false)]
    [InlineData("/archive-state/perene.db", "/archive", true)]
    [InlineData("/appdata/perene.db", "/archive", true)]
    public void Rejects_a_database_inside_an_archive_cache_or_output_root(string database, string root, bool expected)
    {
        Assert.Equal(expected, DatabaseOptions.IsDisjointFromRoots(new DatabaseOptions { Path = database }, [root]));
    }

    [Fact]
    public void Ignores_roots_that_are_not_configured()
    {
        Assert.True(DatabaseOptions.IsDisjointFromRoots(new DatabaseOptions { Path = "/appdata/perene.db" }, [null, "", "relative"]));
    }

    [Fact]
    public void Auth_options_require_positive_values()
    {
        Assert.True(AuthOptions.HasPositiveTemporaryPasswordDays(new AuthOptions()));
        Assert.True(AuthOptions.HasPositiveValidationInterval(new AuthOptions()));
        Assert.False(AuthOptions.HasPositiveTemporaryPasswordDays(new AuthOptions { TemporaryPasswordDays = 0 }));
        Assert.False(AuthOptions.HasPositiveValidationInterval(new AuthOptions { SecurityStampValidationMinutes = 0 }));
        Assert.Equal(1, new AuthOptions().SecurityStampValidationMinutes);
    }

    [Fact]
    public async Task Host_startup_rejects_a_database_inside_the_archive_root()
    {
        using var root = new TemporaryDirectory();
        using var factory = new DatabaseInsideArchiveFactory(root.Path);

        var failure = await Record.ExceptionAsync(async () => { using var client = factory.CreateClient(); await Task.CompletedTask; });

        Assert.NotNull(failure);
        Assert.Contains("Database:Path", failure!.ToString());
    }

    private sealed class DatabaseInsideArchiveFactory(string root) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Path", Path.Combine(root, "perene.db"));
            builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), $"keys-{Guid.NewGuid():N}"));
            builder.UseSetting("ArchiveRoot:Path", root);
            builder.UseSetting("VideoLibrary:Path", root);
            builder.UseSetting("ThumbnailCache:Path", root);
            builder.UseSetting("VideoCut:Path", root);
            builder.UseSetting("VideoComposition:Path", root);
        }
    }
}
