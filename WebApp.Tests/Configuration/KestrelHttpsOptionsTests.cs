using WebApp.Configuration;

namespace WebApp.Tests.Configuration;

public sealed class KestrelHttpsOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsEnabled_is_false_when_certificate_path_is_unset(string? path)
    {
        var options = new KestrelHttpsOptions { Path = path ?? string.Empty };

        Assert.False(KestrelHttpsOptions.IsEnabled(options));
    }

    [Fact]
    public void IsEnabled_is_false_when_certificate_path_does_not_exist_on_disk()
    {
        var options = new KestrelHttpsOptions
        {
            Path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pfx")
        };

        Assert.False(KestrelHttpsOptions.IsEnabled(options));
    }

    [Fact]
    public void IsEnabled_is_true_when_certificate_path_points_at_a_real_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cert-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, [1, 2, 3]);
        try
        {
            var options = new KestrelHttpsOptions { Path = path };

            Assert.True(KestrelHttpsOptions.IsEnabled(options));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HasPositivePort_rejects_non_positive_ports(int port)
    {
        var options = new KestrelHttpsOptions { Port = port };

        Assert.False(KestrelHttpsOptions.HasPositivePort(options));
    }

    [Fact]
    public void HasPositivePort_accepts_a_positive_port()
    {
        var options = new KestrelHttpsOptions { Port = 8443 };

        Assert.True(KestrelHttpsOptions.HasPositivePort(options));
    }

    [Fact]
    public void HasPasswordWhenPathConfigured_rejects_a_configured_path_with_an_empty_password()
    {
        var options = new KestrelHttpsOptions { Path = "/https/perene.pfx", Password = "" };

        Assert.False(KestrelHttpsOptions.HasPasswordWhenPathConfigured(options));
    }

    [Fact]
    public void HasPasswordWhenPathConfigured_allows_an_unconfigured_path_with_an_empty_password()
    {
        var options = new KestrelHttpsOptions { Path = "", Password = "" };

        Assert.True(KestrelHttpsOptions.HasPasswordWhenPathConfigured(options));
    }

    [Fact]
    public void HasPasswordWhenPathConfigured_accepts_a_configured_path_with_a_password()
    {
        var options = new KestrelHttpsOptions { Path = "/https/perene.pfx", Password = "secret" };

        Assert.True(KestrelHttpsOptions.HasPasswordWhenPathConfigured(options));
    }
}
