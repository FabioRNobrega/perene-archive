using System.Xml.Linq;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class MediaIdentityClassifierTests
{
    private static readonly DateTime T1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = T1.AddHours(1);

    [Theory]
    [InlineData("book", "a", "a", SamePathChange.ConfirmedUpdate)]
    [InlineData("comic", "a", "a", SamePathChange.ConfirmedUpdate)]
    [InlineData("book", "a", "b", SamePathChange.Replacement)]
    [InlineData("comic", "a", "b", SamePathChange.Replacement)]
    [InlineData("book", null, "a", SamePathChange.Uncertain)]
    [InlineData("book", "a", null, SamePathChange.Uncertain)]
    [InlineData("comic", null, null, SamePathChange.Uncertain)]
    [InlineData("other", null, null, SamePathChange.SameItem)]
    [InlineData("other", "a", "b", SamePathChange.SameItem)]
    public void A_changed_file_at_the_same_path_is_classified_by_identity(string category, string? oldKey, string? newKey, SamePathChange expected)
    {
        Assert.Equal(expected, MediaIdentityClassifier.Classify(category, oldKey, newKey, 10, T1, 20, T2));
    }

    [Theory]
    [InlineData("book")]
    [InlineData("comic")]
    [InlineData("other")]
    public void An_unchanged_file_is_never_reclassified(string category)
    {
        Assert.Equal(SamePathChange.Unchanged, MediaIdentityClassifier.Classify(category, "a", "b", 10, T1, 10, T1));
    }

    [Theory]
    [InlineData(MediaStatus.Active, MediaStatus.Active, true)]
    [InlineData(MediaStatus.Active, MediaStatus.Superseded, true)]
    [InlineData(MediaStatus.Active, MediaStatus.NeedsReview, true)]
    [InlineData(MediaStatus.Active, MediaStatus.Missing, true)]
    [InlineData(MediaStatus.Missing, MediaStatus.Active, true)]
    [InlineData(MediaStatus.Missing, MediaStatus.NeedsReview, true)]
    [InlineData(MediaStatus.Missing, MediaStatus.Superseded, true)]
    [InlineData(MediaStatus.NeedsReview, MediaStatus.Superseded, true)]
    [InlineData(MediaStatus.NeedsReview, MediaStatus.Active, false)]
    [InlineData(MediaStatus.Superseded, MediaStatus.Active, false)]
    [InlineData(MediaStatus.Superseded, MediaStatus.NeedsReview, false)]
    [InlineData(MediaStatus.Superseded, MediaStatus.Missing, false)]
    [InlineData(MediaStatus.NeedsReview, MediaStatus.Missing, false)]
    public void Only_the_approved_transitions_are_allowed(MediaStatus from, MediaStatus to, bool allowed)
    {
        Assert.Equal(allowed, MediaIdentityClassifier.IsAllowedTransition(from, to));
    }

    [Fact]
    public void Only_books_and_comics_are_annotatable()
    {
        Assert.True(MediaIdentityClassifier.IsAnnotatable("book"));
        Assert.True(MediaIdentityClassifier.IsAnnotatable("comic"));
        Assert.False(MediaIdentityClassifier.IsAnnotatable("other"));
    }

    [Fact]
    public void Epub_identity_ignores_case_and_spacing_but_not_the_work()
    {
        XDocument Opf(string id, string title, string creator) => XDocument.Parse(
            $"<package xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><metadata><dc:identifier>{id}</dc:identifier><dc:title>{title}</dc:title><dc:creator>{creator}</dc:creator></metadata></package>");

        var original = MediaIdentityKeys.ForEpubMetadata(Opf("urn:uuid:1", "My  Book", "Ann Author"));
        Assert.Equal(original, MediaIdentityKeys.ForEpubMetadata(Opf("urn:uuid:1", "my book", "ann author")));
        Assert.NotEqual(original, MediaIdentityKeys.ForEpubMetadata(Opf("urn:uuid:1", "Another Book", "Ann Author")));
        Assert.NotEqual(original, MediaIdentityKeys.ForEpubMetadata(Opf("urn:uuid:2", "My Book", "Ann Author")));
        Assert.Null(MediaIdentityKeys.ForEpubMetadata(XDocument.Parse("<package xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><metadata /></package>")));
    }

    [Fact]
    public void Cbz_identity_depends_on_page_count_and_first_and_last_entries()
    {
        var baseKey = MediaIdentityKeys.ForCbzPages(10, ("a.jpg", 1), ("j.jpg", 2));
        Assert.Equal(baseKey, MediaIdentityKeys.ForCbzPages(10, ("A.JPG", 1), ("j.jpg", 2)));
        Assert.NotEqual(baseKey, MediaIdentityKeys.ForCbzPages(11, ("a.jpg", 1), ("j.jpg", 2)));
        Assert.NotEqual(baseKey, MediaIdentityKeys.ForCbzPages(10, ("a.jpg", 9), ("j.jpg", 2)));
        Assert.NotEqual(baseKey, MediaIdentityKeys.ForCbzPages(10, ("a.jpg", 1), ("k.jpg", 2)));
    }

    [Fact]
    public void Identity_keys_are_null_for_files_that_are_not_valid_containers()
    {
        using var directory = new TemporaryDirectory();
        var bogus = Path.Combine(directory.Path, "bogus.epub");
        File.WriteAllText(bogus, "not a zip");
        Assert.Null(MediaIdentityKeys.ForEpub(bogus));
        Assert.Null(MediaIdentityKeys.ForCbz(bogus));
        Assert.Null(MediaIdentityKeys.ForEpub(Path.Combine(directory.Path, "missing.epub")));
    }

    [Fact]
    public void Fingerprints_are_stable_and_sensitive_to_content_and_size()
    {
        using var directory = new TemporaryDirectory();
        var a = Path.Combine(directory.Path, "a.bin");
        var b = Path.Combine(directory.Path, "b.bin");
        var c = Path.Combine(directory.Path, "c.bin");
        var bytes = new byte[300_000];
        new Random(1).NextBytes(bytes);
        File.WriteAllBytes(a, bytes);
        File.Copy(a, b);
        bytes[^10] ^= 0xFF;
        File.WriteAllBytes(c, bytes);

        Assert.Equal(MediaFingerprint.Compute(a), MediaFingerprint.Compute(b));
        Assert.NotEqual(MediaFingerprint.Compute(a), MediaFingerprint.Compute(c));
        Assert.Null(MediaFingerprint.Compute(Path.Combine(directory.Path, "missing.bin")));
        File.WriteAllBytes(b, bytes[..1000]);
        Assert.NotEqual(MediaFingerprint.Compute(a), MediaFingerprint.Compute(b));
    }
}
