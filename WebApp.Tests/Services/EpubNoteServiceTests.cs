using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class EpubNoteServiceTests
{
    [Fact]
    public async Task AppendNoteAsync_creates_notes_folder_and_file_when_missing()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        await service.AppendNoteAsync("note-1", "My Book", "Some Author", 0, null, null, "A highlighted passage.", CancellationToken.None);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        Assert.True(File.Exists(notesPath));
    }

    [Fact]
    public async Task AppendNoteAsync_writes_kindle_style_structure_with_separator()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        await service.AppendNoteAsync("note-1", "My Book", "Some Author", 2, null, null, "A highlighted passage.", CancellationToken.None);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var content = await File.ReadAllTextAsync(notesPath);
        var lines = content.Replace("\r\n", "\n").Split('\n');

        Assert.Equal("My Book (Some Author)", lines[0]);
        Assert.StartsWith("- Your Highlight on Chapter 3", lines[1]);
        Assert.Contains("Added on", lines[1]);
        Assert.Equal("- Perene Archive Note ID: note-1", lines[2]);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal("A highlighted passage.", lines[4]);
        Assert.Equal("==========", lines[5]);
    }

    [Fact]
    public async Task AppendNoteAsync_omits_author_parentheses_when_author_is_missing()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        await service.AppendNoteAsync("note-1", "My Book", null, 0, null, null, "Text", CancellationToken.None);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var firstLine = (await File.ReadAllLinesAsync(notesPath))[0];
        Assert.Equal("My Book", firstLine);
    }

    [Fact]
    public async Task AppendNoteAsync_uses_offsets_for_the_generated_location_when_provided()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        await service.AppendNoteAsync("note-1", "My Book", "Author", 1, 120, 180, "Text", CancellationToken.None);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var metadataLine = (await File.ReadAllLinesAsync(notesPath))[1];
        Assert.Contains("Location 120-180", metadataLine);
    }

    [Fact]
    public async Task AppendNoteAsync_appends_subsequent_notes_after_existing_content()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);

        await service.AppendNoteAsync("note-1", "Book A", "Author A", 0, null, null, "First note.", CancellationToken.None);
        await service.AppendNoteAsync("note-2", "Book B", "Author B", 1, null, null, "Second note.", CancellationToken.None);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var content = await File.ReadAllTextAsync(notesPath);

        Assert.Contains("First note.", content);
        Assert.Contains("Second note.", content);
        Assert.Equal(2, content.Split("==========").Length - 1);
        Assert.True(content.IndexOf("First note.", StringComparison.Ordinal) < content.IndexOf("Second note.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_appends_are_serialized_without_interleaving_or_corruption()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);
        const int concurrentWrites = 20;

        var tasks = Enumerable.Range(0, concurrentWrites)
            .Select(index => service.AppendNoteAsync(
                $"note-{index}", $"Book {index}", "Author", index, null, null, $"Selected text number {index}.", CancellationToken.None));
        await Task.WhenAll(tasks);

        var notesPath = Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt");
        var content = await File.ReadAllTextAsync(notesPath);
        var separatorCount = content.Split("==========").Length - 1;

        Assert.Equal(concurrentWrites, separatorCount);
        for (var index = 0; index < concurrentWrites; index++)
        {
            Assert.Contains($"Selected text number {index}.", content);
        }
    }

    [Fact]
    public async Task RemoveNoteAsync_removes_only_the_matching_note()
    {
        using var root = new TemporaryDirectory();
        var service = CreateService(root.Path);
        await service.AppendNoteAsync("note-1", "Book A", "Author", 0, null, null, "First note.", CancellationToken.None);
        await service.AppendNoteAsync("note-2", "Book B", "Author", 1, null, null, "Second note.", CancellationToken.None);

        Assert.True(await service.RemoveNoteAsync("note-1", CancellationToken.None));
        Assert.False(await service.RemoveNoteAsync("missing", CancellationToken.None));

        var content = await File.ReadAllTextAsync(Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt"));
        Assert.DoesNotContain("First note.", content);
        Assert.DoesNotContain("note-1", content);
        Assert.Contains("Second note.", content);
        Assert.Contains("note-2", content);
    }

    private static EpubNoteService CreateService(string archiveRootPath) =>
        new(Options.Create(new ArchiveRootOptions { Path = archiveRootPath }));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-epub-note-service-{Guid.NewGuid():N}");
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
