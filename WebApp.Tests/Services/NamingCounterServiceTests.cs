using WebApp.Client.Models;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Tests.Services;

public sealed class NamingCounterServiceTests : IDisposable
{
    private readonly JobTestDb _db = new();
    private readonly TemporaryDirectory _dir = new();

    public void Dispose()
    {
        _dir.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void One_hundred_parallel_allocations_are_unique_and_consecutive()
    {
        var results = new int[100];
        Parallel.For(0, 100, new ParallelOptions { MaxDegreeOfParallelism = 32 }, i =>
            results[i] = _db.Counters.Allocate("cut", _dir.Path, "Jennifer White", ".mp4", () => 0));

        Assert.Equal(Enumerable.Range(1, 100), results.OrderBy(value => value));
    }

    [Fact]
    public async Task First_use_seeds_from_existing_files_and_deletion_never_regresses_the_counter()
    {
        var existing = Path.Combine(_dir.Path, "Jennifer White 0003.mp4");
        await File.WriteAllBytesAsync(existing, [1]);

        var first = _db.Counters.AllocatePath("cut", _dir.Path, "Jennifer White", ".mp4",
            () => NamingCounterService.HighestOnDisk(_dir.Path, "Jennifer White", ".mp4"), n => Path.Combine(_dir.Path, $"Jennifer White {n:0000}.mp4"));
        Assert.EndsWith("Jennifer White 0004.mp4", first);

        File.Delete(existing);
        var second = _db.Counters.AllocatePath("cut", _dir.Path, "Jennifer White", ".mp4", () => 0, n => Path.Combine(_dir.Path, $"Jennifer White {n:0000}.mp4"));
        Assert.EndsWith("Jennifer White 0005.mp4", second);
    }

    [Fact]
    public async Task A_file_added_outside_the_app_makes_the_allocator_skip_ahead()
    {
        Assert.Equal(1, _db.Counters.Allocate("cut", _dir.Path, "Maria Rodriguez", ".mp4", () => 0));
        await File.WriteAllBytesAsync(Path.Combine(_dir.Path, "Maria Rodriguez 0002.mp4"), [1]);

        var path = _db.Counters.AllocatePath("cut", _dir.Path, "Maria Rodriguez", ".mp4", () => 0, n => Path.Combine(_dir.Path, $"Maria Rodriguez {n:0000}.mp4"));

        Assert.EndsWith("Maria Rodriguez 0003.mp4", path);
    }

    [Fact]
    public void Series_are_separate_per_kind_and_directory_but_prefix_case_is_ignored()
    {
        using var other = new TemporaryDirectory();
        Assert.Equal(1, _db.Counters.Allocate("cut", _dir.Path, "Beach Sunset", ".jpg", () => 0));
        Assert.Equal(2, _db.Counters.Allocate("cut", _dir.Path, "beach sunset", ".jpg", () => 0));
        Assert.Equal(1, _db.Counters.Allocate("image-crop", _dir.Path, "Beach Sunset", ".jpg", () => 0));
        Assert.Equal(1, _db.Counters.Allocate("cut", other.Path, "Beach Sunset", ".jpg", () => 0));
    }

    [Fact]
    public async Task Counters_survive_a_new_service_instance()
    {
        Assert.Equal(1, _db.Counters.Allocate("composition", _dir.Path, "Trip", ".mp4", () => 0));

        var restarted = new NamingCounterService(_db.Scopes);

        Assert.Equal(2, restarted.Allocate("composition", _dir.Path, "Trip", ".mp4", () => 0));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Conversion_naming_keeps_its_format_and_numbers_consecutively()
    {
        var source = Path.Combine(_dir.Path, "Holiday.mkv");
        await File.WriteAllBytesAsync(source, [1]);
        var entry = new ArchiveItemEntry("id", ArchiveCategory.Defaults[0], source, "Holiday.mkv", ArchiveItemKind.File, ".mkv", 1, DateTime.UtcNow, true);
        var naming = new VideoConversionNamingService(_db.Counters);

        var first = naming.GetNextPath(entry);
        await File.WriteAllBytesAsync(first, [1]);
        var second = naming.GetNextPath(entry);

        Assert.Equal(Path.Combine(_dir.Path, "Holiday Converted 0001.mp4"), first);
        Assert.Equal(Path.Combine(_dir.Path, "Holiday Converted 0002.mp4"), second);
    }
}
