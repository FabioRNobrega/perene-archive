using Microsoft.Extensions.DependencyInjection;
using WebApp.Models;

namespace WebApp.Authorization;

/// <summary>
/// Re-authorizes queued work when it is about to run. Jobs carry the account that enqueued them; the check is fresh against the
/// database, so a revoked grant, a deactivated account, or a deleted user stops the job. A job with no actor is internal and unchecked.
/// </summary>
internal interface IFolderJobAuthorizer
{
    Task<bool> CanRunAsync(CutJob job, CancellationToken cancellationToken);
    Task<bool> CanRunAsync(CompositionJob job, CancellationToken cancellationToken);
    Task<bool> CanRunAsync(VideoConversionJob job, CancellationToken cancellationToken);
    Task<bool> CanRunAsync(ArchiveMutationJob job, CancellationToken cancellationToken);

    /// <summary>Read on the folder holding <paramref name="entry"/>, for derived-media jobs such as audio-track remux.</summary>
    Task<bool> CanReadAsync(string? actorUserId, VideoFileEntry entry, CancellationToken cancellationToken);
}

internal sealed class FolderJobAuthorizer(IServiceScopeFactory scopes, FolderLocator locator) : IFolderJobAuthorizer
{
    public Task<bool> CanRunAsync(CutJob job, CancellationToken cancellationToken) => AllowedAsync(job.ActorUserId,
        [(FolderOperation.Read, locator.LocateContainer(job.SourceEntry.PhysicalPath)),
         (FolderOperation.Create, locator.LocateRoot(FolderLocator.CutRootKey))], cancellationToken);

    public Task<bool> CanRunAsync(CompositionJob job, CancellationToken cancellationToken) => AllowedAsync(job.ActorUserId,
        [(FolderOperation.Read, locator.LocateRoot(FolderLocator.CutRootKey)),
         (FolderOperation.Create, locator.LocateRoot(FolderLocator.CompositionRootKey))], cancellationToken);

    public Task<bool> CanRunAsync(VideoConversionJob job, CancellationToken cancellationToken)
    {
        var container = locator.LocateContainer(job.Source.PhysicalPath);
        return AllowedAsync(job.ActorUserId, [(FolderOperation.Read, container), (FolderOperation.Create, container)], cancellationToken);
    }

    public Task<bool> CanRunAsync(ArchiveMutationJob job, CancellationToken cancellationToken)
    {
        var requirements = new List<(FolderOperation, FolderLocation?)>();
        switch (job.Kind)
        {
            case Client.Models.ArchiveMutationKind.EmptyTrash:
                requirements.Add((FolderOperation.Delete, locator.LocateDirectory(job.SourcePath)));
                break;
            case Client.Models.ArchiveMutationKind.Move:
                Add(requirements, job.SourcePath, job.DestinationPath, isTrash: false);
                break;
            case Client.Models.ArchiveMutationKind.MoveToTrash:
                Add(requirements, job.SourcePath, null, isTrash: true);
                break;
            default:
                foreach (var entry in job.BatchEntries ?? [])
                    Add(requirements, entry.SourcePath, entry.DestinationPath, job.Kind == Client.Models.ArchiveMutationKind.BatchMoveToTrash);
                break;
        }

        return AllowedAsync(job.ActorUserId, requirements, cancellationToken);
    }

    public Task<bool> CanReadAsync(string? actorUserId, VideoFileEntry entry, CancellationToken cancellationToken) =>
        AllowedAsync(actorUserId, [(FolderOperation.Read, locator.LocateContainer(entry.PhysicalPath))], cancellationToken);

    private void Add(List<(FolderOperation, FolderLocation?)> requirements, string source, string? destination, bool isTrash)
    {
        requirements.Add((FolderOperation.Delete, locator.LocateContainer(source)));
        if (!isTrash && destination is not null) requirements.Add((FolderOperation.Create, locator.LocateContainer(destination)));
    }

    private async Task<bool> AllowedAsync(string? actorUserId, IReadOnlyList<(FolderOperation Operation, FolderLocation? Location)> requirements, CancellationToken cancellationToken)
    {
        if (actorUserId is null) return true;
        using var scope = scopes.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<IFolderAccessService>();
        foreach (var (operation, location) in requirements)
        {
            if (location is null || !await access.CheckAsync(actorUserId, operation, location, cancellationToken)) return false;
        }

        return true;
    }
}

/// <summary>Keeps folder rows aligned with the filesystem after a queued move, trash, or empty-trash job succeeds.</summary>
internal interface IFolderPathSync
{
    Task AfterMutationAsync(ArchiveMutationJob job, CancellationToken cancellationToken);
}

internal sealed class FolderPathSync(IServiceScopeFactory scopes, FolderLocator locator) : IFolderPathSync
{
    public async Task AfterMutationAsync(ArchiveMutationJob job, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<FolderCatalog>();
        switch (job.Kind)
        {
            case Client.Models.ArchiveMutationKind.EmptyTrash:
                if (locator.LocateDirectory(job.SourcePath) is { } root) await catalog.RemoveAsync(root, includeSelf: false, cancellationToken);
                break;
            case Client.Models.ArchiveMutationKind.Move or Client.Models.ArchiveMutationKind.MoveToTrash:
                await RelocateAsync(catalog, job.IsFolder, job.SourcePath, job.DestinationPath, cancellationToken);
                break;
            default:
                foreach (var entry in job.BatchEntries ?? [])
                    await RelocateAsync(catalog, entry.IsFolder, entry.SourcePath, entry.DestinationPath, cancellationToken);
                break;
        }
    }

    private async Task RelocateAsync(FolderCatalog catalog, bool isFolder, string source, string? destination, CancellationToken cancellationToken)
    {
        if (!isFolder || destination is null) return;
        if (locator.LocateDirectory(source) is { } from && locator.LocateDirectory(destination) is { } to)
            await catalog.RelocateAsync(from, to, cancellationToken);
    }
}
