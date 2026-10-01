using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Tests.Services;

/// <summary>Runs against a real SQLite file: the partial unique index and the free-then-insert ordering only mean something against the real engine.</summary>
public sealed class MediaItemRepositoryTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite($"Data Source={Path.Combine(_root.Path, "media.db")};Foreign Keys=True")
        .AddInterceptors(new SqliteConnectionInterceptor())
        .Options);

    private async Task<(AppDbContext Db, MediaItemRepository Repo, long FolderId)> StartAsync()
    {
        var db = CreateContext();
        await db.Database.MigrateAsync();
        var folder = new Folder { RootKey = "books", Label = "Books" };
        db.Folders.Add(folder);
        await db.SaveChangesAsync();
        return (db, new MediaItemRepository(db), folder.Id);
    }

    private static MediaItem Item(long folderId, string path, string key = "k") => new()
    {
        FolderId = folderId, Category = "book", RelativePath = path, Size = 10, LastWriteTimeUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ContentFingerprint = "fp-" + path, IdentityKey = key, CreatedUtc = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task Only_one_active_row_may_hold_a_path()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        await repo.AddActiveAsync(Item(folderId, "a.epub"));

        db.MediaItems.Add(Item(folderId, "a.epub"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Historical_rows_do_not_occupy_the_path()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var old = await repo.AddActiveAsync(Item(folderId, "a.epub"));
        old.Status = MediaStatus.Superseded;
        await db.SaveChangesAsync();

        await repo.AddActiveAsync(Item(folderId, "a.epub", "other"));
        db.MediaItems.Add(new MediaItem { FolderId = folderId, Category = "book", RelativePath = "a.epub", Status = MediaStatus.NeedsReview, CreatedUtc = DateTimeOffset.UtcNow });
        db.MediaItems.Add(new MediaItem { FolderId = folderId, Category = "book", RelativePath = "a.epub", Status = MediaStatus.Missing, CreatedUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        Assert.Equal(4, await db.MediaItems.CountAsync(item => item.RelativePath == "a.epub"));
        Assert.Equal("other", (await repo.GetActiveAsync(folderId, "a.epub"))!.IdentityKey);
    }

    [Theory]
    [InlineData(MediaStatus.Superseded)]
    [InlineData(MediaStatus.NeedsReview)]
    public async Task A_replacement_frees_the_path_before_inserting_the_new_active_row(MediaStatus oldStatus)
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var old = await repo.AddActiveAsync(Item(folderId, "a.epub", "old"));

        var replacement = await repo.ReplaceAsync(old, oldStatus, Item(folderId, "a.epub", "new"));

        Assert.Equal(replacement.Id, (await repo.GetActiveAsync(folderId, "a.epub"))!.Id);
        db.ChangeTracker.Clear();
        var reloaded = await db.MediaItems.SingleAsync(item => item.Id == old.Id);
        Assert.Equal(oldStatus, reloaded.Status);
        Assert.Equal(oldStatus == MediaStatus.Superseded ? replacement.Id : null, reloaded.SupersededByMediaItemId);
    }

    [Fact]
    public async Task Disallowed_replacement_statuses_are_rejected()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var old = await repo.AddActiveAsync(Item(folderId, "a.epub"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReplaceAsync(old, MediaStatus.Missing, Item(folderId, "a.epub", "new")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repo.ReplaceAsync(old, MediaStatus.Active, Item(folderId, "a.epub", "new")));
    }

    [Fact]
    public async Task A_failed_replacement_rolls_the_old_row_back()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var old = await repo.AddActiveAsync(Item(folderId, "a.epub", "old"));
        await repo.AddActiveAsync(Item(folderId, "b.epub"));

        // A replacement aimed at an occupied path violates the index; the old row must not stay half-updated.
        await Assert.ThrowsAsync<DbUpdateException>(() => repo.ReplaceAsync(old, MediaStatus.Superseded, Item(folderId, "b.epub", "new")));
        db.ChangeTracker.Clear();

        Assert.Equal(MediaStatus.Active, (await db.MediaItems.SingleAsync(item => item.Id == old.Id)).Status);
    }

    [Fact]
    public async Task Missing_rows_reactivate_only_when_the_path_is_free()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var item = await repo.AddActiveAsync(Item(folderId, "a.epub"));
        await repo.MarkMissingAsync(item, DateTimeOffset.UtcNow);
        Assert.Equal(MediaStatus.Missing, item.Status);
        Assert.NotNull(item.MissingSince);

        Assert.True(await repo.ReactivateAsync(item, 11, DateTime.UtcNow, "fp", bumpRevision: true));
        Assert.Equal(MediaStatus.Active, item.Status);
        Assert.Null(item.MissingSince);
        Assert.Equal(2, item.ContentRevision);

        await repo.MarkMissingAsync(item, DateTimeOffset.UtcNow);
        await repo.AddActiveAsync(Item(folderId, "a.epub", "squatter"));
        Assert.False(await repo.ReactivateAsync(item, 11, DateTime.UtcNow, "fp", bumpRevision: false));
        Assert.Equal(MediaStatus.NeedsReview, item.Status);
    }

    [Fact]
    public async Task Only_active_rows_can_go_missing_and_review_rows_never_return_to_active()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var item = await repo.AddActiveAsync(Item(folderId, "a.epub"));
        await repo.MarkNeedsReviewAsync(item);
        await repo.MarkMissingAsync(item, DateTimeOffset.UtcNow);

        Assert.Equal(MediaStatus.NeedsReview, item.Status);
    }

    [Fact]
    public async Task Relinking_keeps_identity_and_refuses_an_occupied_target()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var item = await repo.AddActiveAsync(Item(folderId, "a.epub"));
        await repo.AddActiveAsync(Item(folderId, "taken.epub"));
        var id = item.Id;

        Assert.False(await repo.RelinkAsync(item, folderId, "taken.epub", 10, DateTime.UtcNow, "fp"));
        Assert.True(await repo.RelinkAsync(item, folderId, "moved/b.epub", 10, DateTime.UtcNow, "fp"));

        Assert.Equal(id, item.Id);
        Assert.Null(await repo.GetActiveAsync(folderId, "a.epub"));
        Assert.Equal(id, (await repo.GetActiveAsync(folderId, "moved/b.epub"))!.Id);
    }

    [Fact]
    public async Task The_latest_missing_row_at_a_path_is_the_comparison_candidate()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var first = Item(folderId, "a.epub", "first");
        first.Status = MediaStatus.Missing;
        first.CreatedUtc = DateTimeOffset.UtcNow.AddDays(-2);
        var second = Item(folderId, "a.epub", "second");
        second.Status = MediaStatus.Missing;
        second.CreatedUtc = DateTimeOffset.UtcNow.AddDays(-1);
        db.MediaItems.AddRange(first, second);
        await db.SaveChangesAsync();

        Assert.Equal("second", (await repo.GetLatestMissingAtPathAsync(folderId, "a.epub"))!.IdentityKey);
    }

    [Fact]
    public async Task A_media_item_cannot_be_deleted_while_user_data_references_it()
    {
        var (db, repo, folderId) = await StartAsync();
        await using var _ = db;
        var item = await repo.AddActiveAsync(Item(folderId, "a.epub"));
        db.Users.Add(new WebApp.Identity.ApplicationUser { Id = "u1", UserName = "u1", NormalizedUserName = "U1", DisplayName = "u1", CreatedUtc = DateTimeOffset.UtcNow });
        db.Favorites.Add(new Favorite { UserId = "u1", MediaItemId = item.Id, CreatedUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        db.MediaItems.Remove(await db.MediaItems.SingleAsync(row => row.Id == item.Id));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
