namespace WebApp.Client.Models;

/// <summary>Counts only: what a legacy-file import moved into the database and what it could not resolve. No paths or IDs.</summary>
public sealed record LegacyImportReportDto(
    DateTimeOffset RanUtc,
    bool BackupVerified,
    bool Verified,
    int Imported,
    int Skipped,
    IReadOnlyDictionary<string, int> ImportedByKind,
    IReadOnlyDictionary<string, int> SkippedByKind);

public sealed record MediaReconcileReportDto(int Relinked, int Reactivated, int SentToReview, int MarkedMissing);
