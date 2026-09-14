using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WebApp.Configuration;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Owns the lifecycle of resumable archive upload sessions: a dedicated, server-only
/// <c>&lt;ArchiveRoot&gt;/.uploads</c> workspace holds each session's <c>&lt;id&gt;.json</c> metadata
/// and <c>&lt;id&gt;.part</c> temporary content. No physical or root-relative path is ever exposed
/// through this type's public contract (<see cref="ArchiveUploadSession"/> carries only opaque IDs
/// and safe display names). <see cref="IArchiveService"/> remains the sole authority for category
/// containment, safe names, supported extensions, and final publication.
/// </summary>
internal sealed class ArchiveUploadService : IArchiveUploadService
{
    private const string WorkspaceFolderName = ".uploads";

    private readonly IArchiveService _archive;
    private readonly ArchiveUploadOptions _options;
    private readonly IArchiveUploadClock _clock;
    private readonly string _workspacePath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public ArchiveUploadService(
        IOptions<ArchiveRootOptions> rootOptions,
        IOptions<ArchiveUploadOptions> uploadOptions,
        IArchiveService archive,
        IArchiveUploadClock? clock = null)
    {
        _archive = archive;
        _options = uploadOptions.Value;
        _clock = clock ?? new SystemArchiveUploadClock();
        _workspacePath = Path.Combine(Path.GetFullPath(rootOptions.Value.Path), WorkspaceFolderName);
        Directory.CreateDirectory(_workspacePath);
    }

    public ArchiveUploadSession Create(string categoryKey, string? parentId, string fileName, long totalBytes)
    {
        if (totalBytes <= 0 || totalBytes > _options.MaxDeclaredSizeBytes)
        {
            throw new ArchiveValidationException("The declared upload size is not supported.");
        }

        // Validate now (category/parent/name/extension/collision) without writing the final file;
        // ArchiveService remains the sole authority for these rules.
        _archive.ValidateUploadDestination(categoryKey, parentId, fileName);

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow;
        var session = new ArchiveUploadSession(
            id,
            categoryKey,
            parentId,
            fileName,
            totalBytes,
            ReceivedBytes: 0,
            _options.SelectChunkSize(totalBytes),
            ArchiveUploadSessionStatus.Uploading,
            now,
            now);

        var partPath = GetPartPath(id);
        var metaPath = GetMetaPath(id);
        try
        {
            using (new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            WriteMetadataAtomic(metaPath, session);
        }
        catch
        {
            TryDelete(partPath);
            TryDelete(metaPath);
            throw;
        }

        return session;
    }

    public ArchiveUploadSession GetStatus(string categoryKey, string uploadId)
    {
        ValidateId(uploadId);
        var session = ReadMetadata(uploadId) ?? throw new ArchiveNotFoundException("The upload session does not exist.");
        EnsureCategory(session, categoryKey);
        EnsureNotExpired(session);
        return session;
    }

    public async Task<ArchiveUploadSession> AppendChunkAsync(
        string categoryKey, string uploadId, long offset, long expectedLength, Stream body, CancellationToken cancellationToken)
    {
        ValidateId(uploadId);
        var gate = GetGate(uploadId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = ReadMetadata(uploadId) ?? throw new ArchiveNotFoundException("The upload session does not exist.");
            EnsureCategory(session, categoryKey);
            EnsureNotExpired(session);

            if (session.Status != ArchiveUploadSessionStatus.Uploading)
            {
                throw new ArchiveConflictException("The upload session is not accepting chunks.");
            }

            if (offset != session.ReceivedBytes)
            {
                throw new ArchiveConflictException("The chunk offset does not match the next expected offset.");
            }

            if (expectedLength <= 0 || offset + expectedLength > session.TotalBytes)
            {
                throw new ArchiveValidationException("The chunk length is invalid.");
            }

            var partPath = GetPartPath(uploadId);
            long written;
            await using (var stream = new FileStream(
                partPath, FileMode.Open, FileAccess.Write, FileShare.Read, bufferSize: 1024 * 1024, useAsync: true))
            {
                stream.Position = offset;
                written = await CopyExactAsync(body, stream, expectedLength, cancellationToken);
            }

            if (written != expectedLength)
            {
                throw new ArchiveValidationException("The chunk was not fully received.");
            }

            var updated = session with { ReceivedBytes = offset + written, LastActivityAt = _clock.UtcNow };
            WriteMetadataAtomic(GetMetaPath(uploadId), updated);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ArchiveListing> CompleteAsync(string categoryKey, string uploadId, CancellationToken cancellationToken)
    {
        ValidateId(uploadId);
        var gate = GetGate(uploadId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = ReadMetadata(uploadId) ?? throw new ArchiveNotFoundException("The upload session does not exist.");
            EnsureCategory(session, categoryKey);
            EnsureNotExpired(session);

            if (session.Status != ArchiveUploadSessionStatus.Uploading)
            {
                throw new ArchiveConflictException("The upload session is not ready to complete.");
            }

            if (session.ReceivedBytes != session.TotalBytes)
            {
                throw new ArchiveValidationException("The upload is not complete.");
            }

            var partPath = GetPartPath(uploadId);
            var actualLength = new FileInfo(partPath).Length;
            if (actualLength != session.TotalBytes)
            {
                throw new ArchiveValidationException("The uploaded file size does not match the declared size.");
            }

            var listing = _archive.PublishUploadedFile(session.CategoryKey, session.ParentId, session.FileName, partPath);
            TryDelete(GetMetaPath(uploadId));
            return listing;
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(uploadId, out _);
        }
    }

    public async Task CancelAsync(string categoryKey, string uploadId, CancellationToken cancellationToken)
    {
        ValidateId(uploadId);
        var gate = GetGate(uploadId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = ReadMetadata(uploadId) ?? throw new ArchiveNotFoundException("The upload session does not exist.");
            EnsureCategory(session, categoryKey);

            TryDelete(GetPartPath(uploadId));
            TryDelete(GetMetaPath(uploadId));
        }
        finally
        {
            gate.Release();
            _locks.TryRemove(uploadId, out _);
        }
    }

    public IReadOnlyList<ArchiveUploadSession> ListActive(string categoryKey, string? parentId)
    {
        var sessions = new List<ArchiveUploadSession>();
        foreach (var metaPath in SafeEnumerateMetaFiles())
        {
            var session = TryReadMetadataFile(metaPath);
            if (session is null || session.Status != ArchiveUploadSessionStatus.Uploading)
            {
                continue;
            }

            if (!string.Equals(session.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(session.ParentId ?? string.Empty, parentId ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsExpired(session))
            {
                continue;
            }

            sessions.Add(session);
        }

        return sessions;
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        foreach (var metaPath in SafeEnumerateMetaFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = TryReadMetadataFile(metaPath);
            if (session is null || !IsExpired(session))
            {
                continue;
            }

            var gate = GetGate(session.Id);
            await gate.WaitAsync(cancellationToken);
            try
            {
                // Re-read after acquiring the lock in case a concurrent write refreshed activity
                // or completed/cancelled the session while cleanup was waiting.
                var current = TryReadMetadataFile(metaPath);
                if (current is null || !IsExpired(current))
                {
                    continue;
                }

                TryDelete(GetPartPath(current.Id));
                TryDelete(metaPath);
            }
            finally
            {
                gate.Release();
                _locks.TryRemove(session.Id, out _);
            }
        }
    }

    private SemaphoreSlim GetGate(string uploadId) => _locks.GetOrAdd(uploadId, static _ => new SemaphoreSlim(1, 1));

    private bool IsExpired(ArchiveUploadSession session) =>
        _clock.UtcNow - session.LastActivityAt > TimeSpan.FromHours(_options.SessionTtlHours);

    private void EnsureNotExpired(ArchiveUploadSession session)
    {
        if (IsExpired(session))
        {
            throw new ArchiveNotFoundException("The upload session has expired.");
        }
    }

    private static void EnsureCategory(ArchiveUploadSession session, string categoryKey)
    {
        if (!string.Equals(session.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArchiveNotFoundException("The upload session does not exist.");
        }
    }

    private static async Task<long> CopyExactAsync(Stream source, Stream destination, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (total < length)
        {
            var toRead = (int)Math.Min(buffer.Length, length - total);
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        return total;
    }

    private ArchiveUploadSession? ReadMetadata(string id) => TryReadMetadataFile(GetMetaPath(id));

    private static ArchiveUploadSession? TryReadMetadataFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ArchiveUploadSession>(json);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void WriteMetadataAtomic(string path, ArchiveUploadSession session)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(session));
        File.Move(tempPath, path, overwrite: true);
    }

    private string GetPartPath(string id) => Path.Combine(_workspacePath, $"{id}.part");

    private string GetMetaPath(string id) => Path.Combine(_workspacePath, $"{id}.json");

    private IEnumerable<string> SafeEnumerateMetaFiles()
    {
        try
        {
            return Directory.EnumerateFiles(_workspacePath, "*.json").ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateId(string uploadId)
    {
        if (string.IsNullOrWhiteSpace(uploadId) ||
            uploadId.Length is 0 or > 64 ||
            !uploadId.All(char.IsLetterOrDigit))
        {
            throw new ArchiveNotFoundException("The upload session does not exist.");
        }
    }
}
