using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Data.Entities;

namespace WebApp.Authorization;

/// <summary>
/// Reads and bumps the two authorization versions. Bumps use a single atomic UPDATE so they join the caller's open transaction:
/// the global version (folder/permission/policy/ownership changes) and the per-user <c>AuthzVersion</c> (user-scoped changes).
/// </summary>
public sealed class AuthzVersionStore(AppDbContext db)
{
    public async Task<long> GetGlobalAsync(CancellationToken cancellationToken = default) =>
        await db.AccessPolicies.AsNoTracking().Where(policy => policy.Id == AccessPolicy.SingletonId)
            .Select(policy => policy.Version).FirstOrDefaultAsync(cancellationToken);

    public Task BumpGlobalAsync(CancellationToken cancellationToken = default) =>
        db.AccessPolicies.Where(policy => policy.Id == AccessPolicy.SingletonId)
            .ExecuteUpdateAsync(set => set.SetProperty(policy => policy.Version, policy => policy.Version + 1), cancellationToken);

    public Task BumpUserAsync(string userId, CancellationToken cancellationToken = default) =>
        db.Users.Where(user => user.Id == userId)
            .ExecuteUpdateAsync(set => set.SetProperty(user => user.AuthzVersion, user => user.AuthzVersion + 1), cancellationToken);
}
