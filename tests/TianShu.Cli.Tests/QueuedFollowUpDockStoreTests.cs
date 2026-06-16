using TianShu.Cli.Interaction.Host;

namespace TianShu.Cli.Tests;

public sealed class QueuedFollowUpDockStoreTests
{
    [Fact]
    public void MoveSelection_WhenNoSelection_ChoosesDirectionalEdge()
    {
        var store = new QueuedFollowUpDockStore();
        store.Add("a", "第一条");
        store.Add("b", "第二条");

        Assert.True(store.MoveSelection(1));
        Assert.Equal(1, store.Capture()?.SelectedIndex);

        store = new QueuedFollowUpDockStore();
        store.Add("a", "第一条");
        store.Add("b", "第二条");

        Assert.True(store.MoveSelection(-1));
        Assert.Equal(2, store.Capture()?.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_KeepsSelectedEntryVisible()
    {
        var store = new QueuedFollowUpDockStore();
        store.Add("a", "第一条");
        store.Add("b", "第二条");
        store.Add("c", "第三条");
        store.Add("d", "第四条");

        store.MoveSelection(-1);

        var state = store.Capture();

        Assert.Equal(4, state?.SelectedIndex);
        Assert.Contains(state!.Entries, entry => entry.Index == 4 && entry.IsSelected);
        Assert.DoesNotContain(state.Entries, entry => entry.Index == 1);
    }

    [Fact]
    public void Remove_AdjustsSelectionAfterDeletedEntry()
    {
        var store = new QueuedFollowUpDockStore();
        store.Add("a", "第一条");
        store.Add("b", "第二条");
        store.Add("c", "第三条");

        store.MoveSelection(1);
        store.MoveSelection(1);
        Assert.Equal(2, store.Capture()?.SelectedIndex);

        Assert.True(store.Remove("b"));

        var state = store.Capture();
        Assert.Equal(2, state?.SelectedIndex);
        Assert.Contains(state!.Entries, entry => entry.Index == 2 && entry.Preview == "第三条" && entry.IsSelected);
    }

    [Fact]
    public void TryGetSelected_ReturnsCurrentSelectedEntry()
    {
        var store = new QueuedFollowUpDockStore();
        store.Add("a", "第一条");
        store.Add("b", "第二条");

        Assert.Null(store.TryGetSelected());

        store.MoveSelection(-1);

        var selected = store.TryGetSelected();

        Assert.NotNull(selected);
        Assert.Equal(2, selected.Index);
        Assert.Equal("b", selected.CorrelationId);
        Assert.Equal("第二条", selected.Preview);
    }
}
