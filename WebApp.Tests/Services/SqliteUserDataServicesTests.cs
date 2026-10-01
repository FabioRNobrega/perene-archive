using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Tests.Services;

/// <summary>The SQLite-backed per-user services: two-user isolation, ownership plus folder Read, and content preservation.</summary>
public sealed class SqliteUserDataServicesTests : IDisposable
{
    private readonly MediaTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private string Epub(string name = "novel.epub", string title = "My Book") =>
        EpubTestFixture.CreateMinimalEpub(Path.Combine(_host.BooksPath, name), title, "My Author");

    private static void Comic(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var page in new[] { "page-01.jpg", "page-02.jpg" })
        {
            using var writer = new StreamWriter(zip.CreateEntry(page).Open());
            writer.Write("fixture " + page);
        }
    }

    private static BookHighlightDto Highlight(string id, string text = "first chapter") =>
        new(id, "0", 5, 5 + text.Length, text, "before", "after", DateTimeOffset.UtcNow);

    // ---- notes -----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Notes_keep_arbitrary_multiline_content_including_the_legacy_delimiter()
    {
        Epub();
        var alice = await _host.AddUserAsync("alice");
        const string text = "line one\n==========\nline three\r\n\r\nlast";

        await _host.AsAsync(alice, async sp =>
        {
            var id = _host.ItemId(sp, "books", "novel.epub");
            var notes = sp.GetRequiredService<IEpubNoteService>();
            await notes.AppendNoteAsync("books", id, "note-1", "My Book", "My Author", 2, 3, 40, text, CancellationToken.None);
            Assert.True(await notes.RemoveNoteAsync("books", id, "note-1", CancellationToken.None));
            Assert.False(await notes.RemoveNoteAsync("books", id, "note-1", CancellationToken.None));
            await notes.AppendNoteAsync("books", id, "note-2", "My Book", null, 0, null, null, text, CancellationToken.None);
        });

        await _host.WithDbAsync(async (_, db) =>
        {
            var note = await db.BookNotes.SingleAsync();
            Assert.Equal(text.Trim(), note.Content);
            Assert.Equal("note-2", note.NoteKey);
            Assert.Null(note.BookAuthor);
        });
    }

    [Fact]
    public async Task Notes_and_highlights_are_private_to_their_author()
    {
        Epub();
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        string id = "";

        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            await sp.GetRequiredService<IEpubHighlightService>().SaveHighlightAsync("books", id, Highlight("h1"), CancellationToken.None);
            await sp.GetRequiredService<IEpubNoteService>().AppendNoteAsync("books", id, "h1", "My Book", null, 0, 5, 18, "first chapter", CancellationToken.None);
        });

        await _host.AsAsync(bob, async sp =>
        {
            var highlights = sp.GetRequiredService<IEpubHighlightService>();
            Assert.Empty(await highlights.LoadHighlightsAsync("books", id, CancellationToken.None));
            // Bob can neither see nor remove Alice's records, even knowing their keys.
            Assert.False(await highlights.RemoveHighlightAsync("books", id, "h1", CancellationToken.None));
            Assert.False(await sp.GetRequiredService<IEpubNoteService>().RemoveNoteAsync("books", id, "h1", CancellationToken.None));
            await highlights.SaveHighlightAsync("books", id, Highlight("h1", "bobs own"), CancellationToken.None);
        });

        await _host.AsAsync(alice, async sp =>
        {
            var mine = Assert.Single(await sp.GetRequiredService<IEpubHighlightService>().LoadHighlightsAsync("books", id, CancellationToken.None));
            Assert.Equal("first chapter", mine.SelectedText);
            Assert.Equal("before", mine.ContextBefore);
        });

        await _host.WithDbAsync(async (_, db) =>
        {
            Assert.Equal(1, await db.BookNotes.CountAsync());
            Assert.Equal(2, await db.BookHighlights.CountAsync());
        });
    }

    // ---- progress --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reading_progress_is_per_user_and_updates_in_place()
    {
        Epub();
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        string id = "";

        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            var progress = sp.GetRequiredService<IEpubProgressService>();
            Assert.Null(await progress.LoadProgressAsync("books", id, CancellationToken.None));
            await progress.SaveProgressAsync("books", id, new BookProgressDto("1", 750), CancellationToken.None);
            await progress.SaveProgressAsync("books", id, new BookProgressDto("1", 900), CancellationToken.None);
            Assert.Equal(new BookProgressDto("1", 900), await progress.LoadProgressAsync("books", id, CancellationToken.None));
        });

        await _host.AsAsync(bob, async sp =>
        {
            var progress = sp.GetRequiredService<IEpubProgressService>();
            Assert.Null(await progress.LoadProgressAsync("books", id, CancellationToken.None));
            await progress.SaveProgressAsync("books", id, new BookProgressDto("0", 5), CancellationToken.None);
        });

        await _host.AsAsync(alice, async sp =>
            Assert.Equal(900, (await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None))!.WordOffset));
        await _host.WithDbAsync(async (_, db) => Assert.Equal(2, await db.ReadingProgresses.CountAsync()));
    }

    [Fact]
    public async Task Progress_made_against_an_older_revision_restarts_its_chapter()
    {
        var path = Epub();
        var alice = await _host.AddUserAsync("alice");
        string id = "";
        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            await sp.GetRequiredService<IEpubProgressService>().SaveProgressAsync("books", id, new BookProgressDto("1", 750), CancellationToken.None);
        });

        // Same work (same title/author/identifier), different bytes: a confirmed update bumps the revision.
        EpubTestFixture.CreateEpub(path, [
            new EpubTestFixture.SpineEntry("chapter1.xhtml", "Chapter One", "Rewritten first chapter with plenty of new words in it."),
            new EpubTestFixture.SpineEntry("chapter2.xhtml", "Chapter Two", "Second."),
            new EpubTestFixture.SpineEntry("chapter3.xhtml", "Chapter Three", "A whole new chapter.")
        ], "My Book", "My Author");

        await _host.AsAsync(alice, async sp =>
        {
            var loaded = await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None);
            Assert.Equal(new BookProgressDto("1", 0), loaded);
        });
        await _host.WithDbAsync(async (_, db) => Assert.Equal(2, (await db.MediaItems.SingleAsync()).ContentRevision));
    }

    [Fact]
    public async Task Comic_progress_is_per_user()
    {
        Comic(Path.Combine(_host.BooksPath, "comic.cbz"));
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        string id = "";

        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "comic.cbz");
            var progress = sp.GetRequiredService<IComicProgressService>();
            await progress.SaveProgressAsync("books", id, new ComicProgressDto(1), CancellationToken.None);
            await progress.SaveProgressAsync("books", id, new ComicProgressDto(-4), CancellationToken.None);
            Assert.Equal(0, (await progress.LoadProgressAsync("books", id, CancellationToken.None))!.PageIndex);
            await progress.SaveProgressAsync("books", id, new ComicProgressDto(1), CancellationToken.None);
        });
        await _host.AsAsync(bob, async sp => Assert.Null(await sp.GetRequiredService<IComicProgressService>().LoadProgressAsync("books", id, CancellationToken.None)));
        await _host.AsAsync(alice, async sp => Assert.Equal(1, (await sp.GetRequiredService<IComicProgressService>().LoadProgressAsync("books", id, CancellationToken.None))!.PageIndex));
    }

    // ---- favorites -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Favorites_toggle_per_user_and_are_marked_in_listings()
    {
        File.WriteAllText(Path.Combine(_host.ArchivePath, "Documents", "note.txt"), "text");
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        string id = "";

        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "documents", "note.txt");
            var favorites = sp.GetRequiredService<IArchiveFavoritesService>();
            Assert.True(await favorites.ToggleAsync("documents", id, CancellationToken.None));
            Assert.Contains(id, await favorites.GetFavoriteIdsAsync("documents", [id], CancellationToken.None));
        });
        await _host.AsAsync(bob, async sp =>
        {
            var favorites = sp.GetRequiredService<IArchiveFavoritesService>();
            Assert.Empty(await favorites.GetFavoriteIdsAsync("documents", [id], CancellationToken.None));
            Assert.True(await favorites.ToggleAsync("documents", id, CancellationToken.None));
            Assert.False(await favorites.ToggleAsync("documents", id, CancellationToken.None));
        });
        await _host.AsAsync(alice, async sp =>
            Assert.Contains(id, await sp.GetRequiredService<IArchiveFavoritesService>().GetFavoriteIdsAsync("documents", [id], CancellationToken.None)));
    }

    // ---- access ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Losing_folder_read_hides_a_users_data_and_restoring_it_brings_it_back()
    {
        Epub();
        var alice = await _host.AddUserAsync("alice");
        string id = "";
        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            await sp.GetRequiredService<IEpubProgressService>().SaveProgressAsync("books", id, new BookProgressDto("1", 42), CancellationToken.None);
        });

        await _host.SetBooksPrivateAsync(true);
        await _host.AsAsync(alice, async sp =>
        {
            var progress = sp.GetRequiredService<IEpubProgressService>();
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => progress.LoadProgressAsync("books", id, CancellationToken.None));
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => progress.SaveProgressAsync("books", id, new BookProgressDto("0", 1), CancellationToken.None));
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => sp.GetRequiredService<IArchiveFavoritesService>().ToggleAsync("books", id, CancellationToken.None));
        });
        // The rows are kept, merely inaccessible.
        await _host.WithDbAsync(async (_, db) => Assert.Equal(42, (await db.ReadingProgresses.SingleAsync()).WordOffset));

        await _host.SetBooksPrivateAsync(false);
        await _host.AsAsync(alice, async sp =>
            Assert.Equal(42, (await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None))!.WordOffset));
    }

    [Fact]
    public async Task Admins_have_no_path_to_other_users_rows_through_the_services()
    {
        Epub();
        var boss = await _host.AddUserAsync("boss", admin: true);
        var alice = await _host.AddUserAsync("alice");
        string id = "";
        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            await sp.GetRequiredService<IEpubHighlightService>().SaveHighlightAsync("books", id, Highlight("h1"), CancellationToken.None);
            await sp.GetRequiredService<IEpubProgressService>().SaveProgressAsync("books", id, new BookProgressDto("1", 9), CancellationToken.None);
        });

        await _host.AsAsync(boss, async sp =>
        {
            Assert.Empty(await sp.GetRequiredService<IEpubHighlightService>().LoadHighlightsAsync("books", id, CancellationToken.None));
            Assert.Null(await sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None));
        });
    }

    [Fact]
    public async Task Unknown_items_and_signed_out_callers_are_not_found()
    {
        Epub();
        var alice = await _host.AddUserAsync("alice");
        string id = "";
        await _host.AsAsync(alice, async sp =>
        {
            id = _host.ItemId(sp, "books", "novel.epub");
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", "nope", CancellationToken.None));
        });
        await _host.AsAsync("", async sp =>
        {
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => sp.GetRequiredService<IEpubProgressService>().LoadProgressAsync("books", id, CancellationToken.None));
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => sp.GetRequiredService<IEpubReaderThemeService>().LoadAsync(CancellationToken.None));
        });
    }

    // ---- reader themes ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reader_themes_are_shared_but_only_their_creator_can_change_them_and_active_settings_stay_personal()
    {
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        var night = new BookReaderThemeSettingsDto("Arial", 24, 2.0, "#112233", "#AABBCC");
        string themeId = "";

        await _host.AsAsync(alice, async sp =>
        {
            var themes = sp.GetRequiredService<IEpubReaderThemeService>();
            Assert.Equal("Montserrat", (await themes.LoadAsync(CancellationToken.None)).ActiveSettings.FontFamily);
            var created = await themes.CreateAsync(new CreateBookReaderThemeRequest("Night", night), CancellationToken.None);
            themeId = Assert.Single(created.Themes).Id;
            Assert.Equal(themeId, created.SelectedThemeId);
            await Assert.ThrowsAsync<ArgumentException>(() => themes.CreateAsync(new CreateBookReaderThemeRequest("  ", night), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => themes.SaveActiveAsync(new BookReaderThemeSettingsDto("Bad", 99, 9, "red", null), CancellationToken.None));
        });

        await _host.AsAsync(bob, async sp =>
        {
            var themes = sp.GetRequiredService<IEpubReaderThemeService>();
            // Bob sees Alice's theme but still has his own default active settings and no selection.
            var library = await themes.LoadAsync(CancellationToken.None);
            Assert.Equal(["Night"], library.Themes.Select(theme => theme.Name));
            Assert.False(library.Themes[0].CanEdit);
            Assert.Equal("Montserrat", library.ActiveSettings.FontFamily);
            Assert.Null(library.SelectedThemeId);
            // He can use it by applying its settings, which changes only his own reader.
            var applied = await themes.SaveActiveAsync(night, CancellationToken.None);
            Assert.Equal(themeId, applied.SelectedThemeId);
            Assert.Equal("Arial", applied.ActiveSettings.FontFamily);
            // Only the creator can edit or delete it.
            Assert.Null(await themes.UpdateAsync(themeId, new UpdateBookReaderThemeRequest("Hijack", night), CancellationToken.None));
            Assert.Null(await themes.DeleteAsync(themeId, CancellationToken.None));
            // Names may repeat, even across users.
            var twin = await themes.CreateAsync(new CreateBookReaderThemeRequest("night", night with { FontSizePx = 20 }), CancellationToken.None);
            Assert.Equal(2, twin.Themes.Count);
        });

        await _host.AsAsync(alice, async sp =>
        {
            var themes = sp.GetRequiredService<IEpubReaderThemeService>();
            var library = await themes.LoadAsync(CancellationToken.None);
            Assert.Equal(2, library.Themes.Count);
            Assert.True(library.Themes.Single(theme => theme.Id == themeId).CanEdit);
            Assert.False(library.Themes.Single(theme => theme.Id != themeId).CanEdit);
            Assert.Equal(24, library.ActiveSettings.FontSizePx);
            Assert.Equal(themeId, library.SelectedThemeId);
            var updated = await themes.UpdateAsync(themeId, new UpdateBookReaderThemeRequest("Evening", night with { FontSizePx = 28 }), CancellationToken.None);
            Assert.Contains(updated!.Themes, theme => theme.Name == "Evening");
            Assert.Equal(28, updated.ActiveSettings.FontSizePx);
            Assert.Equal(themeId, updated.SelectedThemeId);
        });
    }

    [Fact]
    public async Task Deleting_a_theme_resets_only_the_selection_of_whoever_had_it_selected()
    {
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        var settings = new BookReaderThemeSettingsDto("Arial", 24, 2.0, null, null);
        string themeId = "";
        await _host.AsAsync(alice, async sp =>
            themeId = Assert.Single((await sp.GetRequiredService<IEpubReaderThemeService>().CreateAsync(new CreateBookReaderThemeRequest("Night", settings), CancellationToken.None)).Themes).Id);
        await _host.AsAsync(bob, async sp => Assert.Equal(themeId, (await sp.GetRequiredService<IEpubReaderThemeService>().SaveActiveAsync(settings, CancellationToken.None)).SelectedThemeId));

        await _host.AsAsync(alice, async sp =>
        {
            var deleted = await sp.GetRequiredService<IEpubReaderThemeService>().DeleteAsync(themeId, CancellationToken.None);
            Assert.Empty(deleted!.Themes);
            Assert.Null(deleted.SelectedThemeId);
        });

        await _host.AsAsync(bob, async sp =>
        {
            var library = await sp.GetRequiredService<IEpubReaderThemeService>().LoadAsync(CancellationToken.None);
            Assert.Empty(library.Themes);
            Assert.Null(library.SelectedThemeId);
            Assert.Equal("Arial", library.ActiveSettings.FontFamily);
        });
    }

    [Fact]
    public async Task A_theme_outlives_its_deleted_creator_and_nobody_can_edit_it_afterwards()
    {
        var boss = await _host.AddUserAsync("boss", admin: true);
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        var settings = new BookReaderThemeSettingsDto("Arial", 24, 2.0, null, null);
        string themeId = "";
        await _host.AsAsync(alice, async sp =>
            themeId = Assert.Single((await sp.GetRequiredService<IEpubReaderThemeService>().CreateAsync(new CreateBookReaderThemeRequest("Night", settings), CancellationToken.None)).Themes).Id);

        await _host.WithDbAsync(async (sp, _) => Assert.True((await sp.GetRequiredService<WebApp.Identity.AccountLifecycleService>().DeleteAsync(boss, "alice")).Succeeded));

        await _host.AsAsync(bob, async sp =>
        {
            var themes = sp.GetRequiredService<IEpubReaderThemeService>();
            Assert.Equal(["Night"], (await themes.LoadAsync(CancellationToken.None)).Themes.Select(theme => theme.Name));
            Assert.Null(await themes.UpdateAsync(themeId, new UpdateBookReaderThemeRequest("Mine now", settings), CancellationToken.None));
            Assert.Null(await themes.DeleteAsync(themeId, CancellationToken.None));
        });
        await _host.WithDbAsync(async (_, db) => Assert.Null((await db.ReaderThemes.SingleAsync()).CreatedByUserId));
    }

    // ---- custom storage views --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Custom_storage_views_are_owned_per_user_with_global_views_visible_to_all()
    {
        Directory.CreateDirectory(Path.Combine(_host.ArchivePath, "Videos", "Clips"));
        File.WriteAllBytes(Path.Combine(_host.ArchivePath, "Videos", "Clips", "a.bin"), new byte[100]);
        var alice = await _host.AddUserAsync("alice");
        var bob = await _host.AddUserAsync("bob");
        string viewId = "";

        await _host.WithDbAsync(async (_, db) =>
        {
            db.CustomStorageViews.Add(new CustomStorageView { PublicId = "global", UserId = null, IsWholeArchive = true, Title = "Everything", MaxSizeBytes = 1, CreatedUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        await _host.AsAsync(alice, async sp =>
        {
            var folder = sp.GetRequiredService<IArchiveService>().List("videos", null).Items.Single(item => item.Name == "Clips").Id;
            var views = sp.GetRequiredService<ICustomStorageViewService>();
            var added = await views.AddAsync("videos", folder, false, 1024, CancellationToken.None);
            var mine = added.Single(view => view.Title == "Clips");
            viewId = mine.Id;
            Assert.True(mine.IsAvailable);
            Assert.Equal(100, mine.UsedBytes);
            Assert.Contains(added, view => view.Id == "global");
            await Assert.ThrowsAsync<ArchiveNotFoundException>(() => views.AddAsync("videos", "not-a-real-id", false, 1024, CancellationToken.None));
        });

        await _host.AsAsync(bob, async sp =>
        {
            var views = sp.GetRequiredService<ICustomStorageViewService>();
            Assert.Equal(["global"], (await views.GetAllAsync(CancellationToken.None)).Select(view => view.Id));
            Assert.Null(await views.RemoveAsync(viewId, CancellationToken.None));
            Assert.Null(await views.UpdateMaxSizeAsync(viewId, 5, CancellationToken.None));
            Assert.Null(await views.RemoveAsync("global", CancellationToken.None));
        });

        await _host.AsAsync(alice, async sp =>
        {
            var views = sp.GetRequiredService<ICustomStorageViewService>();
            Assert.Equal(4096, (await views.UpdateMaxSizeAsync(viewId, 4096, CancellationToken.None))!.Single(view => view.Id == viewId).MaxBytes);
            Assert.DoesNotContain(await views.RemoveAsync(viewId, CancellationToken.None) ?? [], view => view.Id == viewId);
        });
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_data_but_not_the_media_identity()
    {
        Epub();
        var boss = await _host.AddUserAsync("boss", admin: true);
        var alice = await _host.AddUserAsync("alice");
        await _host.AsAsync(alice, async sp =>
        {
            var id = _host.ItemId(sp, "books", "novel.epub");
            await sp.GetRequiredService<IEpubProgressService>().SaveProgressAsync("books", id, new BookProgressDto("1", 7), CancellationToken.None);
            await sp.GetRequiredService<IArchiveFavoritesService>().ToggleAsync("books", id, CancellationToken.None);
        });

        await _host.WithDbAsync(async (sp, db) =>
        {
            var lifecycle = sp.GetRequiredService<WebApp.Identity.AccountLifecycleService>();
            Assert.True((await lifecycle.DeleteAsync(boss, "alice")).Succeeded);
            Assert.Empty(await db.ReadingProgresses.ToListAsync());
            Assert.Empty(await db.Favorites.ToListAsync());
            Assert.Single(await db.MediaItems.ToListAsync());
        });
    }
}
