using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

internal sealed class ArchiveService(IOptions<ArchiveRootOptions> options) : IArchiveService
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".m4v" };
    private static readonly HashSet<string> ConversionSourceExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".webm", ".mov", ".m4v", ".avi", ".mkv", ".rmvb", ".flv", ".wmv", ".mpeg", ".mpg", ".3gp", ".ts" };

    private static readonly HashSet<string> MusicExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a" };

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".bmp", ".ico" };

    private static readonly HashSet<string> BookExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".epub" };
    private static readonly HashSet<string> ComicExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cbz" };

    private static readonly HashSet<string> TextDocumentExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".markdown", ".txt" };

    private static readonly HashSet<string> PdfDocumentExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private static readonly HashSet<string> SubtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt" };

    private static readonly HashSet<string> CreatableFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

    private static readonly HashSet<string> UploadableExtensions = new(
        VideoExtensions
            .Concat(ConversionSourceExtensions)
            .Concat(MusicExtensions)
            .Concat(ImageExtensions)
            .Concat(BookExtensions)
            .Concat(ComicExtensions)
            .Concat(TextDocumentExtensions)
            .Concat(PdfDocumentExtensions)
            .Concat(SubtitleExtensions),
        StringComparer.OrdinalIgnoreCase);

    private const string BooksCategoryKey = "books";

    private static readonly HashSet<string> AlbumCoverExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };

    private static readonly HashSet<string> ReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

    private readonly string _rootPath = Path.GetFullPath(options.Value.Path);

    public ArchiveListing List(string categoryKey, string? folderId)
    {
        var category = ResolveCategory(categoryKey);
        var folder = ResolveFolder(category, folderId);
        return BuildListing(category, folder);
    }

    public ArchiveListing ListPlaylist(string categoryKey, string? folderId)
    {
        var category = ResolveCategory(categoryKey);
        var folder = ResolveFolder(category, folderId);
        var items = new List<ArchiveItemEntry>();
        CollectPlayableMedia(category, folder, items);
        return new ArchiveListing(
            category,
            folder,
            TryGetParent(category, folder),
            BuildBreadcrumbs(category, folder),
            items);
    }

    public ArchiveListing CreateFolder(string categoryKey, string? parentId, string name)
    {
        var category = ResolveCategory(categoryKey);
        if (!category.CanCreateFolder)
        {
            throw new ArchiveForbiddenException("Folders cannot be created in this category.");
        }

        var parent = ResolveFolder(category, parentId);
        var safeName = ValidateName(name);
        var destination = ContainedPath(category, Path.Combine(parent.PhysicalPath, safeName));
        if (File.Exists(destination) || Directory.Exists(destination))
        {
            throw new ArchiveConflictException("An item with that name already exists.");
        }

        Directory.CreateDirectory(destination);
        return BuildListing(category, parent);
    }

    public ArchiveListing CreateFile(string categoryKey, string? parentId, string name, string extension)
    {
        var category = ResolveCategory(categoryKey);
        if (!category.CanCreateFolder)
        {
            throw new ArchiveForbiddenException("Files cannot be created in this category.");
        }

        if (!CreatableFileExtensions.Contains(extension))
        {
            throw new ArchiveValidationException("Use a supported file type.");
        }

        var parent = ResolveFolder(category, parentId);
        var safeName = ValidateName(name);
        var fileName = $"{safeName}{extension}";
        var destination = ContainedPath(category, Path.Combine(parent.PhysicalPath, fileName));
        if (Exists(destination))
        {
            throw new ArchiveConflictException("An item with that name already exists.");
        }

        File.Create(destination).Dispose();
        return BuildListing(category, parent);
    }

    public ArchiveUploadDestination ValidateUploadDestination(string categoryKey, string? parentId, string fileName)
    {
        var category = ResolveCategory(categoryKey);
        if (!category.CanCreateFolder)
        {
            throw new ArchiveForbiddenException("Files cannot be uploaded to this category.");
        }

        var safeName = ValidateName(fileName);
        var extension = Path.GetExtension(safeName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension) || !UploadableExtensions.Contains(extension))
        {
            throw new ArchiveValidationException("This file type is not supported.");
        }

        var parent = ResolveFolder(category, parentId);
        var destination = ContainedPath(category, Path.Combine(parent.PhysicalPath, safeName));
        if (Exists(destination))
        {
            throw new ArchiveConflictException("An item with that name already exists.");
        }

        return new ArchiveUploadDestination(category, parent, safeName, destination);
    }

    public ArchiveListing PublishUploadedFile(string categoryKey, string? parentId, string fileName, string sourceTempPath)
    {
        var destination = ValidateUploadDestination(categoryKey, parentId, fileName);
        File.Move(sourceTempPath, destination.FinalPath);
        return BuildListing(destination.Category, destination.Parent);
    }

    public ArchiveListing Rename(string categoryKey, string itemId, string name)
    {
        var category = ResolveCategory(categoryKey);
        var item = ResolveItem(category, itemId);
        EnsureNotCategoryRoot(item);
        var safeName = ValidateName(name);
        var destination = ContainedPath(category, Path.Combine(Path.GetDirectoryName(item.PhysicalPath)!, safeName));
        if (Exists(destination))
        {
            throw new ArchiveConflictException("An item with that name already exists.");
        }

        MovePhysical(item, destination);
        return BuildListing(category, GetParentEntry(category, destination));
    }

    public ArchiveListing Move(string categoryKey, string itemId, string destinationCategoryKey, string? destinationFolderId)
    {
        var category = ResolveCategory(categoryKey);
        var item = ResolveItem(category, itemId);
        EnsureNotCategoryRoot(item);
        var destinationCategory = ResolveCategory(destinationCategoryKey);
        var destinationFolder = ResolveFolder(destinationCategory, destinationFolderId);
        var destination = ContainedPath(destinationCategory, Path.Combine(destinationFolder.PhysicalPath, item.Name));
        if (IsSamePath(item.PhysicalPath, destination))
        {
            return BuildListing(category, GetParentEntry(category, item.PhysicalPath));
        }

        if (Exists(destination))
        {
            throw new ArchiveConflictException("An item with that name already exists in the destination folder.");
        }

        if (item.Kind == ArchiveItemKind.Folder && IsWithinOrSame(item.PhysicalPath, destinationFolder.PhysicalPath))
        {
            throw new ArchiveValidationException("A folder cannot be moved into itself.");
        }

        MovePhysical(item, destination);
        return BuildListing(category, GetParentEntry(category, item.PhysicalPath));
    }

    public ArchiveListing MoveToTrash(string categoryKey, string itemId)
    {
        var category = ResolveCategory(categoryKey);
        if (string.Equals(category.Key, "trash", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArchiveForbiddenException("Trash items cannot be deleted permanently.");
        }

        var item = ResolveItem(category, itemId);
        EnsureNotCategoryRoot(item);
        var trash = ResolveCategory("trash");
        var trashRoot = GetCategoryRoot(trash);
        var destination = GetUniqueTrashPath(trashRoot, item.Name);
        MovePhysical(item, destination);
        return BuildListing(category, GetParentEntry(category, item.PhysicalPath));
    }

    public ArchiveListing EmptyTrash(string categoryKey)
    {
        var category = ResolveCategory(categoryKey);
        if (!string.Equals(category.Key, "trash", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArchiveForbiddenException("Only Trash can be permanently emptied.");
        }

        var trashRoot = GetCategoryRoot(category);
        var entries = Directory.EnumerateFileSystemEntries(trashRoot).ToArray();
        try
        {
            foreach (var path in entries)
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ArchiveConflictException("Trash could not be fully emptied. No items were removed.");
        }

        return BuildListing(category, CreateEntry(category, trashRoot));
    }

    public bool TryResolveVideo(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsVideo)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveDownloadableItem(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            item = ResolveItem(ResolveCategory(categoryKey), itemId);
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveConvertibleVideo(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            if (string.Equals(category.Key, "trash", StringComparison.OrdinalIgnoreCase)) return false;
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsConvertibleVideo) return false;
            item = resolved; return true;
        }
        catch (Exception exception) when (exception is ArchiveException or IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException) { return false; }
    }

    public bool TryResolveMusic(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsMusic)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveImage(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsImage)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveBook(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsBook)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveComic(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var resolved = ResolveItem(ResolveCategory(categoryKey), itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsComic) return false;
            item = resolved;
            return true;
        }
        catch (Exception exception) when (exception is ArchiveException or IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveTextDocument(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsTextDocument)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolvePdfDocument(string categoryKey, string itemId, out ArchiveItemEntry? item)
    {
        item = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var resolved = ResolveItem(category, itemId);
            if (resolved.Kind != ArchiveItemKind.File || !resolved.IsPdfDocument)
            {
                return false;
            }

            item = resolved;
            return true;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public bool TryResolveAlbumCover(string categoryKey, string folderId, out ArchiveAlbumCoverInfo? cover)
    {
        cover = null;
        try
        {
            var category = ResolveCategory(categoryKey);
            var folder = ResolveFolder(category, folderId);
            cover = FindAlbumCover(category, folder);
            return cover is not null;
        }
        catch (ArchiveException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    public string GetCategoryRootPath(string categoryKey) => GetCategoryRoot(ResolveCategory(categoryKey));

    public string ComputeItemId(string categoryKey, string physicalPath) =>
        ComputeId(ResolveCategory(categoryKey), Path.GetFullPath(physicalPath));

    internal static bool IsSafeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        if (trimmed is "." or ".." || trimmed.StartsWith('.'))
        {
            return false;
        }

        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(trimmed);
        return !ReservedNames.Contains(nameWithoutExtension);
    }

    private ArchiveListing BuildListing(ArchiveCategory category, ArchiveItemEntry folder)
    {
        if (folder.Kind != ArchiveItemKind.Folder)
        {
            throw new ArchiveValidationException("The requested item is not a folder.");
        }

        var children = new List<ArchiveItemEntry>();
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFileSystemEntries(folder.PhysicalPath).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            throw new ArchiveNotFoundException("The folder could not be read.");
        }

        foreach (var path in paths)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var canonicalPath = ContainedPath(category, path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var info = isDirectory ? null : new FileInfo(canonicalPath);
                var directory = isDirectory ? new DirectoryInfo(canonicalPath) : null;
                var extension = isDirectory ? null : Path.GetExtension(canonicalPath).ToLowerInvariant();
                children.Add(new ArchiveItemEntry(
                    ComputeId(category, canonicalPath),
                    category,
                    canonicalPath,
                    Path.GetFileName(canonicalPath),
                    isDirectory ? ArchiveItemKind.Folder : ArchiveItemKind.File,
                    extension,
                    info?.Length,
                    isDirectory ? directory!.LastWriteTimeUtc : info!.LastWriteTimeUtc,
                    extension is not null && VideoExtensions.Contains(extension),
                    extension is not null && MusicExtensions.Contains(extension),
                    extension is not null && ImageExtensions.Contains(extension),
                    IsBook(category, extension),
                    IsTextDocument(extension),
                    IsPdfDocument(extension),
                    HasPlayableMedia: isDirectory && HasPlayableMediaRecursive(canonicalPath),
                    IsConvertibleVideo: extension is not null && ConversionSourceExtensions.Contains(extension)));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }

        var orderedChildren = children
            .OrderBy(item => item.Kind == ArchiveItemKind.File)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        var albumCover = FindAlbumCover(category, folder);
        if (albumCover is not null)
        {
            orderedChildren = orderedChildren
                .Select(item => item.IsMusic ? item with { AlbumCoverId = albumCover.FolderId } : item)
                .ToList();
        }

        var parent = TryGetParent(category, folder);
        return new ArchiveListing(
            category,
            folder,
            parent,
            BuildBreadcrumbs(category, folder),
            orderedChildren);
    }

    private ArchiveCategory ResolveCategory(string key)
    {
        if (!ArchiveCategory.TryGet(key, out var category) || category is null)
        {
            throw new ArchiveNotFoundException("The category does not exist.");
        }

        return category;
    }

    private ArchiveItemEntry ResolveFolder(ArchiveCategory category, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            var root = GetCategoryRoot(category);
            return CreateEntry(category, root);
        }

        var rootEntry = CreateEntry(category, GetCategoryRoot(category));
        if (string.Equals(rootEntry.Id, folderId, StringComparison.Ordinal))
        {
            return rootEntry;
        }

        var item = ResolveItem(category, folderId);
        if (item.Kind != ArchiveItemKind.Folder)
        {
            throw new ArchiveValidationException("The requested item is not a folder.");
        }

        return item;
    }

    private ArchiveItemEntry ResolveItem(ArchiveCategory category, string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArchiveNotFoundException("The item does not exist.");
        }

        var root = GetCategoryRoot(category);
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var canonicalPath = ContainedPath(category, path);
                if (string.Equals(ComputeId(category, canonicalPath), itemId, StringComparison.Ordinal))
                {
                    return CreateEntry(category, canonicalPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }

        throw new ArchiveNotFoundException("The item does not exist.");
    }

    private ArchiveItemEntry CreateEntry(ArchiveCategory category, string path)
    {
        var canonicalPath = ContainedPath(category, path);
        var attributes = File.GetAttributes(canonicalPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArchiveNotFoundException("The item does not exist.");
        }

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var extension = isDirectory ? null : Path.GetExtension(canonicalPath).ToLowerInvariant();
        var file = isDirectory ? null : new FileInfo(canonicalPath);
        var directory = isDirectory ? new DirectoryInfo(canonicalPath) : null;
        return new ArchiveItemEntry(
            ComputeId(category, canonicalPath),
            category,
            canonicalPath,
            Path.GetFileName(canonicalPath),
            isDirectory ? ArchiveItemKind.Folder : ArchiveItemKind.File,
            extension,
            file?.Length,
            isDirectory ? directory!.LastWriteTimeUtc : file!.LastWriteTimeUtc,
            extension is not null && VideoExtensions.Contains(extension),
            extension is not null && MusicExtensions.Contains(extension),
            extension is not null && ImageExtensions.Contains(extension),
            IsBook(category, extension),
            IsTextDocument(extension),
            IsPdfDocument(extension),
            extension is not null && ComicExtensions.Contains(extension),
            IsConvertibleVideo: extension is not null && ConversionSourceExtensions.Contains(extension));
    }

    private static bool IsBook(ArchiveCategory category, string? extension) =>
        extension is not null &&
        BookExtensions.Contains(extension) &&
        string.Equals(category.Key, BooksCategoryKey, StringComparison.OrdinalIgnoreCase);

    private static bool IsTextDocument(string? extension) =>
        extension is not null && TextDocumentExtensions.Contains(extension);

    private static bool IsPdfDocument(string? extension) =>
        extension is not null && PdfDocumentExtensions.Contains(extension);

    private static bool HasPlayableMediaRecursive(string folderPath)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (VideoExtensions.Contains(extension) || MusicExtensions.Contains(extension))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }

        return false;
    }

    private void CollectPlayableMedia(ArchiveCategory category, ArchiveItemEntry folder, List<ArchiveItemEntry> results)
    {
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFileSystemEntries(folder.PhysicalPath).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return;
        }

        var albumCover = FindAlbumCover(category, folder);

        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var canonicalPath = ContainedPath(category, path);
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (isDirectory)
                {
                    CollectPlayableMedia(category, CreateEntry(category, canonicalPath), results);
                    continue;
                }

                var extension = Path.GetExtension(canonicalPath).ToLowerInvariant();
                var isVideo = VideoExtensions.Contains(extension);
                var isMusic = MusicExtensions.Contains(extension);
                if (!isVideo && !isMusic)
                {
                    continue;
                }

                var info = new FileInfo(canonicalPath);
                results.Add(new ArchiveItemEntry(
                    ComputeId(category, canonicalPath),
                    category,
                    canonicalPath,
                    Path.GetFileName(canonicalPath),
                    ArchiveItemKind.File,
                    extension,
                    info.Length,
                    info.LastWriteTimeUtc,
                    isVideo,
                    isMusic,
                    AlbumCoverId: isMusic ? albumCover?.FolderId : null));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
            {
            }
        }
    }

    private ArchiveAlbumCoverInfo? FindAlbumCover(ArchiveCategory category, ArchiveItemEntry folder)
    {
        if (folder.Kind != ArchiveItemKind.Folder)
        {
            return null;
        }

        try
        {
            return Directory.EnumerateFiles(folder.PhysicalPath)
                .Select(path =>
                {
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            return null;
                        }

                        var canonicalPath = ContainedPath(category, path);
                        var extension = Path.GetExtension(canonicalPath).ToLowerInvariant();
                        if (!AlbumCoverExtensions.Contains(extension))
                        {
                            return null;
                        }

                        return new ArchiveAlbumCoverInfo(
                            folder.Id,
                            canonicalPath,
                            Path.GetFileName(canonicalPath),
                            extension,
                            File.GetLastWriteTimeUtc(canonicalPath));
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
                    {
                        return null;
                    }
                })
                .OfType<ArchiveAlbumCoverInfo>()
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PhysicalPath, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private ArchiveItemEntry GetParentEntry(ArchiveCategory category, string path)
    {
        var parentPath = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parentPath) || !IsWithinOrSame(GetCategoryRoot(category), parentPath))
        {
            return CreateEntry(category, GetCategoryRoot(category));
        }

        return CreateEntry(category, parentPath);
    }

    private ArchiveItemEntry? TryGetParent(ArchiveCategory category, ArchiveItemEntry folder)
    {
        var root = GetCategoryRoot(category);
        if (IsSamePath(root, folder.PhysicalPath))
        {
            return null;
        }

        return GetParentEntry(category, folder.PhysicalPath);
    }

    private IReadOnlyList<ArchiveBreadcrumbDto> BuildBreadcrumbs(ArchiveCategory category, ArchiveItemEntry folder)
    {
        var root = GetCategoryRoot(category);
        var relative = Path.GetRelativePath(root, folder.PhysicalPath);
        if (relative == ".")
        {
            return [];
        }

        var breadcrumbs = new List<ArchiveBreadcrumbDto>();
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToList())
        {
            current = Path.Combine(current, segment);
            breadcrumbs.Add(new ArchiveBreadcrumbDto(ComputeId(category, current), segment));
        }

        return breadcrumbs;
    }

    private string GetCategoryRoot(ArchiveCategory category)
    {
        var path = Path.GetFullPath(Path.Combine(_rootPath, category.FolderName));
        Directory.CreateDirectory(path);
        return path;
    }

    private string ContainedPath(ArchiveCategory category, string path)
    {
        var root = GetCategoryRoot(category);
        var canonicalPath = Path.GetFullPath(path);
        if (!IsWithinOrSame(root, canonicalPath))
        {
            throw new ArchiveValidationException("The item is outside the selected category.");
        }

        return canonicalPath;
    }

    private static string ValidateName(string name)
    {
        var trimmed = name.Trim();
        if (!IsSafeName(trimmed))
        {
            throw new ArchiveValidationException("Use a valid folder or file name.");
        }

        return trimmed;
    }

    private static void EnsureNotCategoryRoot(ArchiveItemEntry item)
    {
        if (string.IsNullOrWhiteSpace(Path.GetDirectoryName(item.PhysicalPath)))
        {
            throw new ArchiveForbiddenException("The category root cannot be changed.");
        }
    }

    private string ComputeId(ArchiveCategory category, string path)
    {
        var relative = Path.GetRelativePath(GetCategoryRoot(category), path).Replace('\\', '/');
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{category.Key}:{relative}")))[..32].ToLowerInvariant();
    }

    private string GetUniqueTrashPath(string trashRoot, string name)
    {
        var candidate = Path.Combine(trashRoot, name);
        if (!Exists(candidate))
        {
            return candidate;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 1; index < 10000; index++)
        {
            candidate = Path.Combine(trashRoot, $"{baseName} {DateTime.UtcNow:yyyyMMddHHmmss} {index:0000}{extension}");
            if (!Exists(candidate))
            {
                return candidate;
            }
        }

        throw new ArchiveConflictException("A unique Trash name could not be created.");
    }

    private static void MovePhysical(ArchiveItemEntry item, string destination)
    {
        if (item.Kind == ArchiveItemKind.Folder)
        {
            Directory.Move(item.PhysicalPath, destination);
        }
        else
        {
            File.Move(item.PhysicalPath, destination);
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsWithinOrSame(string rootPath, string candidatePath)
    {
        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var canonicalCandidate = Path.GetFullPath(candidatePath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(canonicalRoot, canonicalCandidate, comparison)
            || canonicalCandidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsSamePath(string first, string second)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), comparison);
    }

}

internal class ArchiveException(string message) : Exception(message);

internal sealed class ArchiveValidationException(string message) : ArchiveException(message);

internal sealed class ArchiveNotFoundException(string message) : ArchiveException(message);

internal sealed class ArchiveForbiddenException(string message) : ArchiveException(message);

internal sealed class ArchiveConflictException(string message) : ArchiveException(message);
