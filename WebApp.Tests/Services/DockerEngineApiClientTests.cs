using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class DockerEngineApiClientTests
{
    [Fact]
    public void Uses_the_direct_read_only_socket_mount_uri()
    {
        Assert.Equal("unix:///var/run/docker.sock", DockerEngineApiClient.DockerSocketUri);
    }
}
