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
