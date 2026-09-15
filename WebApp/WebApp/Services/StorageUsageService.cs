using System.Globalization;
using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;

namespace WebApp.Services;

public sealed class StorageUsageService(IOptions<ArchiveRootOptions> archiveRootOptions) : IStorageUsageService
{
    private const string DiskStatsPath = "/proc/diskstats";
    private const string MountInfoPath = "/proc/self/mountinfo";
    private const int SectorSizeBytes = 512;

    private readonly string _path = archiveRootOptions.Value.Path;
    private readonly object _gate = new();
    private (DateTime TimestampUtc, BlockDevice Device, long ReadSectors, long WriteSectors)? _previousSample;

    public StorageUsageDto GetUsage()
    {
        try
        {
            var drive = new DriveInfo(_path);
            var totalBytes = drive.TotalSize;
            // TotalFreeSpace includes filesystem-reserved blocks, matching the "Used"
            // calculation reported by df rather than presenting those blocks as used.
            var usedBytes = totalBytes - drive.TotalFreeSpace;
            return new StorageUsageDto(Math.Max(0, usedBytes), Math.Max(0, totalBytes));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new StorageUsageDto(0, 0);
        }
    }

    public (double? ReadBytesPerSecond, double? WriteBytesPerSecond) GetThroughput()
    {
        BlockDevice? device = null;
        (long ReadSectors, long WriteSectors)? totals;
        try
        {
            device = TryGetMountedDevice(_path, File.ReadAllText(MountInfoPath));
            totals = device is null
                ? null
                : ParseDiskStats(File.ReadAllText(DiskStatsPath), device.Value);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            totals = null;
        }

        if (device is null || totals is null)
        {
            return (null, null);
        }

        var resolvedDevice = device.Value;
        var currentTotals = totals.Value;

        var now = DateTime.UtcNow;
        double? readBytesPerSecond = null;
        double? writeBytesPerSecond = null;

        lock (_gate)
        {
            if (_previousSample is not null && _previousSample.Value.Device == resolvedDevice)
            {
                var elapsedSeconds = (now - _previousSample.Value.TimestampUtc).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    var readDelta = Math.Max(0, currentTotals.ReadSectors - _previousSample.Value.ReadSectors);
                    var writeDelta = Math.Max(0, currentTotals.WriteSectors - _previousSample.Value.WriteSectors);
                    readBytesPerSecond = readDelta * SectorSizeBytes / elapsedSeconds;
                    writeBytesPerSecond = writeDelta * SectorSizeBytes / elapsedSeconds;
                }
            }

            _previousSample = (now, resolvedDevice, currentTotals.ReadSectors, currentTotals.WriteSectors);
        }

        return (readBytesPerSecond, writeBytesPerSecond);
    }

    internal static (long ReadSectors, long WriteSectors)? ParseDiskStats(string diskStatsContent, BlockDevice device)
    {
        foreach (var line in diskStatsContent.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10)
            {
                continue;
            }

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ||
                !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor) ||
                major != device.Major || minor != device.Minor)
            {
                continue;
            }

            if (!long.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectorsRead) ||
                !long.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sectorsWritten))
            {
                continue;
            }

            return (sectorsRead, sectorsWritten);
        }

        return null;
    }

    internal static BlockDevice? TryGetMountedDevice(string path, string mountInfoContent)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(normalizedPath))
        {
            normalizedPath = Path.DirectorySeparatorChar.ToString();
        }

        (string MountPoint, BlockDevice Device)? match = null;
        foreach (var line in mountInfoContent.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 5 || !TryParseDevice(fields[2], out var device))
            {
                continue;
            }

            var mountPoint = UnescapeMountPath(fields[4]);
            if (!IsPathWithinMount(normalizedPath, mountPoint) ||
                (match is not null && mountPoint.Length <= match.Value.MountPoint.Length))
            {
                continue;
            }

            match = (mountPoint, device);
        }

        return match?.Device;
    }

    private static bool TryParseDevice(string value, out BlockDevice device)
    {
        var separator = value.IndexOf(':');
        if (separator > 0 &&
            int.TryParse(value[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) &&
            int.TryParse(value[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minor))
        {
            device = new BlockDevice(major, minor);
            return true;
        }

        device = default;
        return false;
    }

    private static bool IsPathWithinMount(string path, string mountPoint) =>
        mountPoint == Path.DirectorySeparatorChar.ToString()
            ? path.StartsWith(Path.DirectorySeparatorChar)
            : path.Equals(mountPoint, StringComparison.Ordinal) ||
              path.StartsWith($"{mountPoint}{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string UnescapeMountPath(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 3 < value.Length &&
                TryParseOctal(value.AsSpan(index + 1, 3), out var character))
            {
                result.Append((char)character);
                index += 3;
                continue;
            }

            result.Append(value[index]);
        }

        return result.ToString();
    }

    private static bool TryParseOctal(ReadOnlySpan<char> value, out int character)
    {
        character = 0;
        foreach (var digit in value)
        {
            if (digit is < '0' or > '7')
            {
                return false;
            }

            character = (character * 8) + digit - '0';
        }

        return true;
    }

    internal readonly record struct BlockDevice(int Major, int Minor);
}
