using WebApp.Authorization;
using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Endpoints;

/// <summary>Which directory of a resolved archive item a requirement applies to.</summary>
internal enum ItemTarget
{
    /// <summary>The folder itself, or the folder that holds a file.</summary>
    Self,

    /// <summary>The folder that holds the item, for rename/move/delete of a file or folder.</summary>
    Container,

    /// <summary>Every folder row below a folder item must be readable; used so a folder holding hidden content cannot be renamed, moved, or deleted.</summary>
    Subtree
}

/// <summary>The route-to-folder mapping for every protected media route, in one place.</summary>
internal static class AccessRules
{
    private const string CutsSubfolder = "cuts";

    private sealed record Resolved(FolderLocation? Self, FolderLocation? Container, bool IsFolder);

    public static AccessRule ArchiveItem(params (FolderOperation Operation, ItemTarget Target)[] checks) => context =>
    {
        var http = context.HttpContext;
        return ItemChecks(http, Route(http, "category"), Route(http, "id"), checks);
    };

    /// <summary>A folder (or the category root) named by a request body or query, such as a create/upload parent.</summary>
    public static AccessRule ArchiveFolder(FolderOperation operation, Func<EndpointFilterInvocationContext, string?> folderId) => context =>
    {
        var http = context.HttpContext;
        var resolved = ResolveFolderOrRoot(http, Route(http, "category"), folderId(context));
        return Single(resolved is null ? null : new AccessRequirement(operation, resolved.Self));
    };

    public static AccessRule CreateInParent<T>(Func<T, string?> parentId) => ArchiveFolder(FolderOperation.Create, context =>
        context.Arguments.OfType<T>().FirstOrDefault() is { } body ? parentId(body) : null);

    public static AccessRule UploadSession() => context =>
    {
        var http = context.HttpContext;
        var category = Route(http, "category");
        try
        {
            var session = http.RequestServices.GetRequiredService<IArchiveUploadService>().GetStatus(category, Route(http, "uploadId"));
            var resolved = ResolveFolderOrRoot(http, category, session.ParentId);
            return Single(resolved is null ? null : new AccessRequirement(FolderOperation.Create, resolved.Self));
        }
        catch (ArchiveException)
        {
            return Empty;
        }
    };

    public static AccessRule Move() => async context =>
    {
        var http = context.HttpContext;
        var checks = await ItemChecks(http, Route(http, "category"), Route(http, "id"),
            [(FolderOperation.Delete, ItemTarget.Container), (FolderOperation.Read, ItemTarget.Subtree)]);
        if (checks.Count == 0 || context.Arguments.OfType<MoveArchiveItemRequest>().FirstOrDefault() is not { } request) return checks;
        return [.. checks, Destination(http, request.DestinationCategory, request.DestinationFolderId)];
    };

    public static AccessRule BatchMove() => async context =>
    {
        var http = context.HttpContext;
        if (context.Arguments.OfType<BatchMoveArchiveItemsRequest>().FirstOrDefault() is not { } request) return [];
        var list = new List<AccessRequirement>();
        foreach (var id in request.ItemIds ?? [])
        {
            list.AddRange(await ItemChecks(http, Route(http, "category"), id,
                [(FolderOperation.Delete, ItemTarget.Container), (FolderOperation.Read, ItemTarget.Subtree)]));
        }

        list.Add(Destination(http, request.DestinationCategory, request.DestinationFolderId));
        return list;
    };

    public static AccessRule BatchTrash() => async context =>
    {
        var http = context.HttpContext;
        if (context.Arguments.OfType<BatchMoveToTrashArchiveItemsRequest>().FirstOrDefault() is not { } request) return [];
        var list = new List<AccessRequirement>();
        foreach (var id in (request.ItemIds ?? []).Distinct(StringComparer.Ordinal))
        {
            list.AddRange(await ItemChecks(http, Route(http, "category"), id,
                [(FolderOperation.Delete, ItemTarget.Container), (FolderOperation.Read, ItemTarget.Subtree)]));
        }

        return list;
    };

    /// <summary>Emptying a category (Trash) deletes everything in it, so the root needs Delete and nothing below may be hidden.</summary>
    public static AccessRule EmptyCategory() => async context =>
    {
        var http = context.HttpContext;
        var root = ResolveFolderOrRoot(http, Route(http, "category"), null);
        if (root?.Self is not { } location) return [new AccessRequirement(FolderOperation.Delete, null)];
        return [new AccessRequirement(FolderOperation.Delete, location), .. await SubtreeReadsAsync(http, location)];
    };

    /// <summary>Image crops are written to the category's <c>cuts</c> subfolder.</summary>
    public static AccessRule Crop() => async context =>
    {
        var http = context.HttpContext;
        var category = Route(http, "category");
        var reads = await ItemChecks(http, category, Route(http, "id"), [(FolderOperation.Read, ItemTarget.Self)]);
        if (reads.Count == 0) return reads;
        var root = ResolveFolderOrRoot(http, category, null)?.Self;
        return [.. reads, new AccessRequirement(FolderOperation.Create, root?.Child(CutsSubfolder))];
    };

    public static AccessRule LibraryVideo(params FolderOperation[] operations) => context =>
    {
        var http = context.HttpContext;
        var library = http.RequestServices.GetRequiredService<IVideoLibraryService>();
        if (!library.TryResolve(Route(http, "id"), out var entry) || entry is null) return Empty;
        var location = http.RequestServices.GetRequiredService<FolderLocator>().LocateContainer(entry.PhysicalPath);
        return ValueTask.FromResult<IReadOnlyList<AccessRequirement>>(operations.Select(operation => new AccessRequirement(operation, location)).ToList());
    };

    /// <summary>Cuts and compositions are flat roots: the whole root is the folder.</summary>
    public static AccessRule Root(string rootKey, params FolderOperation[] operations) => context =>
        ValueTask.FromResult<IReadOnlyList<AccessRequirement>>(operations.Select(operation => new AccessRequirement(operation, new FolderLocation(rootKey, string.Empty))).ToList());

    public static AccessRule All(params AccessRule[] rules) => async context =>
    {
        var list = new List<AccessRequirement>();
        foreach (var rule in rules) list.AddRange(await rule(context));
        return list;
    };

    private static AccessRequirement Destination(HttpContext http, string? category, string? folderId)
    {
        var resolved = ResolveFolderOrRoot(http, category ?? string.Empty, folderId);
        return new AccessRequirement(FolderOperation.Create, resolved?.Self);
    }

    private static async ValueTask<IReadOnlyList<AccessRequirement>> ItemChecks(
        HttpContext http, string category, string id, (FolderOperation Operation, ItemTarget Target)[] checks)
    {
        var resolved = ResolveAny(http, category, id);
        if (resolved is null) return [];

        var list = new List<AccessRequirement>();
        foreach (var (operation, target) in checks)
        {
            switch (target)
            {
                case ItemTarget.Self:
                    list.Add(new AccessRequirement(operation, resolved.Self));
                    break;
                case ItemTarget.Container:
                    list.Add(new AccessRequirement(operation, resolved.Container));
                    break;
                case ItemTarget.Subtree when resolved is { IsFolder: true, Self: { } self }:
                    list.AddRange(await SubtreeReadsAsync(http, self));
                    break;
            }
        }

        return list;
    }

    private static async Task<List<AccessRequirement>> SubtreeReadsAsync(HttpContext http, FolderLocation folder)
    {
        var tree = await http.RequestServices.GetRequiredService<FolderAccessService>().GetTreeAsync(http.RequestAborted);
        return tree.All
            .Where(row => row.Location != folder && row.Location.IsWithin(folder))
            .Select(row => new AccessRequirement(FolderOperation.Read, row.Location, ForbidWhenUnreadable: true))
            .ToList();
    }

    private static Resolved? ResolveFolderOrRoot(HttpContext http, string category, string? folderId)
    {
        var locator = http.RequestServices.GetRequiredService<FolderLocator>();
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return ArchiveCategory.TryGet(category, out var known) && known is not null
                ? new Resolved(locator.LocateRoot(known.Key), null, true)
                : null;
        }

        return ResolveAny(http, category, folderId);
    }

    /// <summary>Resolves an opaque archive ID (including a category root's ID) to the folders it touches; null when it does not resolve.</summary>
    private static Resolved? ResolveAny(HttpContext http, string category, string id)
    {
        var archive = http.RequestServices.GetRequiredService<IArchiveService>();
        var locator = http.RequestServices.GetRequiredService<FolderLocator>();
        if (!ArchiveCategory.TryGet(category, out var known) || known is null) return null;

        if (archive.TryResolveItem(known.Key, id, out var item) && item is not null)
        {
            var container = locator.LocateContainer(item.PhysicalPath);
            return item.Kind == ArchiveItemKind.Folder
                ? new Resolved(locator.LocateDirectory(item.PhysicalPath), container, true)
                : new Resolved(container, container, false);
        }

        var rootPath = archive.GetCategoryRootPath(known.Key);
        return string.Equals(archive.ComputeItemId(known.Key, rootPath), id, StringComparison.Ordinal)
            ? new Resolved(locator.LocateRoot(known.Key), null, true)
            : null;
    }

    private static string Route(HttpContext http, string name) => http.GetRouteValue(name)?.ToString() ?? string.Empty;

    private static ValueTask<IReadOnlyList<AccessRequirement>> Single(AccessRequirement? requirement) =>
        ValueTask.FromResult<IReadOnlyList<AccessRequirement>>(requirement is { } value ? [value] : []);

    private static ValueTask<IReadOnlyList<AccessRequirement>> Empty => ValueTask.FromResult<IReadOnlyList<AccessRequirement>>([]);
}
