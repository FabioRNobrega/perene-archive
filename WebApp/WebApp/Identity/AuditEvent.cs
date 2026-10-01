namespace WebApp.Identity;

/// <summary>An append-only record of an account lifecycle action. Never holds passwords, tokens, keys, or recovery codes.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredUtc { get; set; }
    public required string Action { get; set; }
    public string? ActorUserId { get; set; }
    public string? TargetUserId { get; set; }
    public string? TargetUserName { get; set; }
    public string? Detail { get; set; }
}
