using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace WebApp.Authorization;

[Flags]
public enum FolderOperation
{
    None = 0,
    Read = 1,
    Write = 2,
    Create = 4,
    Delete = 8,
    Manage = 16
}

/// <summary>The operation requirements consumed by <see cref="FolderPermissionAuthorizationHandler"/>.</summary>
public static class FolderOperations
{
    public static readonly OperationAuthorizationRequirement Read = new() { Name = nameof(FolderOperation.Read) };
    public static readonly OperationAuthorizationRequirement Write = new() { Name = nameof(FolderOperation.Write) };
    public static readonly OperationAuthorizationRequirement Create = new() { Name = nameof(FolderOperation.Create) };
    public static readonly OperationAuthorizationRequirement Delete = new() { Name = nameof(FolderOperation.Delete) };
    public static readonly OperationAuthorizationRequirement Manage = new() { Name = nameof(FolderOperation.Manage) };

    public static readonly IReadOnlyList<FolderOperation> All =
        [FolderOperation.Read, FolderOperation.Write, FolderOperation.Create, FolderOperation.Delete, FolderOperation.Manage];

    public static OperationAuthorizationRequirement Requirement(FolderOperation operation) => operation switch
    {
        FolderOperation.Read => Read,
        FolderOperation.Write => Write,
        FolderOperation.Create => Create,
        FolderOperation.Delete => Delete,
        FolderOperation.Manage => Manage,
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    public static bool TryParse(string? name, out FolderOperation operation) =>
        Enum.TryParse(name, ignoreCase: true, out operation) && All.Contains(operation);
}
