using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Endpoints;

public sealed class JobActivityEndpointTests : IDisposable
{
    private readonly MediaTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<HttpClient> SignedInAsync(string name)
    {
        var client = _host.Factory.CreateClient(IdentityTestHost.NoRedirect);
        await IdentityTestHost.SignInAsync(client, name, "password1");
        return client;
    }

    [Fact]
    public async Task Activity_list_requires_sign_in()
    {
        using var client = _host.Factory.CreateClient(IdentityTestHost.NoRedirect);

        using var response = await client.GetAsync("/api/dashboard/jobs/activity");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Members_see_their_own_cuts_compositions_and_moves_and_admins_see_everything()
    {
        var bob = await _host.AddUserAsync("bob");
        var carol = await _host.AddUserAsync("carol");
        await _host.AddUserAsync("boss", admin: true);
        var services = _host.Factory.Services;
        var entry = new VideoFileEntry("i", "/server-only/Beach Day.mp4", "x", "Beach Day.mp4", ".mp4", 1, DateTime.UtcNow);
        services.GetRequiredService<ICutJobRecorder>().Seed(new CutJob("bob-cut", entry, TimeSpan.Zero, TimeSpan.FromSeconds(3), bob));
        services.GetRequiredService<ICompositionJobStatusStore>().Seed("carol-comp", carol, "{\"sources\":[{},{}]}");
        services.GetRequiredService<IArchiveMutationJobStatusStore>().Seed(
            new ArchiveMutationJob("bob-move", ArchiveMutationKind.Move, "/server-only/a", "/server-only/b", false, 1, "a.mp4 to Pictures", null, bob));

        using var bobClient = await SignedInAsync("bob");
        var mine = (await bobClient.GetFromJsonAsync<List<JobSummaryDto>>("/api/dashboard/jobs/activity"))!;
        Assert.Equal(["bob-cut", "bob-move"], mine.Select(job => job.JobId).OrderBy(id => id));
        Assert.All(mine, job => Assert.Equal("Member", job.StartedBy));

        using var boss = await SignedInAsync("boss");
        var all = (await boss.GetFromJsonAsync<List<JobSummaryDto>>("/api/dashboard/jobs/activity"))!;
        Assert.Equal(["bob-cut", "bob-move", "carol-comp"], all.Select(job => job.JobId).OrderBy(id => id));
        Assert.DoesNotContain("server-only", System.Text.Json.JsonSerializer.Serialize(all));
    }
}
