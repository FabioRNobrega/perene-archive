using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class EpubBookServiceTests
{
    [Fact]
    public void TryGetBook_reads_title_author_cover_and_navigation_from_a_minimal_epub()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"), "Test Book", "Test Author");
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        var found = service.TryGetBook(item, out var book);

        Assert.True(found);
        Assert.NotNull(book);
        Assert.Equal("Test Book", book!.Title);
        Assert.Equal("Test Author", book.Author);
        Assert.True(book.HasCover);
        Assert.Equal(["0", "1"], book.ChapterIds);
        Assert.Equal(20, book.TotalWordCount);
        Assert.Collection(
            book.Chapters,
            chapter =>
            {
                Assert.Equal("0", chapter.ChapterId);
                Assert.Equal(0, chapter.ChapterIndex);
                Assert.Equal(10, chapter.WordCount);
            },
            chapter =>
            {
                Assert.Equal("1", chapter.ChapterId);
                Assert.Equal(1, chapter.ChapterIndex);
                Assert.Equal(10, chapter.WordCount);
            });
        Assert.Equal(2, book.Navigation.Count);
        Assert.Equal("Chapter One", book.Navigation[0].Title);
        Assert.Equal("0", book.Navigation[0].ChapterId);
        Assert.Equal("Chapter Two", book.Navigation[1].Title);
        Assert.Equal("1", book.Navigation[1].ChapterId);
    }

    [Fact]
    public void TryGetBook_reports_no_first_content_chapter_when_all_chapters_are_below_the_word_count_floor()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"));
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.True(service.TryGetBook(item, out var book));
        Assert.Null(book!.FirstContentChapterId);
    }

    [Fact]
    public void TryGetBook_skips_blank_front_matter_using_navigation_to_find_the_first_content_chapter()
    {
        using var directory = new TemporaryDirectory();
        var realChapterParagraph = string.Join(' ', Enumerable.Repeat("word", 40));
        var path = EpubTestFixture.CreateEpub(
            Path.Combine(directory.Path, "book.epub"),
            [
                new EpubTestFixture.SpineEntry("cover.xhtml", "Cover", "Test Book"),
                new EpubTestFixture.SpineEntry("titlepage.xhtml", "Title Page", "Test Book by Test Author"),
                new EpubTestFixture.SpineEntry("chapter1.xhtml", "Chapter One", realChapterParagraph, "Chapter One"),
                new EpubTestFixture.SpineEntry("chapter2.xhtml", "Chapter Two", realChapterParagraph, "Chapter Two")
            ]);
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.True(service.TryGetBook(item, out var book));
        Assert.Equal("0", book!.ChapterIds.First());
        Assert.Equal("2", book.FirstContentChapterId);
    }

    [Fact]
    public void TryGetBook_falls_back_to_word_count_for_first_content_chapter_when_navigation_has_no_entries()
    {
        using var directory = new TemporaryDirectory();
        var realChapterParagraph = string.Join(' ', Enumerable.Repeat("word", 40));
        var path = EpubTestFixture.CreateEpub(
            Path.Combine(directory.Path, "book.epub"),
            [
                new EpubTestFixture.SpineEntry("cover.xhtml", "Cover", "Test Book"),
                new EpubTestFixture.SpineEntry("titlepage.xhtml", "Title Page", "Test Book by Test Author"),
                new EpubTestFixture.SpineEntry("chapter1.xhtml", "Chapter One", realChapterParagraph),
                new EpubTestFixture.SpineEntry("chapter2.xhtml", "Chapter Two", realChapterParagraph)
            ]);
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.True(service.TryGetBook(item, out var book));
        Assert.Empty(book!.Navigation);
        Assert.Equal("2", book.FirstContentChapterId);
    }

    [Fact]
    public void TryGetBook_falls_back_to_file_name_when_title_is_missing()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"), title: " ", author: " ");
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        var found = service.TryGetBook(item, out var book);

        Assert.True(found);
        Assert.Equal("book.epub", book!.Title);
        Assert.Null(book.Author);
    }

    [Fact]
    public void TryGetBook_reports_no_cover_when_epub_has_none()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"), includeCover: false);
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.True(service.TryGetBook(item, out var book));
        Assert.False(book!.HasCover);
        Assert.False(service.TryGetCover(item, out _, out _));
    }

    [Fact]
    public void TryGetChapter_returns_sanitized_content_with_previous_and_next_links()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"));
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        var foundFirst = service.TryGetChapter(item, "0", out var first);
        var foundSecond = service.TryGetChapter(item, "1", out var second);

        Assert.True(foundFirst);
        Assert.NotNull(first);
        Assert.Equal(0, first!.ChapterIndex);
        Assert.Equal(10, first.WordCount);
        Assert.Null(first.PreviousChapterId);
        Assert.Equal("1", first.NextChapterId);
        Assert.Contains("This is the first chapter", first.ContentHtml);
        Assert.DoesNotContain("<script", first.ContentHtml, StringComparison.OrdinalIgnoreCase);

        Assert.True(foundSecond);
        Assert.Equal("0", second!.PreviousChapterId);
        Assert.Null(second.NextChapterId);
    }

    [Fact]
    public void TryGetChapter_returns_false_for_unknown_or_out_of_range_chapter_ids()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"));
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.False(service.TryGetChapter(item, "99", out _));
        Assert.False(service.TryGetChapter(item, "not-a-number", out _));
        Assert.False(service.TryGetChapter(item, "-1", out _));
    }

    [Fact]
    public void TryGetCover_returns_bytes_and_content_type_when_cover_is_present()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMinimalEpub(Path.Combine(directory.Path, "book.epub"));
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        var found = service.TryGetCover(item, out var bytes, out var contentType);

        Assert.True(found);
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes!);
        Assert.Equal("image/png", contentType);
    }

    [Fact]
    public void Malformed_epub_files_fail_gracefully_without_throwing()
    {
        using var directory = new TemporaryDirectory();
        var path = EpubTestFixture.CreateMalformedEpub(Path.Combine(directory.Path, "broken.epub"));
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(path);

        Assert.False(service.TryGetBook(item, out var book));
        Assert.Null(book);
        Assert.False(service.TryGetChapter(item, "0", out _));
        Assert.False(service.TryGetCover(item, out _, out _));
    }

    [Fact]
    public void Missing_epub_file_fails_gracefully_without_throwing()
    {
        using var directory = new TemporaryDirectory();
        var missingPath = Path.Combine(directory.Path, "missing.epub");
        var service = new EpubBookService(new EpubContentSanitizer());
        var item = CreateBookEntry(missingPath);

        Assert.False(service.TryGetBook(item, out _));
    }

    private static ArchiveItemEntry CreateBookEntry(string physicalPath)
    {
        Assert.True(ArchiveCategory.TryGet("books", out var category));
        return new ArchiveItemEntry(
            "test-book-id",
            category!,
            physicalPath,
            Path.GetFileName(physicalPath),
            ArchiveItemKind.File,
            ".epub",
            File.Exists(physicalPath) ? new FileInfo(physicalPath).Length : null,
            File.Exists(physicalPath) ? File.GetLastWriteTimeUtc(physicalPath) : DateTime.UtcNow,
            IsVideo: false,
            IsBook: true);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-epub-book-service-{Guid.NewGuid():N}");
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
