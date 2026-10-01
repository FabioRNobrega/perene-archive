namespace WebApp.Data.Entities;

/// <summary>
/// The singleton (<see cref="SingletonId"/>) Shared-mode default policy plus the monotonic global policy version.
/// Every folder, permission, policy, and ownership change bumps <see cref="Version"/> in its own transaction.
/// </summary>
public sealed class AccessPolicy
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public long Version { get; set; } = 1;
    public bool DefaultRead { get; set; } = true;
    public bool DefaultCreate { get; set; }
    public bool DefaultWrite { get; set; }
    public bool DefaultDelete { get; set; }
}
