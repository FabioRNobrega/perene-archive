using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using WebApp.Configuration;

namespace WebApp.Services;

internal sealed class EpubNoteService(IOptions<ArchiveRootOptions> options) : IEpubNoteService
{
    private const string NotesFileName = "pereneArchiveBookNotes.txt";

    private readonly string _archiveRootPath = Path.GetFullPath(options.Value.Path);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task AppendNoteAsync(
        string noteId,
        string bookTitle,
        string? bookAuthor,
        int chapterIndex,
        int? textOffsetStart,
        int? textOffsetEnd,
        string selectedText,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedText);

        var entry = BuildEntry(noteId, bookTitle, bookAuthor, chapterIndex, textOffsetStart, textOffsetEnd, selectedText);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var notesFolder = Path.Combine(_archiveRootPath, "Books", "Notes");
            Directory.CreateDirectory(notesFolder);
            var notesFilePath = Path.Combine(notesFolder, NotesFileName);
            await File.AppendAllTextAsync(notesFilePath, entry, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> RemoveNoteAsync(string noteId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(noteId);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var notesFilePath = Path.Combine(_archiveRootPath, "Books", "Notes", NotesFileName);
            if (!File.Exists(notesFilePath)) return false;

            var content = await File.ReadAllTextAsync(notesFilePath, cancellationToken);
            var marker = $"- Perene Archive Note ID: {noteId}";
            var entries = content.Split("==========", StringSplitOptions.None);
            var remainingEntries = entries.Where(entry => !entry.Split('\n').Any(line =>
                string.Equals(line.TrimEnd('\r'), marker, StringComparison.Ordinal))).ToArray();
            if (remainingEntries.Length == entries.Length) return false;

            var remaining = string.Concat(remainingEntries
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Select(entry => entry.TrimEnd('\r', '\n') + Environment.NewLine + "==========" + Environment.NewLine));
            await File.WriteAllTextAsync(notesFilePath, remaining, Encoding.UTF8, cancellationToken);
            return true;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string BuildEntry(
        string noteId,
        string bookTitle,
        string? bookAuthor,
        int chapterIndex,
        int? textOffsetStart,
        int? textOffsetEnd,
        string selectedText)
    {
        var titleLine = string.IsNullOrWhiteSpace(bookAuthor)
            ? bookTitle
            : $"{bookTitle} ({bookAuthor})";

        var location = textOffsetStart is not null && textOffsetEnd is not null
            ? $"Location {textOffsetStart.Value}-{textOffsetEnd.Value}"
            : $"Location {(chapterIndex + 1) * 1000}";

        var timestamp = DateTime.Now.ToString("dddd, d MMMM yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        var metadataLine = $"- Your Highlight on Chapter {chapterIndex + 1} | {location} | Added on {timestamp}";

        var builder = new StringBuilder();
        builder.AppendLine(titleLine);
        builder.AppendLine(metadataLine);
        builder.AppendLine($"- Perene Archive Note ID: {noteId}");
        builder.AppendLine();
        builder.AppendLine(selectedText.Trim());
        builder.AppendLine("==========");
        return builder.ToString();
    }
}
