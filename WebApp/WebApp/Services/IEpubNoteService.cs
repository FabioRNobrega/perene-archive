namespace WebApp.Services;

internal interface IEpubNoteService
{
    /// <summary>
    /// Appends one ID-linked Kindle-style clipping entry to <c>${ArchiveRoot}/Books/Notes/pereneArchiveBookNotes.txt</c>,
    /// creating the <c>Books/Notes</c> folder and the file if they do not already exist. Concurrent calls are
    /// serialized so appends never interleave or corrupt the shared file.
    /// </summary>
    Task AppendNoteAsync(
        string noteId,
        string bookTitle,
        string? bookAuthor,
        int chapterIndex,
        int? textOffsetStart,
        int? textOffsetEnd,
        string selectedText,
        CancellationToken cancellationToken);

    Task<bool> RemoveNoteAsync(string noteId, CancellationToken cancellationToken);
}
