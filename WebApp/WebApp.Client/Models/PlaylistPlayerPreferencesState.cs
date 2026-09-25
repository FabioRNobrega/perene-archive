namespace WebApp.Client.Models;

public sealed class PlaylistPlayerPreferencesState
{
    public bool IsActive { get; private set; }
    public string? Category { get; private set; }
    public string? FolderId { get; private set; }
    public bool IsVrPovEnabled { get; private set; }
    public double Saturation { get; private set; } = SaturationState.Default;
    public double Volume { get; private set; } = 1;
    public bool IsMuted { get; private set; }
    public double PlaybackRate { get; private set; } = 1;
    public bool IsSubtitlesEnabled { get; private set; } = true;
    public bool IsFillTabActive { get; private set; }

    public void Begin(string category, string folderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        if (IsActive && string.Equals(Category, category, StringComparison.Ordinal) &&
            string.Equals(FolderId, folderId, StringComparison.Ordinal))
        {
            return;
        }

        Reset();
        IsActive = true;
        Category = category;
        FolderId = folderId;
    }

    public void Capture(bool isVrPovEnabled, double saturation, double volume, bool isMuted,
        double playbackRate, bool isSubtitlesEnabled, bool isFillTabActive)
    {
        if (!IsActive)
        {
            return;
        }

        IsVrPovEnabled = isVrPovEnabled;
        Saturation = double.IsFinite(saturation) ? Math.Clamp(saturation, 0, SaturationState.Max) : Saturation;
        IsSubtitlesEnabled = isSubtitlesEnabled;
        CaptureAudioPreferences(volume, isMuted, playbackRate, isFillTabActive);
    }

    public void CaptureAudioPreferences(double volume, bool isMuted, double playbackRate, bool isFillTabActive)
    {
        if (!IsActive)
        {
            return;
        }

        Volume = double.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : Volume;
        IsMuted = isMuted;
        PlaybackRate = double.IsFinite(playbackRate) && playbackRate > 0 ? playbackRate : PlaybackRate;
        IsFillTabActive = isFillTabActive;
    }

    public void Clear() => Reset();

    private void Reset()
    {
        IsActive = false;
        Category = null;
        FolderId = null;
        IsVrPovEnabled = false;
        Saturation = SaturationState.Default;
        Volume = 1;
        IsMuted = false;
        PlaybackRate = 1;
        IsSubtitlesEnabled = true;
        IsFillTabActive = false;
    }
}
