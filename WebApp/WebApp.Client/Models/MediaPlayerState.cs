using System.Globalization;

namespace WebApp.Client.Models;

public sealed class MediaPlayerState
{
    private static readonly IReadOnlyList<double> Rates = Array.AsReadOnly([0.25, 0.5, 0.7, 0.8, 0.9, 1, 1.1, 1.2, 1.3, 1.5, 2]);

    public string? SelectionId { get; private set; }
    public bool IsPlaying { get; private set; }
    public double CurrentTime { get; private set; }
    public double? Duration { get; private set; }
    public double Volume { get; private set; } = 1;
    public bool IsMuted { get; private set; }
    public double PlaybackRate { get; private set; } = 1;
    public bool IsStandardLoop { get; private set; }
    public bool IsAbLoop { get; private set; }
    public bool IsSubtitlesEnabled { get; private set; } = true;
    public int? SelectedAudioTrackIndex { get; private set; }
    public double? MarkerA { get; private set; }
    public double? MarkerB { get; private set; }
    public string? ValidationMessage { get; private set; }

    public static IReadOnlyList<double> PlaybackRates => Rates;
    public bool HasDuration => Duration is > 0;
    public bool HasValidAbRange => MarkerA is not null && MarkerB is not null && MarkerA < MarkerB;

    public void Select(string? id)
    {
        if (string.Equals(SelectionId, id, StringComparison.Ordinal))
        {
            return;
        }

        SelectionId = id;
        IsPlaying = false;
        CurrentTime = 0;
        Duration = null;
        IsStandardLoop = false;
        IsAbLoop = false;
        IsSubtitlesEnabled = true;
        SelectedAudioTrackIndex = null;
        MarkerA = null;
        MarkerB = null;
        ValidationMessage = null;
    }

    public void Synchronize(MediaSnapshot snapshot)
    {
        Duration = NormalizeDuration(snapshot.Duration);
        CurrentTime = ClampTime(snapshot.CurrentTime);
        Volume = double.IsFinite(snapshot.Volume) ? Math.Clamp(snapshot.Volume, 0, 1) : Volume;
        IsMuted = snapshot.Muted;
        PlaybackRate = double.IsFinite(snapshot.PlaybackRate) && snapshot.PlaybackRate > 0
            ? snapshot.PlaybackRate
            : PlaybackRate;
        IsPlaying = !snapshot.Paused && !snapshot.Ended;
        IsStandardLoop = snapshot.Loop;

        if (IsStandardLoop)
        {
            IsAbLoop = false;
        }

        NormalizeMarkersForDuration();
    }

    public void PreviewTime(double time) => CurrentTime = ClampTime(time);

    public void SetStandardLoop(bool enabled)
    {
        IsStandardLoop = enabled;
        if (enabled)
        {
            IsAbLoop = false;
        }

        ValidationMessage = null;
    }

    public bool SetAbLoop(bool enabled)
    {
        if (enabled && !HasValidAbRange)
        {
            ValidationMessage = "Set point A and a later point B before enabling A/B loop.";
            return false;
        }

        IsAbLoop = enabled;
        if (enabled)
        {
            IsStandardLoop = false;
        }

        ValidationMessage = null;
        return true;
    }

    public void SetMarkerA()
    {
        if (!HasDuration)
        {
            return;
        }

        MarkerA = ClampTime(CurrentTime);
        if (MarkerB is not null && MarkerA >= MarkerB)
        {
            MarkerB = null;
            IsAbLoop = false;
        }

        ValidationMessage = null;
    }

    public bool SetMarkerB()
    {
        if (!HasDuration)
        {
            return false;
        }

        if (MarkerA is null)
        {
            ValidationMessage = "Set point A before setting point B.";
            return false;
        }

        var candidate = ClampTime(CurrentTime);
        if (candidate <= MarkerA)
        {
            ValidationMessage = "Point B must be after point A.";
            return false;
        }

        MarkerB = candidate;
        ValidationMessage = null;
        return true;
    }

    public void ClearAbLoop()
    {
        MarkerA = null;
        MarkerB = null;
        IsAbLoop = false;
        ValidationMessage = null;
    }

    public void SetSubtitlesEnabled(bool enabled) => IsSubtitlesEnabled = enabled;

    public void RestorePlaybackPreferences(double volume, bool isMuted, double playbackRate, bool isSubtitlesEnabled)
    {
        Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : Volume;
        IsMuted = isMuted;
        PlaybackRate = double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : PlaybackRate;
        IsSubtitlesEnabled = isSubtitlesEnabled;
    }

    public void SetSelectedAudioTrack(int index) => SelectedAudioTrackIndex = index;

    public bool TryMoveToAbStart(out double target)
    {
        if (!IsAbLoop || MarkerA is null || MarkerB is null || CurrentTime < MarkerB)
        {
            target = 0;
            return false;
        }

        target = MarkerA.Value;
        CurrentTime = target;
        return true;
    }

    public string FormatCurrentTime() => FormatTime(CurrentTime);
    public string FormatDuration() => Duration is null ? "--:--" : FormatTime(Duration.Value);
    public string FormatMarker(double? marker) => marker is null ? "Not set" : FormatTime(marker.Value);

    public static string FormatTime(double seconds)
    {
        var safeSeconds = double.IsFinite(seconds) ? Math.Max(0, seconds) : 0;
        var time = TimeSpan.FromSeconds(Math.Floor(safeSeconds));
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}");
    }

    private double ClampTime(double value)
    {
        var safeValue = double.IsFinite(value) ? Math.Max(0, value) : 0;
        return Duration is null ? safeValue : Math.Min(safeValue, Duration.Value);
    }

    private static double? NormalizeDuration(double? duration) =>
        duration is > 0 && double.IsFinite(duration.Value) ? duration : null;

    private void NormalizeMarkersForDuration()
    {
        if (Duration is null)
        {
            return;
        }

        if (MarkerA is not null)
        {
            MarkerA = Math.Clamp(MarkerA.Value, 0, Duration.Value);
        }

        if (MarkerB is not null)
        {
            MarkerB = Math.Clamp(MarkerB.Value, 0, Duration.Value);
        }

        if (MarkerA is not null && MarkerB is not null && MarkerA >= MarkerB)
        {
            MarkerB = null;
            IsAbLoop = false;
        }
    }
}

public sealed record MediaSnapshot(
    double CurrentTime,
    double? Duration,
    double Volume,
    bool Muted,
    double PlaybackRate,
    bool Loop,
    bool Paused,
    bool Ended);
