using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class EpubReaderThemeServiceTests
{
    [Fact]
    public async Task Service_falls_back_to_defaults_and_persists_valid_theme_crud()
    {
        using var root = new TemporaryDirectory();
        var service = new EpubReaderThemeService(Options.Create(new ArchiveRootOptions { Path = root.Path }));
        var defaults = await service.LoadAsync(CancellationToken.None);
        Assert.Equal("Montserrat", defaults.ActiveSettings.FontFamily);

        var settings = new BookReaderThemeSettingsDto("Arial", 24, 2.0, "#112233", "#AABBCC");
        var created = await service.CreateAsync(new CreateBookReaderThemeRequest("Night", settings), CancellationToken.None);
        var id = Assert.Single(created.Themes).Id;
        Assert.Equal(id, created.SelectedThemeId);
        var updated = await service.UpdateAsync(id, new UpdateBookReaderThemeRequest("Evening", settings), CancellationToken.None);
        Assert.Equal("Evening", Assert.Single(updated!.Themes).Name);
        Assert.NotNull(await service.DeleteAsync(id, CancellationToken.None));
        Assert.Empty((await service.LoadAsync(CancellationToken.None)).Themes);
        Assert.Empty(Directory.GetFiles(Path.Combine(root.Path, "Books", "Notes"), "*.tmp"));
    }

    [Fact]
    public async Task Service_rejects_unsupported_values_and_duplicate_names_and_malformed_file()
    {
        using var root = new TemporaryDirectory();
        var notes = Path.Combine(root.Path, "Books", "Notes"); Directory.CreateDirectory(notes);
        await File.WriteAllTextAsync(Path.Combine(notes, "pereneArchiveReaderThemes.json"), "{bad");
        var service = new EpubReaderThemeService(Options.Create(new ArchiveRootOptions { Path = root.Path }));
        Assert.Empty((await service.LoadAsync(CancellationToken.None)).Themes);
        var settings = new BookReaderThemeSettingsDto("Arial", 24, 2.0, null, null);
        await service.CreateAsync(new CreateBookReaderThemeRequest("Theme", settings), CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateBookReaderThemeRequest(" theme ", settings), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveActiveAsync(new BookReaderThemeSettingsDto("Bad", 99, 9, "red", null), CancellationToken.None));
    }

    private sealed class TemporaryDirectory : IDisposable { public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"theme-{Guid.NewGuid():N}"); Directory.CreateDirectory(Path); } public string Path { get; } public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); } }
}
