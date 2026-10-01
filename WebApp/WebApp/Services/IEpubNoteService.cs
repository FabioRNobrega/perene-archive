namespace WebApp.Services;

/// <summary>Per-user book notes. Every call resolves the signed-in user and the item's folder Read permission; arbitrary multiline text is preserved.</summary>
internal interface IEpubNoteService
{
    Task AppendNoteAsync(
        string categoryKey,
        string itemId,
        string noteId,
        string bookTitle,
        string? bookAuthor,
        int chapterIndex,
        int? textOffsetStart,
        int? textOffsetEnd,
        string selectedText,
        CancellationToken cancellationToken);

    Task<bool> RemoveNoteAsync(string categoryKey, string itemId, string noteId, CancellationToken cancellationToken);
}
