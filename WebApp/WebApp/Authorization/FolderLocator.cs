using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Authorization;

/// <summary>
/// Maps a server-side physical directory to its <see cref="FolderLocation"/> (root key plus relative path) and back.
/// Archive categories, the cut root, the composition root, and the video-library root are the only roots; the most specific one wins,
/// so a library root that is itself an archive category folder resolves as that category.
/// </summary>
internal sealed class FolderLocator
{
    public const string CutRootKey = "cut-output";
    public const string CompositionRootKey = "composition-output";
    public const string LibraryRootKey = "video-library";

    private readonly IReadOnlyList<RootInfo> _roots;

    public FolderLocator(
        IOptions<ArchiveRootOptions> archive,
        IOptions<VideoCutOptions> cuts,
        IOptions<VideoCompositionOptions> compositions,
        IOptions<VideoLibraryOptions> library)
    {
        var roots = new List<RootInfo>();
        foreach (var category in ArchiveCategory.Defaults)
        {
            roots.Add(new RootInfo(category.Key, category.DisplayName, Path.GetFullPath(Path.Combine(archive.Value.Path, category.FolderName)), true));
        }

        roots.Add(new RootInfo(CutRootKey, "Video cuts", Path.GetFullPath(cuts.Value.Path), false));
        roots.Add(new RootInfo(CompositionRootKey, "Video compositions", Path.GetFullPath(compositions.Value.Path), false));
        roots.Add(new RootInfo(LibraryRootKey, "Video library", Path.GetFullPath(library.Value.Path), false));
        _roots = roots;
    }

    internal sealed record RootInfo(string Key, string Label, string Path, bool IsArchiveCategory);

    public IReadOnlyList<RootInfo> Roots => _roots;

    /// <summary>The location of a directory, or null when it lies outside every known root.</summary>
    public FolderLocation? LocateDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return null;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        RootInfo? best = null;
        foreach (var root in _roots)
        {
            var rootPath = Path.TrimEndingDirectorySeparator(root.Path);
            var within = string.Equals(full, rootPath, comparison) || full.StartsWith(rootPath + Path.DirectorySeparatorChar, comparison);
            if (within && (best is null || rootPath.Length > Path.TrimEndingDirectorySeparator(best.Path).Length)) best = root;
        }

        return best is null
            ? null
            : FolderLocation.Create(best.Key, Path.GetRelativePath(best.Path, full));
    }

    /// <summary>The location of the directory that holds <paramref name="filePath"/>.</summary>
    public FolderLocation? LocateContainer(string filePath) => LocateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));

    public FolderLocation? LocateRoot(string rootKey) => _roots.Any(root => root.Key == rootKey) ? new FolderLocation(rootKey, string.Empty) : null;

    public RootInfo? FindRoot(string rootKey) => _roots.FirstOrDefault(root => root.Key == rootKey);

    /// <summary>The physical directory behind a location. Server-side only.</summary>
    public string? PhysicalPath(FolderLocation location)
    {
        var root = FindRoot(location.RootKey);
        return root is null ? null : location.RelativePath.Length == 0 ? root.Path : Path.Combine(root.Path, location.RelativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
