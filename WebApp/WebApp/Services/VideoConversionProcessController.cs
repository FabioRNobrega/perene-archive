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
    private readonly object _gate = new();
    private string? _jobId;
    private Process? _process;
    private CancellationTokenSource? _cancellation;
    private bool _paused;
    private bool _stopped;

    public CancellationToken Begin(string jobId, CancellationToken shutdownToken)
    {
        lock (_gate)
        {
            if (_jobId is not null) throw new InvalidOperationException("A conversion process is already active.");
            _jobId = jobId;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            return _cancellation.Token;
        }
    }
    public bool RegisterProcess(string jobId, Process process) { lock (_gate) { if (_jobId != jobId || _stopped) return false; _process = process; return true; } }
    public void UnregisterProcess(string jobId, Process process) { lock (_gate) { if (_jobId == jobId && ReferenceEquals(_process, process)) _process = null; } }
    public bool Pause(string jobId) { lock (_gate) { if (_jobId != jobId || _process is null || _paused || _stopped || _process.HasExited || !signal.Suspend(_process)) return false; _paused = true; return true; } }
    public bool Resume(string jobId) { lock (_gate) { if (_jobId != jobId || _process is null || !_paused || _stopped || _process.HasExited || !signal.Resume(_process)) return false; _paused = false; return true; } }
    public bool Stop(string jobId) { lock (_gate) { if (_jobId != jobId || _stopped) return false; _stopped = true; _paused = false; _cancellation?.Cancel(); if (_process is { HasExited: false } process) signal.Terminate(process); return true; } }
    public bool CanPublish(string jobId) { lock (_gate) return _jobId == jobId && !_stopped; }
    public void Complete(string jobId) { lock (_gate) { if (_jobId != jobId) return; _cancellation?.Dispose(); _cancellation = null; _process = null; _jobId = null; _paused = false; _stopped = false; } }
}
