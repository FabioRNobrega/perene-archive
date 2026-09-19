using System.IO.Compression;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveDownloadService : IArchiveDownloadService
{
    public async Task WriteFolderZipAsync(ArchiveItemEntry folder, Stream destination, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        var parentPath = Path.GetDirectoryName(folder.PhysicalPath)!;
        await AddDirectoryAsync(archive, folder.PhysicalPath, parentPath, cancellationToken);
    }

    private static async Task AddDirectoryAsync(ZipArchive archive, string directoryPath, string selectedParentPath, CancellationToken cancellationToken)
    {
        if (!TryGetAttributes(directoryPath, out var attributes) ||
            (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory ||
            !IsContained(selectedParentPath, directoryPath))
        {
            return;
        }

        archive.CreateEntry(GetEntryName(selectedParentPath, directoryPath, isDirectory: true));

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directoryPath).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        foreach (var child in children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetAttributes(child, out var childAttributes) ||
                (childAttributes & FileAttributes.ReparsePoint) != 0 ||
                !IsContained(selectedParentPath, child))
            {
                continue;
            }

            if ((childAttributes & FileAttributes.Directory) != 0)
            {
                await AddDirectoryAsync(archive, child, selectedParentPath, cancellationToken);
            }
            else
            {
                await AddFileAsync(archive, child, selectedParentPath, cancellationToken);
            }
        }
    }

    private static async Task AddFileAsync(ZipArchive archive, string filePath, string selectedParentPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var entry = archive.CreateEntry(GetEntryName(selectedParentPath, filePath, isDirectory: false), CompressionLevel.Optimal);
            await using var output = entry.Open();
            await input.CopyToAsync(output, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            // A descendant can disappear or become unreadable while the ZIP is streamed.
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try { attributes = File.GetAttributes(path); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        { attributes = default; return false; }
    }

    private static bool IsContained(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(candidate));
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static string GetEntryName(string parent, string path, bool isDirectory) =>
        Path.GetRelativePath(parent, path).Replace(Path.DirectorySeparatorChar, '/') + (isDirectory ? "/" : string.Empty);
}
