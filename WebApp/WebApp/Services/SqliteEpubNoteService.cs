using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Services;

internal sealed class SqliteEpubNoteService(AppDbContext db, IUserMediaContext context) : IEpubNoteService
{
    public async Task AppendNoteAsync(
        string categoryKey, string itemId, string noteId, string bookTitle, string? bookAuthor, int chapterIndex,
        int? textOffsetStart, int? textOffsetEnd, string selectedText, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedText);

        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        db.BookNotes.Add(new BookNote
        {
            NoteKey = noteId,
            UserId = media.UserId,
            MediaItemId = media.Item.Id,
            BookTitle = bookTitle,
            BookAuthor = string.IsNullOrWhiteSpace(bookAuthor) ? null : bookAuthor,
            ChapterIndex = chapterIndex,
            TextOffsetStart = textOffsetStart,
            TextOffsetEnd = textOffsetEnd,
            Content = selectedText.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveNoteAsync(string categoryKey, string itemId, string noteId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        var media = await context.RequireAsync(categoryKey, itemId, cancellationToken);
        var removed = await db.BookNotes.Where(note => note.UserId == media.UserId && note.MediaItemId == media.Item.Id && note.NoteKey == noteId)
            .ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }
}
