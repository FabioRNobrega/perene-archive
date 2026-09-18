using System.Diagnostics;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class VideoConversionProcessControllerTests
{
    [Fact]
    public void Requests_for_an_unregistered_job_are_rejected()
    {
        var controller = new VideoConversionProcessController(new FakeSignal());
        using var shutdown = new CancellationTokenSource();
        _ = controller.Begin("active", shutdown.Token);

        Assert.False(controller.Pause("other"));
        Assert.False(controller.Resume("active"));
        Assert.False(controller.Stop("other"));
        Assert.True(controller.CanPublish("active"));
        controller.Complete("active");
    }

    private sealed class FakeSignal : IVideoConversionProcessSignal
    {
        public bool Suspend(Process process) => true;
        public bool Resume(Process process) => true;
        public void Terminate(Process process) { }
    }
}
