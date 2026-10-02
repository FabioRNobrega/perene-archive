using WebApp.Identity;

namespace WebApp.Data.Entities;

public enum JobType { Composition = 0, ArchiveMutation = 1, VideoConversion = 2, Cut = 3 }

/// <summary>
/// A durable record of one queued or finished background job. <see cref="PayloadJson"/> holds stable identities
/// (MediaItem/Folder ids and the identity values captured at enqueue), never a physical or relative path or a snapshot ID.
/// <see cref="StatusJson"/> is the browser-safe status record the existing polling endpoints return. Every column is server-only.
/// </summary>
public sealed class Job
{
    /// <summary>The job id handed to the browser (a GUID in "N" format).</summary>
    public required string Id { get; set; }
    public JobType Type { get; set; }

    /// <summary>The status enum name; the terminal set differs per <see cref="Type"/>.</summary>
    public required string State { get; set; }

    /// <summary>The account that enqueued the job; null when it was an internal job or the account was later deleted.</summary>
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? PayloadJson { get; set; }
    public string? StatusJson { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>When the job first began running (Processing or later); null while it is still queued or if it never ran.</summary>
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
}

/// <summary>The last number handed out for one output name series, so deleting files never lets a number be reused.</summary>
public sealed class NamingCounter
{
    public long Id { get; set; }
    public required string Kind { get; set; }

    /// <summary>The normalized output directory the series lives in (server-only).</summary>
    public required string Directory { get; set; }
    public required string Prefix { get; set; }
    public required string Extension { get; set; }
    public int LastValue { get; set; }
}
