using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class StorageUsageServiceTests
{
    [Fact]
    public void Existing_path_returns_non_negative_usage_with_used_not_exceeding_total()
    {
        using var directory = new TemporaryDirectory();
        var service = new StorageUsageService(Options.Create(new ArchiveRootOptions { Path = directory.Path }));

        var usage = service.GetUsage();

        Assert.True(usage.UsedBytes >= 0);
        Assert.True(usage.TotalBytes >= 0);
        Assert.True(usage.UsedBytes <= usage.TotalBytes);
    }

    [Fact]
    public void Invalid_path_returns_safe_fallback_instead_of_throwing()
    {
        var service = new StorageUsageService(Options.Create(new ArchiveRootOptions { Path = string.Empty }));

        var usage = service.GetUsage();

        Assert.Equal(0, usage.UsedBytes);
        Assert.Equal(0, usage.TotalBytes);
    }

    [Fact]
    public void ParseDiskStats_returns_sectors_for_only_the_requested_device()
    {
        const string diskStats =
            "   8       0 sda 100 0 2000 0 50 0 1000 0 0 0 0\n" +
            "   8       1 sda1 75 0 3000 0 25 0 1500 0 0 0 0\n" +
            "   7       0 loop0 999 0 999999 0 999 0 999999 0 0 0 0\n" +
            "  253       0 ram0 999 0 999999 0 999 0 999999 0 0 0 0\n";

        var totals = StorageUsageService.ParseDiskStats(diskStats, new StorageUsageService.BlockDevice(8, 1));

        Assert.NotNull(totals);
        Assert.Equal(3000, totals!.Value.ReadSectors);
        Assert.Equal(1500, totals.Value.WriteSectors);
    }

    [Fact]
    public void ParseDiskStats_returns_null_when_the_mounted_device_is_absent()
    {
        Assert.Null(StorageUsageService.ParseDiskStats("   8 0 sda 1 0 2 0 3 0 4 0", new StorageUsageService.BlockDevice(8, 1)));
    }

    [Fact]
    public void TryGetMountedDevice_selects_the_deepest_mount_containing_the_archive_path()
    {
        const string mountInfo =
            "24 1 8:1 / / rw,relatime - ext4 /dev/sda1 rw\n" +
            "35 24 8:17 / /archive rw,relatime - ext4 /dev/sdb1 rw\n";

        var device = StorageUsageService.TryGetMountedDevice("/archive/Videos", mountInfo);

        Assert.Equal(new StorageUsageService.BlockDevice(8, 17), device);
    }

    [Fact]
    public void TryGetMountedDevice_returns_null_for_invalid_mountinfo()
    {
        Assert.Null(StorageUsageService.TryGetMountedDevice("/archive", "invalid mount data"));
    }

    [Fact]
    public void GetThroughput_returns_non_negative_values_or_null_when_unavailable()
    {
        using var directory = new TemporaryDirectory();
        var service = new StorageUsageService(Options.Create(new ArchiveRootOptions { Path = directory.Path }));

        var (readBytesPerSecond, writeBytesPerSecond) = service.GetThroughput();

        Assert.True(readBytesPerSecond is null or >= 0);
        Assert.True(writeBytesPerSecond is null or >= 0);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-storage-usage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
