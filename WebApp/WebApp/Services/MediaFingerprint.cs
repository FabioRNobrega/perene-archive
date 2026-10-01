using System.Security.Cryptography;

namespace WebApp.Services;

/// <summary>
/// Server-only content fingerprint used to detect moved or renamed files: the file size plus a SHA-256 of a few fixed-offset
/// chunks. It never reads a whole large file and never leaves the server.
/// </summary>
internal static class MediaFingerprint
{
    internal const int ChunkBytes = 64 * 1024;
    private const int ChunkCount = 4;

    /// <summary>Returns null when the file cannot be read.</summary>
    public static string? Compute(string physicalPath)
    {
        try
        {
            using var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            return Compute(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string Compute(Stream stream)
    {
        var length = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(length));
        var buffer = new byte[ChunkBytes];
        foreach (var offset in Offsets(length))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            var read = stream.Read(buffer, 0, (int)Math.Min(ChunkBytes, length - offset));
            hash.AppendData(buffer, 0, read);
        }

        return $"{length:x}-{Convert.ToHexString(hash.GetHashAndReset())[..32].ToLowerInvariant()}";
    }

    private static IEnumerable<long> Offsets(long length)
    {
        if (length <= ChunkBytes) return [0];
        var last = length - ChunkBytes;
        return Enumerable.Range(0, ChunkCount).Select(index => last * index / (ChunkCount - 1)).Distinct();
    }
}
