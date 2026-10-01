using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace WebApp.Services;

/// <summary>
/// Computes the category-specific identity of the work (not the bytes) from container metadata only, without reading page or
/// chapter content: EPUB from the OPF identifier, title and first creator; CBZ from page count plus the first and last image
/// entry names and CRCs. Returns null when it cannot be computed.
/// </summary>
internal static class MediaIdentityKeys
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    public static string? ForEpub(string physicalPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(physicalPath);
            var container = archive.GetEntry("META-INF/container.xml");
            if (container is null) return null;
            using var containerStream = container.Open();
            var rootFile = XDocument.Load(containerStream).Descendants().FirstOrDefault(element => element.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
            if (string.IsNullOrWhiteSpace(rootFile)) return null;
            var opf = archive.GetEntry(rootFile);
            if (opf is null) return null;
            using var opfStream = opf.Open();
            return ForEpubMetadata(XDocument.Load(opfStream));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Xml.XmlException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string? ForEpubMetadata(XDocument opf)
    {
        string? First(string name) => opf.Descendants().FirstOrDefault(element => element.Name.LocalName == name && element.Name.NamespaceName.Contains("purl.org/dc", StringComparison.Ordinal))?.Value;
        var identifier = Normalize(First("identifier"));
        var title = Normalize(First("title"));
        var creator = Normalize(First("creator"));
        if (identifier.Length == 0 && title.Length == 0) return null;
        return Hash($"epub|{identifier}|{title}|{creator}");
    }

    public static string? ForCbz(string physicalPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(physicalPath);
            var pages = archive.Entries
                .Where(entry => entry.Length > 0 && ImageExtensions.Contains(Path.GetExtension(entry.FullName)))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pages.Count == 0) return null;
            return ForCbzPages(pages.Count, (pages[0].FullName, pages[0].Crc32), (pages[^1].FullName, pages[^1].Crc32));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string ForCbzPages(int pageCount, (string Name, uint Crc) first, (string Name, uint Crc) last) =>
        Hash($"cbz|{pageCount}|{Normalize(first.Name)}|{first.Crc:x8}|{Normalize(last.Name)}|{last.Crc:x8}");

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();
}
