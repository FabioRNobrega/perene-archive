using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WebApp.Services;

internal interface IVideoConversionProcessSignal
{
    bool Suspend(Process process);
    bool Resume(Process process);
    void Terminate(Process process);
}

internal sealed class PosixVideoConversionProcessSignal : IVideoConversionProcessSignal
{
    private const int SigStop = 19;
    private const int SigCont = 18;
    private const int SigTerm = 15;
    public bool Suspend(Process process) => kill(process.Id, SigStop) == 0;
    public bool Resume(Process process) => kill(process.Id, SigCont) == 0;
    public void Terminate(Process process) { if (!process.HasExited) _ = kill(process.Id, SigTerm); }
    [DllImport("libc", SetLastError = true)] private static extern int kill(int pid, int sig);
}

internal interface IVideoConversionProcessController
{
    CancellationToken Begin(string jobId, CancellationToken shutdownToken);
    bool RegisterProcess(string jobId, Process process);
    void UnregisterProcess(string jobId, Process process);
    bool Pause(string jobId);
    bool Resume(string jobId);
    bool Stop(string jobId);
    bool CanPublish(string jobId);
    void Complete(string jobId);
}

internal sealed class VideoConversionProcessController(IVideoConversionProcessSignal signal) : IVideoConversionProcessController
{
    private sealed class JobControl
    {
        public Process? Process;
        public CancellationTokenSource? Cancellation;
        public bool Paused;
        public bool Stopped;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, JobControl> _jobs = new();

    public CancellationToken Begin(string jobId, CancellationToken shutdownToken)
    {
        lock (_gate)
        {
            var control = new JobControl { Cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken) };
            if (!_jobs.TryAdd(jobId, control)) { control.Cancellation.Dispose(); throw new InvalidOperationException("This conversion job is already active."); }
            return control.Cancellation.Token;
        }
    }
    public bool RegisterProcess(string jobId, Process process) { lock (_gate) { if (!_jobs.TryGetValue(jobId, out var c) || c.Stopped) return false; c.Process = process; return true; } }
    public void UnregisterProcess(string jobId, Process process) { lock (_gate) { if (_jobs.TryGetValue(jobId, out var c) && ReferenceEquals(c.Process, process)) c.Process = null; } }
    public bool Pause(string jobId) { lock (_gate) { if (!_jobs.TryGetValue(jobId, out var c) || c.Process is null || c.Paused || c.Stopped || c.Process.HasExited || !signal.Suspend(c.Process)) return false; c.Paused = true; return true; } }
    public bool Resume(string jobId) { lock (_gate) { if (!_jobs.TryGetValue(jobId, out var c) || c.Process is null || !c.Paused || c.Stopped || c.Process.HasExited || !signal.Resume(c.Process)) return false; c.Paused = false; return true; } }
    public bool Stop(string jobId) { lock (_gate) { if (!_jobs.TryGetValue(jobId, out var c) || c.Stopped) return false; c.Stopped = true; c.Paused = false; c.Cancellation?.Cancel(); if (c.Process is { HasExited: false } process) signal.Terminate(process); return true; } }
    public bool CanPublish(string jobId) { lock (_gate) return _jobs.TryGetValue(jobId, out var c) && !c.Stopped; }
    public void Complete(string jobId) { lock (_gate) { if (_jobs.Remove(jobId, out var c)) c.Cancellation?.Dispose(); } }
}
