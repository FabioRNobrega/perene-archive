namespace WebApp.Client.Models;

/// <summary>Counts only: what a media reconciliation scan changed. No paths or IDs.</summary>
public sealed record MediaReconcileReportDto(int Relinked, int Reactivated, int SentToReview, int MarkedMissing);
