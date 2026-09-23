using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class ArchiveMutationExecutorTests
{
    [Fact]
    public async Task MoveAsync_moves_a_single_file_and_reports_zero_then_one()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "source.txt");
        await File.WriteAllTextAsync(source, "content");
        var destination = Path.Combine(root.Path, "dest", "source.txt");
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.Move, source, destination, IsFolder: false, TotalItems: 1, Label: "source.txt");
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.MoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Success, result.Outcome);
        Assert.Equal([1], progress);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task MoveAsync_moves_a_nested_folder_reporting_incremental_progress_and_removes_the_source()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "Source");
        Directory.CreateDirectory(Path.Combine(source, "Nested"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(source, "Nested", "b.txt"), "b");
        var destination = Path.Combine(root.Path, "Target", "Source");
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.Move, source, destination, IsFolder: true, TotalItems: 2, Label: "Source");
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.MoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Success, result.Outcome);
        Assert.Equal([1, 2], progress);
        Assert.False(Directory.Exists(source));
        Assert.Equal("a", await File.ReadAllTextAsync(Path.Combine(destination, "a.txt")));
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(destination, "Nested", "b.txt")));
    }

    [Fact]
    public async Task MoveAsync_with_identical_source_and_destination_is_a_no_op_success()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "source.txt");
        await File.WriteAllTextAsync(source, "content");
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.Move, source, source, IsFolder: false, TotalItems: 1, Label: "source.txt");
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.MoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Success, result.Outcome);
        Assert.Equal([1], progress);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task EmptyTrashAsync_deletes_all_files_recursively_and_reports_progress_per_file()
    {
        using var root = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root.Path, "one.txt"), "1");
        var nested = Path.Combine(root.Path, "Nested");
        Directory.CreateDirectory(nested);
        await File.WriteAllTextAsync(Path.Combine(nested, "two.txt"), "2");
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.EmptyTrash, root.Path, null, IsFolder: true, TotalItems: 2, Label: "Trash");
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.EmptyTrashAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Success, result.Outcome);
        Assert.Equal([1, 2], progress);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
    }

    [Fact]
    public async Task BatchMoveAsync_moves_every_entry_and_reports_a_running_total_across_the_whole_batch()
    {
        using var root = new TemporaryDirectory();
        var fileSource = Path.Combine(root.Path, "a.txt");
        await File.WriteAllTextAsync(fileSource, "a");
        var folderSource = Path.Combine(root.Path, "Folder");
        Directory.CreateDirectory(folderSource);
        await File.WriteAllTextAsync(Path.Combine(folderSource, "b.txt"), "b");
        await File.WriteAllTextAsync(Path.Combine(folderSource, "c.txt"), "c");
        var fileDestination = Path.Combine(root.Path, "Target", "a.txt");
        var folderDestination = Path.Combine(root.Path, "Target", "Folder");
        var batchEntries = new List<ArchiveMutationBatchEntry>
        {
            new(fileSource, fileDestination, IsFolder: false, FileCount: 1),
            new(folderSource, folderDestination, IsFolder: true, FileCount: 2),
        };
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.BatchMove, root.Path, root.Path, IsFolder: true, TotalItems: 3, Label: "2 items", BatchEntries: batchEntries);
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.BatchMoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Success, result.Outcome);
        Assert.Equal([1, 2, 3], progress);
        Assert.False(File.Exists(fileSource));
        Assert.False(Directory.Exists(folderSource));
        Assert.True(File.Exists(fileDestination));
        Assert.True(File.Exists(Path.Combine(folderDestination, "b.txt")));
        Assert.True(File.Exists(Path.Combine(folderDestination, "c.txt")));
    }

    [Fact]
    public async Task BatchMoveAsync_stops_at_the_first_failing_entry_and_reports_the_partial_count()
    {
        using var root = new TemporaryDirectory();
        var firstSource = Path.Combine(root.Path, "first.txt");
        await File.WriteAllTextAsync(firstSource, "1");
        var secondSource = Path.Combine(root.Path, "missing.txt");
        var thirdSource = Path.Combine(root.Path, "third.txt");
        await File.WriteAllTextAsync(thirdSource, "3");
        var batchEntries = new List<ArchiveMutationBatchEntry>
        {
            new(firstSource, Path.Combine(root.Path, "Target", "first.txt"), IsFolder: false, FileCount: 1),
            new(secondSource, Path.Combine(root.Path, "Target", "missing.txt"), IsFolder: false, FileCount: 1),
            new(thirdSource, Path.Combine(root.Path, "Target", "third.txt"), IsFolder: false, FileCount: 1),
        };
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.BatchMove, root.Path, root.Path, IsFolder: true, TotalItems: 3, Label: "3 items", BatchEntries: batchEntries);
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.BatchMoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Failed, result.Outcome);
        Assert.Contains("1 of 3", result.Diagnostic);
        Assert.Equal([1], progress);
        Assert.True(File.Exists(Path.Combine(root.Path, "Target", "first.txt")));
        Assert.True(File.Exists(thirdSource));
        Assert.False(File.Exists(Path.Combine(root.Path, "Target", "third.txt")));
    }

    [Fact]
    public async Task BatchMoveAsync_for_a_trash_batch_names_Trash_in_the_failure_diagnostic()
    {
        using var root = new TemporaryDirectory();
        var firstSource = Path.Combine(root.Path, "first.txt");
        await File.WriteAllTextAsync(firstSource, "1");
        var batchEntries = new List<ArchiveMutationBatchEntry>
        {
            new(firstSource, Path.Combine(root.Path, "Trash", "first.txt"), IsFolder: false, FileCount: 1),
            new(Path.Combine(root.Path, "missing.txt"), Path.Combine(root.Path, "Trash", "missing.txt"), IsFolder: false, FileCount: 1),
        };
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.BatchMoveToTrash, root.Path, root.Path, IsFolder: true, TotalItems: 2, Label: "2 items", BatchEntries: batchEntries);
        var executor = new ArchiveMutationExecutor();

        var result = await executor.BatchMoveAsync(job, _ => { }, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Failed, result.Outcome);
        Assert.Contains("to Trash", result.Diagnostic);
        Assert.Contains("1 of 2", result.Diagnostic);
        Assert.True(File.Exists(Path.Combine(root.Path, "Trash", "first.txt")));
    }

    [Fact]
    public async Task MoveAsync_without_a_destination_fails_without_reporting_progress()
    {
        using var root = new TemporaryDirectory();
        var source = Path.Combine(root.Path, "source.txt");
        await File.WriteAllTextAsync(source, "content");
        var job = new ArchiveMutationJob("job", ArchiveMutationKind.Move, source, null, IsFolder: false, TotalItems: 1, Label: "source.txt");
        var progress = new List<int>();
        var executor = new ArchiveMutationExecutor();

        var result = await executor.MoveAsync(job, progress.Add, CancellationToken.None);

        Assert.Equal(ArchiveMutationOutcome.Failed, result.Outcome);
        Assert.Empty(progress);
        Assert.True(File.Exists(source));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"archive-mutation-executor-{Guid.NewGuid():N}");
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
