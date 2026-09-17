using Docker.DotNet;
using Docker.DotNet.Models;

namespace WebApp.Services;

internal sealed class DockerEngineApiClient : IDockerApiClient, IDisposable
{
    internal const string DockerSocketUri = "unix:///var/run/docker.sock";

    private readonly Lazy<DockerClient> _client = new(() =>
        new DockerClientConfiguration(new Uri(DockerSocketUri)).CreateClient());

    public async Task<IReadOnlyList<DockerContainerSample>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var client = _client.Value;
        var containers = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = false }, cancellationToken);

        var results = new List<DockerContainerSample>();
        foreach (var container in containers)
        {
            var sample = await ReadContainerSampleAsync(client, container, cancellationToken);
            if (sample is not null)
            {
                results.Add(sample.Value);
            }
        }

        return results;
    }

    private static async Task<DockerContainerSample?> ReadContainerSampleAsync(
        DockerClient client, ContainerListResponse container, CancellationToken cancellationToken)
    {
        try
        {
            var inspect = await client.Containers.InspectContainerAsync(container.ID, cancellationToken);

            ContainerStatsResponse? stats = null;
            var progress = new Progress<ContainerStatsResponse>(response => stats = response);
            await client.Containers.GetContainerStatsAsync(
                container.ID,
                new ContainerStatsParameters { Stream = false },
                progress,
                cancellationToken);

            var name = container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..Math.Min(12, container.ID.Length)];
            var cpuPercent = stats is null ? 0 : ComputeCpuPercent(stats);
            var memoryUsedBytes = stats is null ? 0 : (long)stats.MemoryStats.Usage;
            var uptimeSeconds = DateTime.TryParse(inspect.State.StartedAt, out var startedAt)
                ? Math.Max(0, (DateTime.UtcNow - startedAt.ToUniversalTime()).TotalSeconds)
                : 0;

            return new DockerContainerSample(name, cpuPercent, memoryUsedBytes, uptimeSeconds, (int)inspect.RestartCount);
        }
        catch (Exception exception) when (exception is DockerApiException or TimeoutException or OperationCanceledException)
        {
            return null;
        }
    }

    private static double ComputeCpuPercent(ContainerStatsResponse stats)
    {
        var cpuDelta = (double)stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage;
        var systemDelta = (double)stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage;
        if (cpuDelta <= 0 || systemDelta <= 0)
        {
            return 0;
        }

        var onlineCpus = stats.CPUStats.OnlineCPUs > 0
            ? stats.CPUStats.OnlineCPUs
            : (uint)Math.Max(1, stats.CPUStats.CPUUsage.PercpuUsage?.Count ?? 1);

        return Math.Clamp(cpuDelta / systemDelta * onlineCpus * 100.0, 0, 100 * onlineCpus);
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }
}
