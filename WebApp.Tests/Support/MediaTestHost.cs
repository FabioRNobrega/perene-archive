using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using WebApp.Authorization;
using WebApp.Data;
using WebApp.Data.Entities;
using WebApp.Services;

namespace WebApp.Tests;

/// <summary>
/// A real-Identity host with a populated archive, for exercising the per-user media services as different signed-in users.
/// Services are resolved from a fresh scope with the caller's principal installed only for the duration of the call.
/// </summary>
internal sealed class MediaTestHost : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    public MediaTestHost(Dictionary<string, string?>? settings = null)
    {
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
            Directory.CreateDirectory(Path.Combine(_root.Path, "archive", folder));
        Factory = new AccountFactory(_root.Path, settings);
    }

    public AccountFactory Factory { get; }
    public string ArchivePath => Factory.ArchivePath;
    public string BooksPath => Path.Combine(ArchivePath, "Books");
    public string StatePath => Factory.StatePath;
    public string RootPath => _root.Path;

    public async Task<string> AddUserAsync(string name, bool admin = false)
    {
        await IdentityTestHost.CreateMemberAsync(Factory, name, "password1", admin: admin);
        return (await IdentityTestHost.FindUserAsync(Factory, name))!.Id;
    }

    public async Task<T> AsAsync<T>(string userId, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = userId.Length == 0
            ? new DefaultHttpContext()
            : new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test")) };
        try
        {
            return await action(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    public Task AsAsync(string userId, Func<IServiceProvider, Task> action) =>
        AsAsync<int>(userId, async provider =>
        {
            await action(provider);
            return 0;
        });

    /// <summary>Runs against the database and server services with no signed-in user (system context).</summary>
    public async Task<T> WithDbAsync<T>(Func<IServiceProvider, AppDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<IServiceProvider, AppDbContext, Task> action) =>
        WithDbAsync<int>(async (provider, db) =>
        {
            await action(provider, db);
            return 0;
        });

    public string ItemId(IServiceProvider provider, string category, string name) =>
        provider.GetRequiredService<IArchiveService>().List(category, null).Items.Single(item => item.Name == name).Id;

    /// <summary>Hides the Books root from everyone without an explicit grant, taking effect immediately.</summary>
    public Task SetBooksPrivateAsync(bool isPrivate) =>
        WithDbAsync(async (provider, db) =>
        {
            var books = await db.Folders.SingleAsync(folder => folder.RootKey == "books" && folder.RelativePath == "");
            books.AccessMode = isPrivate ? FolderAccessMode.Private : FolderAccessMode.Shared;
            await db.SaveChangesAsync();
            await provider.GetRequiredService<AuthzVersionStore>().BumpGlobalAsync();
        });

    public void Dispose()
    {
        Factory.Dispose();
        _root.Dispose();
    }
}
