namespace WebApp.Client.Models;

/// <summary>Derived batch state and cleanup operations for the archive upload preview.</summary>
public sealed record ArchiveUploadPreviewState(
    int Total,
    int Done,
    int Pending,
    int Uploading,
    int Completing,
    int Interrupted,
    int Error)
{
    /// <summary>Whether the completed-only bulk dismissal action is available.</summary>
    public bool CanDismissCompleted => Done > 0;

    /// <summary>Counts every upload state in a batch.</summary>
    public static ArchiveUploadPreviewState From(IEnumerable<ArchiveUploadItemState> uploads)
    {
        ArgumentNullException.ThrowIfNull(uploads);

        var counts = new int[Enum.GetValues<ArchiveUploadItemStatus>().Length];
        foreach (var upload in uploads)
        {
            counts[(int)upload.Status]++;
        }

        return new ArchiveUploadPreviewState(
            counts.Sum(),
            counts[(int)ArchiveUploadItemStatus.Done],
            counts[(int)ArchiveUploadItemStatus.Pending],
            counts[(int)ArchiveUploadItemStatus.Uploading],
            counts[(int)ArchiveUploadItemStatus.Completing],
            counts[(int)ArchiveUploadItemStatus.Interrupted],
            counts[(int)ArchiveUploadItemStatus.Error]);
    }

    /// <summary>Removes all completed uploads and their associated browser file references.</summary>
    public static void DismissCompleted<TFileReference>(
        ICollection<ArchiveUploadItemState> uploads,
        IDictionary<ArchiveUploadItemState, TFileReference> fileReferences)
    {
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(fileReferences);

        foreach (var upload in uploads.Where(upload => upload.Status == ArchiveUploadItemStatus.Done).ToList())
        {
            fileReferences.Remove(upload);
            uploads.Remove(upload);
        }
    }

    /// <summary>Removes one upload and its associated browser file reference.</summary>
    public static void Dismiss<TFileReference>(
        ArchiveUploadItemState upload,
        ICollection<ArchiveUploadItemState> uploads,
        IDictionary<ArchiveUploadItemState, TFileReference> fileReferences)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(uploads);
        ArgumentNullException.ThrowIfNull(fileReferences);

        fileReferences.Remove(upload);
        uploads.Remove(upload);
    }
}
