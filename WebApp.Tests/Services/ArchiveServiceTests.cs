using Microsoft.Extensions.Options;
using WebApp.Client.Models;
using WebApp.Configuration;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveServiceTests
{
    [Fact]
    public void List_maps_photos_to_pictures_and_orders_folders_first()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Zeta"));
        awaitFile(Path.Combine(root.Path, "Pictures", "alpha.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Alpha Folder"));

        var listing = CreateService(root.Path).List("photos", null);

        Assert.Equal("Photos", listing.Category.DisplayName);
        Assert.Equal(["Alpha Folder", "Zeta", "alpha.txt"], listing.Items.Select(item => item.Name));
        Assert.Equal(ArchiveItemKind.Folder, listing.Items[0].Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("CON")]
    public void CreateFolder_rejects_unsafe_names(string name)
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveValidationException>(() => service.CreateFolder("documents", null, name));
    }

    [Fact]
    public void CreateFolder_creates_inside_current_category()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        var listing = service.CreateFolder("documents", null, "Invoices");

        Assert.Contains(listing.Items, item => item.Name == "Invoices" && item.Kind == ArchiveItemKind.Folder);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "Documents", "Invoices")));
    }

    [Fact]
    public void Trash_does_not_allow_folder_creation()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveForbiddenException>(() => service.CreateFolder("trash", null, "Nope"));
    }

    [Fact]
    public void CreateFile_creates_an_empty_file_with_the_requested_extension()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        var listing = service.CreateFile("documents", null, "notes", ".md");

        Assert.Contains(listing.Items, item => item.Name == "notes.md" && item.Kind == ArchiveItemKind.File);
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "notes.md")));
        Assert.Equal(0, new FileInfo(Path.Combine(root.Path, "Documents", "notes.md")).Length);
    }

    [Fact]
    public void CreateFile_throws_when_category_cannot_create_folders()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveForbiddenException>(() => service.CreateFile("trash", null, "notes", ".txt"));
    }

    [Fact]
    public void CreateFile_throws_on_conflict_with_an_existing_item()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "notes.txt"));
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveConflictException>(() => service.CreateFile("documents", null, "notes", ".txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData("bad/name")]
    [InlineData("CON")]
    public void CreateFile_rejects_unsafe_names(string name)
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveValidationException>(() => service.CreateFile("documents", null, name, ".txt"));
    }

    [Fact]
    public void ValidateUploadDestination_resolves_the_final_path_without_writing_anything()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        var destination = service.ValidateUploadDestination("documents", null, "note.txt");

        Assert.Equal(Path.Combine(root.Path, "Documents", "note.txt"), destination.FinalPath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(root.Path, "Documents")));
    }

    [Fact]
    public void ValidateUploadDestination_rejects_unsupported_extension()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveValidationException>(() => service.ValidateUploadDestination("documents", null, "malware.exe"));
    }

    [Fact]
    public void ValidateUploadDestination_accepts_srt_subtitle_files()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        var destination = service.ValidateUploadDestination("documents", null, "movie.srt");

        Assert.Equal(Path.Combine(root.Path, "Documents", "movie.srt"), destination.FinalPath);
    }

    [Fact]
    public void ValidateUploadDestination_throws_on_name_collision()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "note.txt"));
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveConflictException>(() => service.ValidateUploadDestination("documents", null, "note.txt"));
    }

    [Fact]
    public void ValidateUploadDestination_rejects_a_path_traversal_style_name()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveValidationException>(() => service.ValidateUploadDestination("documents", null, "../evil.txt"));
    }

    [Fact]
    public void ValidateUploadDestination_throws_when_category_cannot_create_folders()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveForbiddenException>(() => service.ValidateUploadDestination("trash", null, "note.txt"));
    }

    [Fact]
    public void PublishUploadedFile_moves_the_source_into_the_validated_destination_and_is_readable_afterward()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"upload-source-{Guid.NewGuid():N}.txt");
        File.WriteAllText(sourcePath, "hello world");

        var listing = service.PublishUploadedFile("documents", null, "note.txt", sourcePath);

        Assert.Contains(listing.Items, item => item.Name == "note.txt");
        var writtenPath = Path.Combine(root.Path, "Documents", "note.txt");
        Assert.True(File.Exists(writtenPath));
        Assert.Equal("hello world", File.ReadAllText(writtenPath));
        Assert.False(File.Exists(sourcePath));
    }

    [Fact]
    public void PublishUploadedFile_rejects_a_collision_and_leaves_the_source_file_untouched()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "note.txt"));
        var service = CreateService(root.Path);
        var sourcePath = Path.Combine(Path.GetTempPath(), $"upload-source-{Guid.NewGuid():N}.txt");
        File.WriteAllText(sourcePath, "new");

        try
        {
            Assert.Throws<ArchiveConflictException>(() => service.PublishUploadedFile("documents", null, "note.txt", sourcePath));
            Assert.True(File.Exists(sourcePath));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public void Rename_rejects_duplicate_sibling_name()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "A"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "B"));
        var service = CreateService(root.Path);
        var item = service.List("documents", null).Items.Single(item => item.Name == "B");

        Assert.Throws<ArchiveConflictException>(() => service.Rename("documents", item.Id, "A"));
    }

    [Fact]
    public void Move_returns_a_pending_job_descriptor_without_touching_the_filesystem()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "note.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Target"));
        var service = CreateService(root.Path);
        var file = service.List("documents", null).Items.Single(item => item.Name == "note.txt");
        var target = service.List("documents", null).Items.Single(item => item.Name == "Target");

        var job = service.Move("documents", file.Id, "documents", target.Id);

        Assert.Equal(ArchiveMutationKind.Move, job.Kind);
        Assert.Equal(1, job.TotalItems);
        Assert.Equal("note.txt", job.Label);
        Assert.False(job.IsFolder);
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "note.txt")));
        Assert.False(File.Exists(Path.Combine(root.Path, "Documents", "Target", "note.txt")));
    }

    [Fact]
    public void Move_of_a_folder_counts_its_files_recursively_without_touching_the_filesystem()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Source", "Nested"));
        awaitFile(Path.Combine(root.Path, "Downloads", "Source", "a.txt"));
        awaitFile(Path.Combine(root.Path, "Downloads", "Source", "Nested", "b.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Target"));
        var service = CreateService(root.Path);
        var folder = service.List("downloads", null).Items.Single(item => item.Name == "Source");
        var target = service.List("videos", null).Items.Single(item => item.Name == "Target");

        var job = service.Move("downloads", folder.Id, "videos", target.Id);

        Assert.Equal(ArchiveMutationKind.Move, job.Kind);
        Assert.True(job.IsFolder);
        Assert.Equal(2, job.TotalItems);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "Downloads", "Source")));
    }

    [Fact]
    public void Move_rejects_moving_a_folder_into_itself()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Folder"));
        var service = CreateService(root.Path);
        var folder = service.List("documents", null).Items.Single(item => item.Name == "Folder");

        Assert.Throws<ArchiveValidationException>(() => service.Move("documents", folder.Id, "documents", folder.Id));
    }

    [Fact]
    public void Move_rejects_moving_a_folder_into_its_own_descendant()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "A", "B"));
        var service = CreateService(root.Path);
        var folderA = service.List("downloads", null).Items.Single(item => item.Name == "A");
        var folderB = service.List("downloads", folderA.Id).Items.Single(item => item.Name == "B");

        Assert.Throws<ArchiveValidationException>(() => service.Move("downloads", folderA.Id, "downloads", folderB.Id));
    }

    [Fact]
    public void Move_rejects_when_destination_already_has_an_item_with_the_same_name()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "note.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Target"));
        awaitFile(Path.Combine(root.Path, "Documents", "Target", "note.txt"));
        var service = CreateService(root.Path);
        var file = service.List("documents", null).Items.Single(item => item.Name == "note.txt");
        var target = service.List("documents", null).Items.Single(item => item.Name == "Target");

        Assert.Throws<ArchiveConflictException>(() => service.Move("documents", file.Id, "documents", target.Id));
    }

    [Fact]
    public void Move_throws_not_found_for_an_unknown_destination_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Downloads", "file.txt"));
        var service = CreateService(root.Path);
        var file = service.List("downloads", null).Items.Single(item => item.Name == "file.txt");

        Assert.Throws<ArchiveNotFoundException>(() => service.Move("downloads", file.Id, "not-a-real-category", null));
    }

    [Fact]
    public void Move_of_the_same_location_returns_a_job_with_source_equal_to_destination()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Downloads", "file.txt"));
        var service = CreateService(root.Path);
        var file = service.List("downloads", null).Items.Single(item => item.Name == "file.txt");

        var job = service.Move("downloads", file.Id, "downloads", null);

        Assert.Equal(Path.Combine(root.Path, "Downloads", "file.txt"), job.SourcePath);
        Assert.Equal(job.SourcePath, job.DestinationPath);
    }

    [Fact]
    public void BatchMove_returns_a_pending_job_with_combined_total_items_without_touching_the_filesystem()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "a.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Folder", "Nested"));
        awaitFile(Path.Combine(root.Path, "Documents", "Folder", "b.txt"));
        awaitFile(Path.Combine(root.Path, "Documents", "Folder", "Nested", "c.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Target"));
        var service = CreateService(root.Path);
        var items = service.List("documents", null).Items;
        var file = items.Single(item => item.Name == "a.txt");
        var folder = items.Single(item => item.Name == "Folder");
        var target = service.List("videos", null).Items.Single(item => item.Name == "Target");

        var job = service.BatchMove("documents", [file.Id, folder.Id], "videos", target.Id);

        Assert.Equal(ArchiveMutationKind.BatchMove, job.Kind);
        Assert.Equal(3, job.TotalItems);
        Assert.Equal("2 items", job.Label);
        Assert.NotNull(job.BatchEntries);
        Assert.Equal(2, job.BatchEntries!.Count);
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "Documents", "Folder")));
    }

    [Fact]
    public void BatchMove_rejects_the_whole_batch_when_one_item_conflicts_and_enqueues_nothing()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "a.txt"));
        awaitFile(Path.Combine(root.Path, "Documents", "b.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Target"));
        awaitFile(Path.Combine(root.Path, "Videos", "Target", "b.txt"));
        var service = CreateService(root.Path);
        var items = service.List("documents", null).Items;
        var fileA = items.Single(item => item.Name == "a.txt");
        var fileB = items.Single(item => item.Name == "b.txt");
        var target = service.List("videos", null).Items.Single(item => item.Name == "Target");

        Assert.Throws<ArchiveConflictException>(() => service.BatchMove("documents", [fileA.Id, fileB.Id], "videos", target.Id));
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "a.txt")));
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "b.txt")));
    }

    [Fact]
    public void BatchMove_rejects_the_whole_batch_when_one_item_would_move_a_folder_into_its_own_descendant()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "a.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "A", "B"));
        var service = CreateService(root.Path);
        var items = service.List("documents", null).Items;
        var fileA = items.Single(item => item.Name == "a.txt");
        var folderA = items.Single(item => item.Name == "A");
        var folderB = service.List("documents", folderA.Id).Items.Single(item => item.Name == "B");

        Assert.Throws<ArchiveValidationException>(() => service.BatchMove("documents", [fileA.Id, folderA.Id], "documents", folderB.Id));
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "Documents", "A")));
    }

    [Fact]
    public void BatchMove_rejects_a_category_root_in_the_selection()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "a.txt"));
        var service = CreateService(root.Path);
        var file = service.List("documents", null).Items.Single(item => item.Name == "a.txt");
        var categoryRoot = service.List("documents", null).CurrentFolder;

        Assert.Throws<ArchiveNotFoundException>(() => service.BatchMove("documents", [file.Id, categoryRoot.Id], "videos", null));
    }

    [Fact]
    public void BatchMove_throws_on_an_empty_selection()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveValidationException>(() => service.BatchMove("documents", [], "videos", null));
    }

    [Fact]
    public void MoveToTrash_returns_a_pending_job_descriptor_without_touching_the_filesystem()
    {
        using var root = CreateArchive();
        var file = Path.Combine(root.Path, "Documents", "note.txt");
        awaitFile(file);
        var service = CreateService(root.Path);
        var item = service.List("documents", null).Items.Single();

        var job = service.MoveToTrash("documents", item.Id);

        Assert.Equal(ArchiveMutationKind.MoveToTrash, job.Kind);
        Assert.Equal(1, job.TotalItems);
        Assert.Equal(file, job.SourcePath);
        Assert.StartsWith(Path.Combine(root.Path, "Trash"), job.DestinationPath);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(root.Path, "Trash", "note.txt")));
    }

    [Fact]
    public void EmptyTrash_returns_a_job_counting_files_recursively_without_touching_the_filesystem()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Trash", "one.txt"));
        awaitFile(Path.Combine(root.Path, "Trash", "two.txt"));
        var nested = Path.Combine(root.Path, "Trash", "Nested");
        Directory.CreateDirectory(nested);
        awaitFile(Path.Combine(nested, "inner.txt"));
        var service = CreateService(root.Path);

        var job = service.EmptyTrash("trash");

        Assert.Equal(ArchiveMutationKind.EmptyTrash, job.Kind);
        Assert.Equal(3, job.TotalItems);
        Assert.Null(job.DestinationPath);
        Assert.Equal(Path.Combine(root.Path, "Trash"), job.SourcePath);
        Assert.True(File.Exists(Path.Combine(root.Path, "Trash", "one.txt")));
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void EmptyTrash_on_non_trash_category_throws_ArchiveForbiddenException()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "note.txt"));
        var service = CreateService(root.Path);

        Assert.Throws<ArchiveForbiddenException>(() => service.EmptyTrash("documents"));
        Assert.True(File.Exists(Path.Combine(root.Path, "Documents", "note.txt")));
    }

    [Fact]
    public void EmptyTrash_with_empty_trash_returns_a_job_with_zero_total_items()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        var job = service.EmptyTrash("trash");

        Assert.Equal(0, job.TotalItems);
    }

    [Fact]
    public void Video_files_are_marked_for_player_selection()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Videos", "clip.mp4"));
        var service = CreateService(root.Path);

        var item = Assert.Single(service.List("videos", null).Items);

        Assert.True(item.IsVideo);
        Assert.Equal(".mp4", item.Extension);
    }

    [Fact]
    public void Transport_stream_files_are_convertible_but_not_playable()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Videos", "capture.ts"));
        var service = CreateService(root.Path);

        var item = Assert.Single(service.List("videos", null).Items);
        var upload = service.ValidateUploadDestination("videos", null, "upload.ts");

        Assert.True(item.IsConvertibleVideo);
        Assert.False(item.IsVideo);
        Assert.Equal(Path.Combine(root.Path, "Videos", "upload.ts"), upload.FinalPath);
    }

    [Theory]
    [InlineData("dvd.vob")]
    [InlineData("dvd.VOB")]
    public void Vob_files_are_convertible_and_uploadable_but_not_playable(string name)
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Videos", name));
        var service = CreateService(root.Path);

        var item = Assert.Single(service.List("videos", null).Items);
        var upload = service.ValidateUploadDestination("videos", null, name);

        Assert.True(item.IsConvertibleVideo);
        Assert.False(item.IsVideo);
        Assert.EndsWith(name, upload.FinalPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Folder_with_direct_video_reports_playable_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Trip"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "clip.mp4"));
        var service = CreateService(root.Path);

        var folder = Assert.Single(service.List("videos", null).Items);

        Assert.True(folder.HasPlayableMedia);
    }

    [Fact]
    public void Folder_with_direct_music_reports_playable_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Music", "Album"));
        awaitFile(Path.Combine(root.Path, "Music", "Album", "song.mp3"));
        var service = CreateService(root.Path);

        var folder = Assert.Single(service.List("music", null).Items);

        Assert.True(folder.HasPlayableMedia);
    }

    [Fact]
    public void Folder_without_direct_media_does_not_report_playable_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Reports"));
        awaitFile(Path.Combine(root.Path, "Documents", "Reports", "notes.txt"));
        var service = CreateService(root.Path);

        var folder = Assert.Single(service.List("documents", null).Items);

        Assert.False(folder.HasPlayableMedia);
    }

    [Fact]
    public void Folder_with_media_only_in_a_subfolder_reports_playable_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Trip", "Raw"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "Raw", "clip.mp4"));
        var service = CreateService(root.Path);

        var folder = Assert.Single(service.List("videos", null).Items);

        Assert.True(folder.HasPlayableMedia);
    }

    [Fact]
    public void Empty_folder_does_not_report_playable_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Empty"));
        var service = CreateService(root.Path);

        var folder = Assert.Single(service.List("videos", null).Items);

        Assert.False(folder.HasPlayableMedia);
    }

    [Fact]
    public void File_items_do_not_report_playable_media()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Videos", "clip.mp4"));
        var service = CreateService(root.Path);

        var file = Assert.Single(service.List("videos", null).Items);

        Assert.False(file.HasPlayableMedia);
    }

    [Fact]
    public void ListPlaylist_collects_direct_and_nested_video_and_music_files()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Videos", "Trip", "Raw"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "intro.mp4"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "Raw", "clip.mp4"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "Raw", "song.mp3"));
        awaitFile(Path.Combine(root.Path, "Videos", "Trip", "notes.txt"));
        var service = CreateService(root.Path);
        var folder = Assert.Single(service.List("videos", null).Items);

        var playlist = service.ListPlaylist("videos", folder.Id);

        Assert.Equal(3, playlist.Items.Count);
        Assert.Equal(["intro.mp4", "clip.mp4", "song.mp3"], playlist.Items.Select(item => item.Name));
        Assert.All(playlist.Items, item => Assert.Equal(ArchiveItemKind.File, item.Kind));
        Assert.Equal(2, playlist.Items.Count(item => item.IsVideo));
        Assert.Equal(1, playlist.Items.Count(item => item.IsMusic));
    }

    [Fact]
    public void ListPlaylist_returns_no_items_for_a_folder_without_media()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Reports"));
        awaitFile(Path.Combine(root.Path, "Documents", "Reports", "notes.txt"));
        var service = CreateService(root.Path);
        var folder = Assert.Single(service.List("documents", null).Items);

        var playlist = service.ListPlaylist("documents", folder.Id);

        Assert.Empty(playlist.Items);
    }

    [Fact]
    public void Audio_files_are_marked_in_any_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Music", "song.mp3"));
        awaitFile(Path.Combine(root.Path, "Music", "beat.wav"));
        awaitFile(Path.Combine(root.Path, "Books", "voice.mp3"));
        var service = CreateService(root.Path);

        var music = service.List("music", null).Items;
        var book = Assert.Single(service.List("books", null).Items);

        Assert.All(music, item => Assert.True(item.IsMusic));
        Assert.All(music, item => Assert.False(item.IsVideo));
        Assert.True(book.IsMusic);
        Assert.False(book.IsVideo);
    }

    [Fact]
    public void Music_listing_uses_first_direct_image_as_album_cover()
    {
        using var root = CreateArchive();
        var album = Path.Combine(root.Path, "Music", "Album");
        Directory.CreateDirectory(album);
        Directory.CreateDirectory(Path.Combine(album, "Nested"));
        awaitFile(Path.Combine(album, "song.mp3"));
        awaitFile(Path.Combine(album, "zeta.png"));
        awaitFile(Path.Combine(album, "alpha.jpg"));
        awaitFile(Path.Combine(album, "Nested", "aardvark.jpg"));
        var service = CreateService(root.Path);
        var folder = service.List("music", null).Items.Single(item => item.Name == "Album");

        var listing = service.List("music", folder.Id);
        var track = listing.Items.Single(item => item.Name == "song.mp3");

        Assert.True(track.IsMusic);
        Assert.Equal(folder.Id, track.AlbumCoverId);
        Assert.True(service.TryResolveAlbumCover("music", folder.Id, out var cover));
        Assert.Equal("alpha.jpg", cover!.Name);
    }

    [Fact]
    public void Image_files_are_marked_in_any_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", "photo.jpg"));
        awaitFile(Path.Combine(root.Path, "Pictures", "scan.jpeg"));
        awaitFile(Path.Combine(root.Path, "Pictures", "banner.png"));
        awaitFile(Path.Combine(root.Path, "Pictures", "notes.txt"));
        awaitFile(Path.Combine(root.Path, "Documents", "receipt.png"));
        var service = CreateService(root.Path);

        var pictures = service.List("photos", null).Items;
        var document = Assert.Single(service.List("documents", null).Items);

        Assert.Equal(3, pictures.Count(item => item.IsImage));
        Assert.True(pictures.Single(item => item.Name == "notes.txt") is { IsImage: false });
        Assert.True(document.IsImage);
        Assert.False(document.IsVideo);
        Assert.False(document.IsMusic);
    }

    [Theory]
    [InlineData("animation.GIF")]
    [InlineData("photo.webp")]
    [InlineData("photo.avif")]
    [InlineData("photo.bmp")]
    [InlineData("favicon.ico")]
    public void Added_raster_image_formats_are_classified_resolved_and_uploadable(string fileName)
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", fileName));
        var service = CreateService(root.Path);

        var item = Assert.Single(service.List("photos", null).Items);

        Assert.True(item.IsImage);
        Assert.True(service.TryResolveImage("photos", item.Id, out var resolved));
        Assert.Equal(fileName, resolved!.Name);
        Assert.Equal(Path.Combine(root.Path, "Documents", fileName),
            service.ValidateUploadDestination("documents", null, fileName).FinalPath);
    }

    [Fact]
    public void Svg_remains_non_image_and_is_not_uploadable()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", "vector.svg"));
        var service = CreateService(root.Path);

        var item = Assert.Single(service.List("photos", null).Items);

        Assert.False(item.IsImage);
        Assert.False(service.TryResolveImage("photos", item.Id, out _));
        Assert.Throws<ArchiveValidationException>(() => service.ValidateUploadDestination("documents", null, "vector.svg"));
    }

    [Fact]
    public void TryResolveImage_returns_false_for_folders_and_non_image_files()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        awaitFile(Path.Combine(root.Path, "Pictures", "notes.txt"));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");
        var textFile = service.List("photos", null).Items.Single(item => item.Name == "notes.txt");

        Assert.False(service.TryResolveImage("photos", folder.Id, out _));
        Assert.False(service.TryResolveImage("photos", textFile.Id, out _));
        Assert.False(service.TryResolveImage("photos", "unknown-id", out _));
    }

    [Fact]
    public void TryResolveImage_resolves_a_valid_image_item()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", "photo.jpg"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("photos", null).Items);

        Assert.True(service.TryResolveImage("photos", item.Id, out var resolved));
        Assert.Equal(Path.Combine(root.Path, "Pictures", "photo.jpg"), resolved!.PhysicalPath);
    }

    [Fact]
    public void TryResolveDownloadableItem_resolves_files_and_folders_only_in_their_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Downloads", "receipt.txt"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Reports"));
        var service = CreateService(root.Path);
        var items = service.List("downloads", null).Items;

        Assert.True(service.TryResolveDownloadableItem("downloads", items.Single(item => item.Name == "receipt.txt").Id, out var file));
        Assert.Equal(ArchiveItemKind.File, file!.Kind);
        Assert.True(service.TryResolveDownloadableItem("downloads", items.Single(item => item.Name == "Reports").Id, out var folder));
        Assert.Equal(ArchiveItemKind.Folder, folder!.Kind);
        Assert.False(service.TryResolveDownloadableItem("documents", file.Id, out _));
        Assert.False(service.TryResolveDownloadableItem("downloads", "unknown", out _));
    }

    [Fact]
    public void Album_cover_resolver_works_for_any_category_folder()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Books", "cover.jpg"));
        var service = CreateService(root.Path);
        var folder = service.List("books", null).CurrentFolder;

        Assert.True(service.TryResolveAlbumCover("books", folder.Id, out var cover));
        Assert.Equal("cover.jpg", cover!.Name);
    }

    [Fact]
    public void Epub_files_are_marked_as_books_only_inside_the_books_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Books", "novel.epub"));
        awaitFile(Path.Combine(root.Path, "Downloads", "archive.epub"));
        var service = CreateService(root.Path);

        var book = Assert.Single(service.List("books", null).Items);
        var download = Assert.Single(service.List("downloads", null).Items);

        Assert.True(book.IsBook);
        Assert.False(book.IsVideo);
        Assert.False(book.IsMusic);
        Assert.False(download.IsBook);
    }

    [Fact]
    public void Books_notes_helper_file_is_not_misclassified_as_book_content()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Books", "Notes"));
        awaitFile(Path.Combine(root.Path, "Books", "Notes", "pereneArchiveBookNotes.txt"));
        awaitFile(Path.Combine(root.Path, "Books", "novel.epub"));
        var service = CreateService(root.Path);

        var rootListing = service.List("books", null).Items;
        var notesFolder = rootListing.Single(item => item.Name == "Notes");
        var book = rootListing.Single(item => item.Name == "novel.epub");
        var notesFile = service.List("books", notesFolder.Id).Items.Single(item => item.Name == "pereneArchiveBookNotes.txt");

        Assert.False(notesFile.IsBook);
        Assert.True(book.IsBook);
    }

    [Fact]
    public void TextDocument_files_are_marked_in_any_category_and_nested_folders()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "notes.md"));
        awaitFile(Path.Combine(root.Path, "Documents", "readme.markdown"));
        awaitFile(Path.Combine(root.Path, "Documents", "plain.txt"));
        awaitFile(Path.Combine(root.Path, "Documents", "photo.jpg"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Nested"));
        awaitFile(Path.Combine(root.Path, "Downloads", "Nested", "log.txt"));
        var service = CreateService(root.Path);

        var documents = service.List("documents", null).Items;
        var nestedFolder = service.List("downloads", null).Items.Single(item => item.Name == "Nested");
        var nestedFile = service.List("downloads", nestedFolder.Id).Items.Single(item => item.Name == "log.txt");

        Assert.Equal(3, documents.Count(item => item.IsTextDocument));
        Assert.True(documents.Single(item => item.Name == "notes.md").IsTextDocument);
        Assert.True(documents.Single(item => item.Name == "readme.markdown").IsTextDocument);
        Assert.True(documents.Single(item => item.Name == "plain.txt").IsTextDocument);
        Assert.False(documents.Single(item => item.Name == "photo.jpg").IsTextDocument);
        Assert.True(nestedFile.IsTextDocument);
    }

    [Fact]
    public void TryResolveTextDocument_returns_false_for_folders_and_unsupported_files()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Folder"));
        awaitFile(Path.Combine(root.Path, "Documents", "photo.jpg"));
        var service = CreateService(root.Path);
        var folder = service.List("documents", null).Items.Single(item => item.Name == "Folder");
        var image = service.List("documents", null).Items.Single(item => item.Name == "photo.jpg");

        Assert.False(service.TryResolveTextDocument("documents", folder.Id, out _));
        Assert.False(service.TryResolveTextDocument("documents", image.Id, out _));
        Assert.False(service.TryResolveTextDocument("documents", "unknown-id", out _));
    }

    [Fact]
    public void TryResolveTextDocument_resolves_a_valid_markdown_item_in_any_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Books", "notes.md"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("books", null).Items);

        Assert.True(service.TryResolveTextDocument("books", item.Id, out var resolved));
        Assert.Equal(Path.Combine(root.Path, "Books", "notes.md"), resolved!.PhysicalPath);
    }

    [Fact]
    public void Pdf_files_are_marked_in_any_category_and_nested_folders()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Documents", "report.pdf"));
        awaitFile(Path.Combine(root.Path, "Documents", "photo.jpg"));
        Directory.CreateDirectory(Path.Combine(root.Path, "Downloads", "Nested"));
        awaitFile(Path.Combine(root.Path, "Downloads", "Nested", "manual.pdf"));
        var service = CreateService(root.Path);

        var documents = service.List("documents", null).Items;
        var nestedFolder = service.List("downloads", null).Items.Single(item => item.Name == "Nested");
        var nestedFile = service.List("downloads", nestedFolder.Id).Items.Single(item => item.Name == "manual.pdf");

        Assert.True(documents.Single(item => item.Name == "report.pdf").IsPdfDocument);
        Assert.False(documents.Single(item => item.Name == "photo.jpg").IsPdfDocument);
        Assert.True(nestedFile.IsPdfDocument);
    }

    [Fact]
    public void TryResolvePdfDocument_returns_false_for_folders_and_unsupported_files()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Documents", "Folder"));
        awaitFile(Path.Combine(root.Path, "Documents", "photo.jpg"));
        var service = CreateService(root.Path);
        var folder = service.List("documents", null).Items.Single(item => item.Name == "Folder");
        var image = service.List("documents", null).Items.Single(item => item.Name == "photo.jpg");

        Assert.False(service.TryResolvePdfDocument("documents", folder.Id, out _));
        Assert.False(service.TryResolvePdfDocument("documents", image.Id, out _));
        Assert.False(service.TryResolvePdfDocument("documents", "unknown-id", out _));
    }

    [Fact]
    public void TryResolvePdfDocument_resolves_a_valid_pdf_item_in_any_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Books", "report.pdf"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("books", null).Items);

        Assert.True(service.TryResolvePdfDocument("books", item.Id, out var resolved));
        Assert.Equal(Path.Combine(root.Path, "Books", "report.pdf"), resolved!.PhysicalPath);
    }

    [Fact]
    public void TryResolveBook_returns_false_for_folders_and_non_epub_files()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Books", "Series"));
        awaitFile(Path.Combine(root.Path, "Books", "notes.txt"));
        var service = CreateService(root.Path);
        var folder = service.List("books", null).Items.Single(item => item.Name == "Series");
        var textFile = service.List("books", null).Items.Single(item => item.Name == "notes.txt");

        Assert.False(service.TryResolveBook("books", folder.Id, out _));
        Assert.False(service.TryResolveBook("books", textFile.Id, out _));
        Assert.False(service.TryResolveBook("books", "unknown-id", out _));
    }

    [Fact]
    public void TryResolveBook_returns_false_when_extension_matches_outside_books_category()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Downloads", "archive.epub"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("downloads", null).Items);

        Assert.False(service.TryResolveBook("downloads", item.Id, out _));
    }

    [Fact]
    public void TryResolveBook_resolves_a_valid_epub_item()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Books", "novel.epub"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("books", null).Items);

        Assert.True(service.TryResolveBook("books", item.Id, out var resolved));
        Assert.Equal(Path.Combine(root.Path, "Books", "novel.epub"), resolved!.PhysicalPath);
    }

    [Fact]
    public void GetCategoryRootPath_returns_the_category_physical_folder()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);

        Assert.Equal(Path.Combine(root.Path, "Pictures"), service.GetCategoryRootPath("photos"));
    }

    [Fact]
    public void ComputeItemId_matches_the_id_produced_by_listing()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", "photo.jpg"));
        var service = CreateService(root.Path);
        var item = Assert.Single(service.List("photos", null).Items);

        var computed = service.ComputeItemId("photos", Path.Combine(root.Path, "Pictures", "photo.jpg"));

        Assert.Equal(item.Id, computed);
    }

    [Fact]
    public void BuildListing_never_reports_the_reserved_folder_thumbnail_file_as_a_child_item()
    {
        using var root = CreateArchive();
        var folderPath = Path.Combine(root.Path, "Pictures", "Album");
        Directory.CreateDirectory(folderPath);
        awaitFile(Path.Combine(folderPath, ArchiveService.FolderThumbnailFileName));
        awaitFile(Path.Combine(folderPath, "photo.jpg"));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");

        var listing = service.List("photos", folder.Id);

        Assert.Single(listing.Items);
        Assert.DoesNotContain(listing.Items, item => item.Name == ArchiveService.FolderThumbnailFileName);
    }

    [Fact]
    public void Folder_without_a_thumbnail_resolves_no_thumbnail_path()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");
        var resolvedFolder = service.List("photos", folder.Id).CurrentFolder;

        Assert.False(service.TryGetFolderThumbnailPath(resolvedFolder, out _));
    }

    [Fact]
    public void Folder_with_a_reserved_thumbnail_file_resolves_its_path()
    {
        using var root = CreateArchive();
        var folderPath = Path.Combine(root.Path, "Pictures", "Album");
        Directory.CreateDirectory(folderPath);
        awaitFile(Path.Combine(folderPath, ArchiveService.FolderThumbnailFileName));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");
        var resolvedFolder = service.List("photos", folder.Id).CurrentFolder;

        Assert.True(service.TryGetFolderThumbnailPath(resolvedFolder, out var path));
        Assert.Equal(Path.Combine(folderPath, ArchiveService.FolderThumbnailFileName), path);
    }

    [Fact]
    public void Reserved_thumbnail_filename_is_excluded_from_album_cover_resolution()
    {
        using var root = CreateArchive();
        var album = Path.Combine(root.Path, "Music", "Album");
        Directory.CreateDirectory(album);
        awaitFile(Path.Combine(album, ArchiveService.FolderThumbnailFileName));
        var service = CreateService(root.Path);
        var folder = service.List("music", null).Items.Single(item => item.Name == "Album");

        Assert.False(service.TryResolveAlbumCover("music", folder.Id, out _));
    }

    [Fact]
    public void TryResolveFolder_resolves_a_non_root_folder_by_id()
    {
        using var root = CreateArchive();
        Directory.CreateDirectory(Path.Combine(root.Path, "Pictures", "Album"));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");

        Assert.True(service.TryResolveFolder("photos", folder.Id, out var resolved));
        Assert.Equal(Path.Combine(root.Path, "Pictures", "Album"), resolved!.PhysicalPath);
    }

    [Fact]
    public void TryResolveFolder_rejects_the_category_root()
    {
        using var root = CreateArchive();
        var service = CreateService(root.Path);
        var categoryRoot = service.List("photos", null).CurrentFolder;

        Assert.False(service.TryResolveFolder("photos", categoryRoot.Id, out _));
    }

    [Fact]
    public void TryResolveFolder_rejects_a_file_id()
    {
        using var root = CreateArchive();
        awaitFile(Path.Combine(root.Path, "Pictures", "photo.jpg"));
        var service = CreateService(root.Path);
        var file = service.List("photos", null).Items.Single(item => item.Name == "photo.jpg");

        Assert.False(service.TryResolveFolder("photos", file.Id, out _));
    }

    [Fact]
    public void Renaming_a_folder_keeps_its_reserved_thumbnail_file_resolvable_at_the_new_id()
    {
        using var root = CreateArchive();
        var folderPath = Path.Combine(root.Path, "Pictures", "Album");
        Directory.CreateDirectory(folderPath);
        awaitFile(Path.Combine(folderPath, ArchiveService.FolderThumbnailFileName));
        var service = CreateService(root.Path);
        var folder = service.List("photos", null).Items.Single(item => item.Name == "Album");

        service.Rename("photos", folder.Id, "Renamed");

        var renamed = service.List("photos", null).Items.Single(item => item.Name == "Renamed");
        Assert.True(service.TryResolveFolder("photos", renamed.Id, out var resolvedFolder));
        Assert.True(service.TryGetFolderThumbnailPath(resolvedFolder!, out var path));
        Assert.Equal(Path.Combine(root.Path, "Pictures", "Renamed", ArchiveService.FolderThumbnailFileName), path);
    }

    private static ArchiveService CreateService(string path) =>
        new(Options.Create(new ArchiveRootOptions { Path = path }));

    private static TemporaryDirectory CreateArchive()
    {
        var root = new TemporaryDirectory();
        foreach (var folder in new[] { "Videos", "Pictures", "Music", "Documents", "Books", "Downloads", "Shared", "Family", "History", "Trash" })
        {
            Directory.CreateDirectory(Path.Combine(root.Path, folder));
        }

        return root;
    }

    private static void awaitFile(string path) => File.WriteAllText(path, "content");

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"video-manager-archive-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
