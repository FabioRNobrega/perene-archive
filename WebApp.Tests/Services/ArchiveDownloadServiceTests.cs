using System.IO.Compression;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveDownloadServiceTests
{
    [Fact]
    public async Task WriteFolderZipAsync_preserves_hierarchy_and_empty_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"archive-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Downloads", "Reports", "2026"));
        Directory.CreateDirectory(Path.Combine(root, "Downloads", "Reports", "Empty"));
        await File.WriteAllTextAsync(Path.Combine(root, "Downloads", "Reports", "2026", "q1.txt"), "quarter one");
        try
        {
            var archive = new ArchiveService(Options.Create(new ArchiveRootOptions { Path = root }));
            var reports = archive.List("downloads", null).Items.Single(item => item.Name == "Reports");
            Assert.True(archive.TryResolveDownloadableItem("downloads", reports.Id, out var folder));
            await using var output = new MemoryStream();

            await new ArchiveDownloadService().WriteFolderZipAsync(folder!, output, CancellationToken.None);

            output.Position = 0;
            using var zip = new ZipArchive(output, ZipArchiveMode.Read);
            Assert.Equal(["Reports/", "Reports/2026/", "Reports/2026/q1.txt", "Reports/Empty/"], zip.Entries.Select(entry => entry.FullName).Order());
            using var reader = new StreamReader(zip.GetEntry("Reports/2026/q1.txt")!.Open());
            Assert.Equal("quarter one", await reader.ReadToEndAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
