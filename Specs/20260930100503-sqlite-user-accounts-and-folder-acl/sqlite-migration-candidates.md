# SQLite Migration Candidates

Inventory of every place the app persists data in local files, browser storage, or
in-memory state (outside the actual media files themselves) that could be moved to a
SQLite database. Generated 2026-09-29 by scanning `WebApp/WebApp`,
`WebApp/WebApp.Client`, and `WebApp.Tests`.

Out of scope / not listed below: `ThumbnailCache`, `HoverPreviewCache`,
`SubtitleCache`, `AudioTrackCache`, `FolderThumbnailProcessor` — these persist real
derivative media artifacts (thumbnails, previews, subtitles, audio tracks), not
bookkeeping metadata, and per the repo's read-only-source-mount design should stay as
filesystem caches.

## 1. Server-side file-based bookkeeping (highest value)

All use a `SemaphoreSlim` + write-to-`.tmp`-then-`File.Move` pattern, and most key
records by a SHA-256 hash of `category:itemId:size:lastWriteTimeUtc.Ticks` — which
means **renaming, re-copying, or re-transcoding a source file silently orphans its
record**.

| Data | Service | File | Notes |
|---|---|---|---|
| EPUB/comic highlights | `EpubHighlightService.cs:12,73,89-92` | `{ArchiveRoot}/Books/Notes/pereneArchiveBookHighlights.json` | `Dictionary<bookKey, List<BookHighlightDto>>`; fragile size/mtime key |
| EPUB notes | `EpubNoteService.cs:10,15,45-102` | `{ArchiveRoot}/Books/Notes/pereneArchiveBookNotes.txt` | **Free-text** Kindle-clippings-style file; delete does full read → split on `==========` → filter → rewrite whole file. Most fragile item in the repo — a note containing that delimiter or a marker-like line can corrupt parsing. |
| EPUB reading progress | `EpubProgressService.cs:12,101-108` | `{ArchiveRoot}/Books/Notes/pereneArchiveBookProgress.json` | `Dictionary<bookKey, ProgressRecord(ChapterId, WordOffset, ScrollFraction)>`; same fragile key |
| Comic (CBZ) reading progress | `ComicProgressService.cs:12,79-85` | `{ArchiveRoot}/Books/Notes/pereneArchiveComicProgress.json` | `Dictionary<bookKey, ProgressRecord(PageIndex)>`; same fragile key |
| EPUB reader theme/typography | `EpubReaderThemeService.cs:11,33-67,80,91-92` | `{ArchiveRoot}/Books/Notes/pereneArchiveReaderThemes.json` | Global font/size/line-height/color prefs + named custom theme presets |
| Archive favorites | `ArchiveFavoritesService.cs:10,15,22,47,55-56` | `{ArchiveRoot}/Dashboard/pereneArchiveFavorites.json` | `List<FavoriteRecord(Category, ItemId)>`; every read does an O(n) filesystem-resolve scan to garbage-collect stale entries |
| Custom dashboard storage views | `CustomStorageViewService.cs:11,34-122,208` | `{ArchiveRoot}/Dashboard/pereneArchiveCustomStorageViews.json` | `List<CustomStorageViewRecord(...)>`, whole file rewritten per add/remove/update |
| Resumable archive uploads | `ArchiveUploadService.cs:311-354` | `{workspace}/{id}.json` + `{id}.part` | Genuine restart-survival job bookkeeping; recovery works by scanning the directory for `*.json` (`SafeEnumerateMetaFiles`). The `.part` binary payload should stay a real file; only the metadata belongs in a DB. |

**Why SQLite helps:** three separate JSON files (highlights, progress, comic-progress)
all key off the same fragile size/mtime hash — a shared `MediaItems` table with a
stable ID would fix all three at once, and real transactions replace the hand-rolled
semaphore+atomic-rename dance.

## 2. Filesystem-scan-based naming/counters (highest correctness risk)

All derive "the next number" by listing existing output files and parsing a numeric
suffix out of each filename — race-prone (TOCTOU) under concurrent requests and
degrades as folders grow.

| Service | Location | Pattern |
|---|---|---|
| `CutNamingService` | `CutNamingService.cs:11-21,34-43` | Enumerate `*.mp4` → parse counters → `Max()+1` → `"{prefix} {next:0000}.mp4"` |
| `CompositionNamingService` | `CompositionNamingService.cs:10-20` | Same, `"{prefix} Composition {next:0000}.mp4"` |
| `ImageCropNamingService` | `ImageCropNamingService.cs:7-17` | Same pattern, generic extension |
| `VideoConversionNamingService` | `VideoConversionServices.cs:99` | Loop `n=1..` checking `File.Exists` until free |

**Why SQLite helps:** a `NamingCounters(Prefix, LastNumber)` table updated in a single
atomic transaction (`UPDATE ... RETURNING` / `INSERT ... ON CONFLICT`) removes the
directory scan and the race condition entirely.

## 3. In-memory-only job/state stores (lost on restart)

All `AddSingleton` in `WebApp/WebApp/Program.cs` unless noted.

| Store | Location | Registered | Holds |
|---|---|---|---|
| `ICompositionJobStatusStore` | `CompositionJobStatusStore.cs:9-10` | `Program.cs:134` | `ConcurrentDictionary` + `ConcurrentQueue` of video-composition job status |
| `IArchiveMutationJobStatusStore` | `ArchiveMutationJobStatusStore.cs:9-10` | `Program.cs:154` | Same shape, for archive move/delete/trash-empty progress |
| `IVideoConversionJobStatusStore` | `VideoConversionServices.cs:86-98` | `Program.cs:145` | Same shape + a `_runGate` lock; tracks pause/resume/stop/progress for ffmpeg conversions |
| `IActiveClientTracker` | `ActiveClientTracker.cs:9` | `Program.cs:164` | `ConcurrentDictionary<clientId, DateTime>`, 5-min sliding window — **intentionally ephemeral, not a migration candidate** |
| `PersistentPlayerState` | `WebApp.Client/Services/PersistentPlayerState.cs` (scoped, `Client/Program.cs:9`) | — | Current video/music item, playlist, `PlaylistPlayerPreferencesState` (volume/mute/rate/saturation/VR-POV/subtitles/fill-tab/last playback time) |
| `MediaPlayerState` | `WebApp.Client/Models/MediaPlayerState.cs`, instantiated in `Player.razor:169` | not DI-registered | Volume/mute/rate, A/B loop markers, subtitle toggle, audio track — component-lifetime only |

**Why SQLite helps:** these are long-running background jobs (compositions, archive
bulk moves, video conversions can take minutes) whose status currently vanishes
silently if the app restarts mid-job. A durable `Jobs` table lets the UI recover
status after a restart/redeploy.

## 4. Browser storage (localStorage)

Only two call sites in the entire client codebase; no IndexedDB usage anywhere.

| Key | Location | Purpose |
|---|---|---|
| `perenearchive-theme` | `WebApp.Client/wwwroot/js/theme.js:4,14,37` | Dark/light theme choice |
| `archiveBrowser.sortOption` | `WebApp.Client/Components/ArchiveBrowser.razor:895,963,1014` | Selected archive sort option (called via raw `JS.InvokeAsync("localStorage.getItem"/"setItem", ...)`, not routed through a dedicated interop file) |

These are legitimately client/per-browser preferences today. Moving them to SQLite
only makes sense if cross-device sync of theme/sort-order becomes an actual goal.

## Priority summary

1. **Fix first (correctness risk today):** EPUB notes flat-file delimiter parsing
   (`EpubNoteService`), and the four scan-based naming services (`CutNamingService`,
   `CompositionNamingService`, `ImageCropNamingService`,
   `VideoConversionNamingService`) — real TOCTOU race and delimiter-collision bugs.
2. **High value for durability:** `VideoConversionJobStatusStore`,
   `CompositionJobStatusStore`, `ArchiveMutationJobStatusStore` — long-running job
   state that silently disappears on restart.
3. **Medium value (concurrency-safety, not durability):** the `Books/Notes/*.json`
   and `Dashboard/*.json` files — already survive restarts as files, but each
   hand-rolls its own locking/atomic-rename instead of using real transactions; the
   size/mtime identity key is fragile across renames/re-encodes.
4. **Low priority / optional:** `localStorage` keys (theme, sort option) and
   `PersistentPlayerState`/`MediaPlayerState` in-memory playback preferences — these
   are legitimately session/client-scoped unless cross-device sync becomes a goal.
5. **Out of scope:** thumbnail/hover-preview/subtitle/audio-track caches — regenerable
   derivative media, not bookkeeping metadata.
