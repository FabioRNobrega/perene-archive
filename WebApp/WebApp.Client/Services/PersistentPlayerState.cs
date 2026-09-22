using WebApp.Client.Models;

namespace WebApp.Client.Services;

public sealed class PersistentPlayerState
{
    public const string VideoStreamBasePath = "api/videos";
    public const string CutStreamBasePath = "api/cuts";
    public const string CompositionStreamBasePath = "api/compositions";
    public event Action? StateChanged;
    public event Action? CutQueued;

    public VideoItemDto? Selected { get; private set; }
    public PersistentMediaKind MediaKind { get; private set; }
    public IReadOnlyList<PlaylistTrackDto> Playlist { get; private set; } = [];
    public string? AlbumCoverUrl { get; private set; }
    public string StreamBasePath { get; private set; } = VideoStreamBasePath;
    public bool HasSelection => Selected is not null;
    public bool CanSaveCut => MediaKind == PersistentMediaKind.Video &&
        string.Equals(StreamBasePath, VideoStreamBasePath, StringComparison.Ordinal);
    public string? SelectedId => Selected?.Id;
    public bool IsMusic => MediaKind == PersistentMediaKind.Music;
    public bool HasPlaylist => Playlist.Count > 0;
    public bool PlaylistViewActive { get; private set; }
    public string? PlaylistFolderName { get; private set; }
    public string? PlaylistCategory { get; private set; }
    public string? PlaylistFolderId { get; private set; }
    public bool CanReturnToPlaylist => HasPlaylist &&
        !string.IsNullOrWhiteSpace(PlaylistCategory) &&
        !string.IsNullOrWhiteSpace(PlaylistFolderId);
    public double LastKnownTime { get; private set; }
    public bool WasPlaying { get; private set; }
    public bool CanSelectPreviousTrack => HasPlaylist && CurrentPlaylistIndex > 0;
    public bool CanSelectNextTrack => HasPlaylist &&
        CurrentPlaylistIndex >= 0 &&
        CurrentPlaylistIndex < Playlist.Count - 1;

    private int CurrentPlaylistIndex => SelectedId is null
        ? -1
        : Playlist.ToList().FindIndex(track => string.Equals(track.Id, SelectedId, StringComparison.Ordinal));

    public void SelectVideo(VideoItemDto video) => Select(video, VideoStreamBasePath);

    public void SelectCut(VideoItemDto cut) => Select(cut, CutStreamBasePath);

    public void SelectComposition(VideoItemDto composition) => Select(composition, CompositionStreamBasePath);

    public void SelectArchiveVideo(string category, ArchiveItemDto item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(item);

        Select(
            new VideoItemDto(
                item.Id,
                item.Name,
                item.Extension ?? string.Empty,
                item.SizeBytes ?? 0,
                ThumbnailState.Unavailable,
                null,
                HoverPreviewState.Unavailable,
                null,
                item.SubtitleState,
                item.SubtitleUrl,
                null,
                null,
                null,
                item.AudioTracks),
            $"api/archive/{Uri.EscapeDataString(category)}/items");
    }

    public void SelectMusic(ArchiveItemDto item, IReadOnlyList<ArchiveItemDto> currentFolderItems)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(currentFolderItems);
        if (!item.IsMusic || string.IsNullOrWhiteSpace(item.AudioUrl))
        {
            throw new ArgumentException("A playable music item is required.", nameof(item));
        }

        var playlist = currentFolderItems
            .Where(track => track.IsMusic && !string.IsNullOrWhiteSpace(track.AudioUrl))
            .Select(ToMusicPlaylistTrack)
            .ToList();
        if (!playlist.Any(track => string.Equals(track.Id, item.Id, StringComparison.Ordinal)))
        {
            playlist.Add(ToMusicPlaylistTrack(item));
        }

        PlaylistCategory = null;
        PlaylistFolderId = null;
        PlaylistFolderName = null;
        SelectPlaylistTrack(ToMusicPlaylistTrack(item), playlist);
    }

    public void EnterPlaylistView(string category, string folderId, string folderName, IReadOnlyList<ArchiveItemDto> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderName);
        ArgumentNullException.ThrowIfNull(items);

        var playlist = items
            .Where(item => item.IsVideo || (item.IsMusic && !string.IsNullOrWhiteSpace(item.AudioUrl)))
            .Select(item => ToPlaylistTrack(category, item))
            .ToList();

        PlaylistViewActive = true;
        PlaylistFolderName = folderName;
        PlaylistCategory = category;
        PlaylistFolderId = folderId;

        if (playlist.Count == 0)
        {
            Selected = null;
            Playlist = [];
            AlbumCoverUrl = null;
            LastKnownTime = 0;
            WasPlaying = false;
            NotifyStateChanged();
            return;
        }

        var resumingSameSelection = Selected is not null &&
            playlist.Any(track => string.Equals(track.Id, Selected.Id, StringComparison.Ordinal));

        if (resumingSameSelection)
        {
            Playlist = playlist;
            NotifyStateChanged();
            return;
        }

        SelectPlaylistTrack(playlist[0], playlist);
    }

    public void ExitPlaylistView()
    {
        if (!PlaylistViewActive)
        {
            return;
        }

        PlaylistViewActive = false;
        NotifyStateChanged();
    }

    public bool SelectPlaylistItem(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var index = Playlist.ToList().FindIndex(track => string.Equals(track.Id, id, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        SelectPlaylistTrack(Playlist[index], Playlist);
        return true;
    }

    public bool SelectPreviousTrack()
    {
        if (!CanSelectPreviousTrack)
        {
            return false;
        }

        SelectPlaylistTrack(Playlist[CurrentPlaylistIndex - 1], Playlist);
        return true;
    }

    public bool SelectNextTrack()
    {
        if (!CanSelectNextTrack)
        {
            return false;
        }

        SelectPlaylistTrack(Playlist[CurrentPlaylistIndex + 1], Playlist);
        return true;
    }

    public void Select(VideoItemDto item, string streamBasePath)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(streamBasePath))
        {
            throw new ArgumentException("A stream base path is required.", nameof(streamBasePath));
        }

        Selected = item;
        StreamBasePath = streamBasePath;
        MediaKind = PersistentMediaKind.Video;
        Playlist = [];
        AlbumCoverUrl = null;
        PlaylistCategory = null;
        PlaylistFolderId = null;
        PlaylistFolderName = null;
        LastKnownTime = 0;
        WasPlaying = false;
        NotifyStateChanged();
    }

    public void UpdateSelected(VideoItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!string.Equals(Selected?.Id, item.Id, StringComparison.Ordinal))
        {
            return;
        }

        Selected = item;
        NotifyStateChanged();
    }

    public void Clear()
    {
        Selected = null;
        StreamBasePath = VideoStreamBasePath;
        MediaKind = PersistentMediaKind.Video;
        Playlist = [];
        AlbumCoverUrl = null;
        PlaylistViewActive = false;
        PlaylistCategory = null;
        PlaylistFolderId = null;
        PlaylistFolderName = null;
        LastKnownTime = 0;
        WasPlaying = false;
        NotifyStateChanged();
    }

    public void NotifyCutQueued() => CutQueued?.Invoke();

    public void UpdatePlaybackProgress(double currentTime, bool isPlaying)
    {
        if (Selected is null)
        {
            return;
        }

        LastKnownTime = double.IsFinite(currentTime) ? Math.Max(0, currentTime) : 0;
        WasPlaying = isPlaying;
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();

    private void SelectPlaylistTrack(PlaylistTrackDto track, IReadOnlyList<PlaylistTrackDto> playlist)
    {
        Selected = new VideoItemDto(
            track.Id,
            track.Name,
            track.Extension,
            track.SizeBytes,
            ThumbnailState.Unavailable,
            null,
            HoverPreviewState.Unavailable,
            null,
            SubtitleState.Unavailable,
            null,
            track.DurationSeconds,
            null,
            null);
        StreamBasePath = track.StreamBasePath;
        MediaKind = track.MediaKind;
        Playlist = playlist.ToList();
        AlbumCoverUrl = track.AlbumCoverUrl;
        LastKnownTime = 0;
        WasPlaying = false;
        NotifyStateChanged();
    }

    private static PlaylistTrackDto ToMusicPlaylistTrack(ArchiveItemDto item)
    {
        var streamBasePath = GetStreamBasePath(item.AudioUrl);
        return new PlaylistTrackDto(
            item.Id,
            item.Name,
            item.Extension ?? string.Empty,
            item.SizeBytes ?? 0,
            PersistentMediaKind.Music,
            item.AudioUrl ?? string.Empty,
            streamBasePath,
            null,
            item.AlbumCoverUrl,
            item.DurationSeconds);
    }

    private static PlaylistTrackDto ToPlaylistTrack(string category, ArchiveItemDto item) =>
        item.IsMusic
            ? ToMusicPlaylistTrack(item)
            : new PlaylistTrackDto(
                item.Id,
                item.Name,
                item.Extension ?? string.Empty,
                item.SizeBytes ?? 0,
                PersistentMediaKind.Video,
                $"api/archive/{Uri.EscapeDataString(category)}/items/{Uri.EscapeDataString(item.Id)}/stream",
                $"api/archive/{Uri.EscapeDataString(category)}/items",
                item.ThumbnailUrl,
                null,
                item.DurationSeconds);

    private static string GetStreamBasePath(string? audioUrl)
    {
        const string audioSuffix = "/audio";
        if (string.IsNullOrWhiteSpace(audioUrl))
        {
            return VideoStreamBasePath;
        }

        var path = audioUrl.StartsWith("/", StringComparison.Ordinal) ? audioUrl[1..] : audioUrl;
        return path.EndsWith(audioSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^audioSuffix.Length]
            : path;
    }
}
