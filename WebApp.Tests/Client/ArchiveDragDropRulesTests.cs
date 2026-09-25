using WebApp.Client.Models;

namespace WebApp.Tests.Client;

public sealed class ArchiveDragDropRulesTests
{
    [Fact]
    public void ResolveTargets_WhenItemIsSelected_ReturnsWholeSelection()
    {
        var result = ArchiveDragDropRules.ResolveTargets(["a", "b", "c"], "b");

        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ResolveTargets_WhenItemIsNotSelected_ReturnsOnlyThatItem()
    {
        var result = ArchiveDragDropRules.ResolveTargets(["a", "b"], "z");

        Assert.Equal(["z"], result);
    }

    [Fact]
    public void ResolveTargets_WithEmptySelection_ReturnsOnlyThatItem()
    {
        Assert.Equal(["z"], ArchiveDragDropRules.ResolveTargets([], "z"));
    }

    [Fact]
    public void CanDrop_OntoDifferentFolder_IsAllowed()
    {
        Assert.True(ArchiveDragDropRules.CanDrop(["a"], "target", "current"));
    }

    [Fact]
    public void CanDrop_OntoOneOfTheDraggedItems_IsRejected()
    {
        Assert.False(ArchiveDragDropRules.CanDrop(["a", "target"], "target", "current"));
    }

    [Fact]
    public void CanDrop_OntoCurrentFolder_IsRejected()
    {
        Assert.False(ArchiveDragDropRules.CanDrop(["a"], "current", "current"));
        Assert.False(ArchiveDragDropRules.CanDrop(["a"], null, null));
    }

    [Fact]
    public void CanDrop_OntoCategoryRootFromSubfolder_IsAllowed()
    {
        Assert.True(ArchiveDragDropRules.CanDrop(["a"], null, "current"));
    }

    [Fact]
    public void CanDrop_WithNoDraggedItems_IsRejected()
    {
        Assert.False(ArchiveDragDropRules.CanDrop([], "target", "current"));
    }
}
