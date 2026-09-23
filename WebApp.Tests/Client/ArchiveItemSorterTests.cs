using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class ArchiveItemSorterTests
{
    private static ArchiveItemDto File(string name, long? size = 0, int day = 1) =>
        new(name, name, ArchiveItemKind.File, ".txt", size, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc), false);

    private static ArchiveItemDto Folder(string name) =>
        new(name, name, ArchiveItemKind.Folder, null, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), false);

    private static string[] Names(IEnumerable<ArchiveItemDto> items) => items.Select(i => i.Name).ToArray();

    [Fact]
    public void Sort_WithNameOption_OrdersFilesAlphabeticallyCaseInsensitive()
    {
        var result = ArchiveItemSorter.Sort([File("banana"), File("Cherry"), File("apple")], ArchiveSortOption.Name);

        Assert.Equal(["apple", "banana", "Cherry"], Names(result));
    }

    [Fact]
    public void Sort_WithNameDescendingOption_OrdersFoldersThenFilesReverseAlphabetically()
    {
        var result = ArchiveItemSorter.Sort([File("banana"), Folder("a"), File("Cherry"), Folder("b"), File("apple")], ArchiveSortOption.NameDescending);

        Assert.Equal(["b", "a", "Cherry", "banana", "apple"], Names(result));
    }

    [Fact]
    public void Sort_WithSizeOption_OrdersFilesLargestFirst()
    {
        var result = ArchiveItemSorter.Sort([File("a", 10), File("b", 300), File("c", 20)], ArchiveSortOption.Size);

        Assert.Equal(["b", "c", "a"], Names(result));
    }

    [Fact]
    public void Sort_WithSizeOption_TreatsNullSizeAsZero()
    {
        var result = ArchiveItemSorter.Sort([File("a", null), File("b", 5), File("c", 1)], ArchiveSortOption.Size);

        Assert.Equal(["b", "c", "a"], Names(result));
    }

    [Fact]
    public void Sort_WithDateOption_OrdersFilesNewestFirst()
    {
        var result = ArchiveItemSorter.Sort([File("a", day: 1), File("b", day: 20), File("c", day: 10)], ArchiveSortOption.Date);

        Assert.Equal(["b", "c", "a"], Names(result));
    }

    [Theory]
    [InlineData(ArchiveSortOption.Name)]
    [InlineData(ArchiveSortOption.NameDescending)]
    [InlineData(ArchiveSortOption.Size)]
    [InlineData(ArchiveSortOption.Date)]
    public void Sort_AlwaysOrdersFoldersBeforeFiles_RegardlessOfOption(ArchiveSortOption option)
    {
        var result = ArchiveItemSorter.Sort([File("a", 999, 28), Folder("z"), File("b", 1, 1), Folder("m")], option);

        Assert.Equal([ArchiveItemKind.Folder, ArchiveItemKind.Folder, ArchiveItemKind.File, ArchiveItemKind.File],
            result.Select(i => i.Kind).ToArray());
    }

    [Theory]
    [InlineData(ArchiveSortOption.Name)]
    [InlineData(ArchiveSortOption.Size)]
    [InlineData(ArchiveSortOption.Date)]
    public void Sort_OrdersFoldersByNameWithinGroup(ArchiveSortOption option)
    {
        var result = ArchiveItemSorter.Sort([Folder("charlie"), Folder("Alpha"), Folder("bravo")], option);

        Assert.Equal(["Alpha", "bravo", "charlie"], Names(result));
    }

    [Fact]
    public void Sort_WithEmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(ArchiveItemSorter.Sort([], ArchiveSortOption.Size));
    }
}
