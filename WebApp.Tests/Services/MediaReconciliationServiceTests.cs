using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Tests.Services;

/// <summary>Identity classification and scan reconciliation against real files and a real SQLite database.</summary>
public sealed class MediaReconciliationServiceTests : IDisposable
{
    private readonly MediaTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string BookPath(string name) => Path.Combine(_host.BooksPath, name);

    private async Task<long> TouchAsync(string userId, string name)
    {
        long mediaId = 0;
        await _host.AsAsync(userId, async sp =>
        {
            var id = _host.ItemId(sp, "books", name);
            mediaId = (await sp.GetRequiredService<IUserMediaContext>().RequireAsync("books", id, CancellationToken.None)).Item.Id;
        });
        return mediaId;
    }

    private async Task<MediaReconcileReport> ReconcileAsync() =>
        await _host.WithDbAsync((sp, _) => sp.GetRequiredService<MediaReconciliationService>().ReconcileAsync());

    private Task<List<MediaItem>> ItemsAsync() => _host.WithDbAsync((_, db) => db.MediaItems.AsNoTracking().OrderBy(item => item.Id).ToListAsync());

    private async Task AddProgressAsync(string userId, string name, int offset)
    {
        await _host.AsAsync(userId, async sp =>
        {
            var id = _host.ItemId(sp, "books", name);
            await sp.GetRequiredService<IEpubProgressService>().SaveProgressAsync("books", id, new BookProgressDto("1", offset), CancellationToken.None);
        });
    }

    [Fact]
    public async Task The_same_file_resolves_to_the_same_identity_every_time()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");

        var first = await TouchAsync(alice, "novel.epub");
        var second = await TouchAsync(alice, "novel.epub");

        Assert.Equal(first, second);
        var item = Assert.Single(await ItemsAsync());
        Assert.Equal("book", item.Category);
        Assert.Equal(MediaStatus.Active, item.Status);
        Assert.NotNull(item.IdentityKey);
        Assert.NotNull(item.ContentFingerprint);
        Assert.Equal(1, item.ContentRevision);
    }

    [Fact]
    public async Task Different_users_share_one_media_identity()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");

        Assert.Equal(await TouchAsync(alice, "novel.epub"), await TouchAsync(bob, "novel.epub"));
        Assert.Single(await ItemsAsync());
    }

    [Fact]
    public async Task A_renamed_file_relinks_by_fingerprint_and_keeps_its_user_data()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("old name.epub"));
        var alice = await _host.AddUserAsync("alice");
        var originalId = await TouchAsync(alice, "old name.epub");
        await AddProgressAsync(alice, "old name.epub", 321);

        Directory.CreateDirectory(Path.Combine(_host.BooksPath, "Shelf"));
        File.Move(BookPath("old name.epub"), Path.Combine(_host.BooksPath, "Shelf", "new name.epub"));
        var report = await ReconcileAsync();

        Assert.Equal(1, report.Relinked);
        var item = Assert.Single(await ItemsAsync());
        Assert.Equal(originalId, item.Id);
        Assert.Equal("Shelf/new name.epub", item.RelativePath);
        Assert.Equal(MediaStatus.Active, item.Status);

        await _host.AsAsync(alice, async sp =>
        {
            var folder = sp.GetRequiredService<IArchiveService>().List("books", null).Items.Single(entry => entry.Name == "Shelf").Id;
            var id = sp.GetRequiredService<IArchiveService>().List("books", folder).Items.Single(entry => entry.Name == "new name.epub").Id;
            var progress = await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None);
            Assert.Equal(321, progress!.WordOffset);
        });
    }

    [Fact]
    public async Task A_vanished_file_goes_missing_with_its_data_kept_and_returns_when_it_reappears()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");
        var originalId = await TouchAsync(alice, "novel.epub");
        await AddProgressAsync(alice, "novel.epub", 55);
        var bytes = await File.ReadAllBytesAsync(BookPath("novel.epub"));
        File.Delete(BookPath("novel.epub"));

        var gone = await ReconcileAsync();

        Assert.Equal(1, gone.MarkedMissing);
        var missing = Assert.Single(await ItemsAsync());
        Assert.Equal(MediaStatus.Missing, missing.Status);
        Assert.NotNull(missing.MissingSince);
        await _host.WithDbAsync(async (_, db) => Assert.Equal(55, (await db.ReadingProgresses.SingleAsync()).WordOffset));

        await File.WriteAllBytesAsync(BookPath("novel.epub"), bytes);
        Assert.Equal(originalId, await TouchAsync(alice, "novel.epub"));
        var back = Assert.Single(await ItemsAsync());
        Assert.Equal(MediaStatus.Active, back.Status);
        Assert.Null(back.MissingSince);
    }

    [Fact]
    public async Task Changed_bytes_of_the_same_work_are_a_confirmed_update()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");
        var id = await TouchAsync(alice, "novel.epub");

        EpubTestFixture.CreateEpub(BookPath("novel.epub"), [new EpubTestFixture.SpineEntry("chapter1.xhtml", "One", "Different words entirely, but the very same book.")]);
        Assert.Equal(id, await TouchAsync(alice, "novel.epub"));

        var item = Assert.Single(await ItemsAsync());
        Assert.Equal(2, item.ContentRevision);
        Assert.Equal(MediaStatus.Active, item.Status);
    }

    [Fact]
    public async Task A_different_work_at_the_same_path_supersedes_the_old_item_and_hides_its_data()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"), "First Book", "Ann");
        var alice = await _host.AddUserAsync("alice");
        var oldId = await TouchAsync(alice, "novel.epub");
        await AddProgressAsync(alice, "novel.epub", 77);

        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"), "Completely Other Book", "Zed");
        File.SetLastWriteTimeUtc(BookPath("novel.epub"), DateTime.UtcNow.AddMinutes(5));
        var newId = await TouchAsync(alice, "novel.epub");

        Assert.NotEqual(oldId, newId);
        var items = await ItemsAsync();
        var old = items.Single(item => item.Id == oldId);
        Assert.Equal(MediaStatus.Superseded, old.Status);
        Assert.Equal(newId, old.SupersededByMediaItemId);
        Assert.Equal(MediaStatus.Active, items.Single(item => item.Id == newId).Status);

        // The old book's progress is retained but never shown against the new content.
        await _host.AsAsync(alice, async sp =>
        {
            var id = _host.ItemId(sp, "books", "novel.epub");
            Assert.Null(await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None));
        });
        await _host.WithDbAsync(async (_, db) => Assert.Equal(oldId, (await db.ReadingProgresses.SingleAsync()).MediaItemId));
    }

    [Fact]
    public async Task A_replacement_whose_identity_cannot_be_established_goes_to_review()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");
        var oldId = await TouchAsync(alice, "novel.epub");

        await File.WriteAllTextAsync(BookPath("novel.epub"), "this is no longer a valid epub");
        var newId = await TouchAsync(alice, "novel.epub");

        Assert.NotEqual(oldId, newId);
        var items = await ItemsAsync();
        Assert.Equal(MediaStatus.NeedsReview, items.Single(item => item.Id == oldId).Status);
        Assert.Null(items.Single(item => item.Id == oldId).SupersededByMediaItemId);
        Assert.Equal(MediaStatus.Active, items.Single(item => item.Id == newId).Status);
    }

    [Fact]
    public async Task A_missing_file_that_returns_as_a_different_work_is_superseded()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"), "First Book", "Ann");
        var alice = await _host.AddUserAsync("alice");
        var oldId = await TouchAsync(alice, "novel.epub");
        File.Delete(BookPath("novel.epub"));
        await ReconcileAsync();

        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"), "Other Book", "Zed");
        var newId = await TouchAsync(alice, "novel.epub");

        Assert.NotEqual(oldId, newId);
        var items = await ItemsAsync();
        Assert.Equal(MediaStatus.Superseded, items.Single(item => item.Id == oldId).Status);
        Assert.Equal(MediaStatus.Active, items.Single(item => item.Id == newId).Status);
    }

    [Fact]
    public async Task True_duplicates_are_never_guessed_between()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("a.epub"));
        File.Copy(BookPath("a.epub"), BookPath("b.epub"));
        var alice = await _host.AddUserAsync("alice");
        // Both copies get an identity; then both vanish and a single copy reappears under a new name.
        var aId = await TouchAsync(alice, "a.epub");
        var bId = await TouchAsync(alice, "b.epub");
        Assert.NotEqual(aId, bId);
        var bytes = await File.ReadAllBytesAsync(BookPath("a.epub"));
        File.Delete(BookPath("a.epub"));
        File.Delete(BookPath("b.epub"));
        await File.WriteAllBytesAsync(BookPath("c.epub"), bytes);

        var report = await ReconcileAsync();

        Assert.Equal(0, report.Relinked);
        Assert.Equal(2, report.SentToReview);
        Assert.All(await ItemsAsync(), item => Assert.Equal(MediaStatus.NeedsReview, item.Status));
    }

    [Fact]
    public async Task Reconciling_a_stable_archive_changes_nothing()
    {
        EpubTestFixture.CreateMinimalEpub(BookPath("novel.epub"));
        var alice = await _host.AddUserAsync("alice");
        await TouchAsync(alice, "novel.epub");

        var report = await ReconcileAsync();

        Assert.Equal(new MediaReconcileReport(0, 0, 0, 0), report);
        Assert.Equal(MediaStatus.Active, Assert.Single(await ItemsAsync()).Status);
    }
}
