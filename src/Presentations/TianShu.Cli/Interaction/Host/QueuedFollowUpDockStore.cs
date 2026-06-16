using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Keeps the visible pending-send follow-up state for the terminal Dock.
/// 保存终端 Dock 中可见的待发送 follow-up 状态。
/// </summary>
internal sealed class QueuedFollowUpDockStore
{
    private readonly object syncRoot = new();
    private readonly List<QueuedFollowUpDockEntry> entries = [];
    private int? selectedIndex;

    public QueuedFollowUpDockState? Capture()
    {
        lock (syncRoot)
        {
            if (entries.Count == 0)
            {
                selectedIndex = null;
                return null;
            }

            selectedIndex = ClampSelectedIndex(selectedIndex);
            var visibleStart = ResolveVisibleStart(selectedIndex, entries.Count, 3);
            var visibleEntries = entries
                .Skip(visibleStart)
                .Take(3)
                .Select((entry, index) =>
                {
                    var displayIndex = visibleStart + index + 1;
                    return new QueuedFollowUpDockEntryState(
                        displayIndex,
                        entry.Preview,
                        selectedIndex == displayIndex);
                })
                .ToArray();

            return new QueuedFollowUpDockState(
                entries.Count,
                visibleEntries,
                selectedIndex);
        }
    }

    public void Add(string correlationId, string text)
    {
        lock (syncRoot)
        {
            var existingIndex = entries.FindIndex(entry =>
                string.Equals(entry.CorrelationId, correlationId, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                entries.RemoveAt(existingIndex);
                AdjustSelectionAfterRemove(existingIndex + 1);
            }

            entries.Add(new QueuedFollowUpDockEntry(correlationId, text));
        }
    }

    public bool Remove(string correlationId)
    {
        lock (syncRoot)
        {
            var removedIndex = entries.FindIndex(entry =>
                string.Equals(entry.CorrelationId, correlationId, StringComparison.Ordinal));
            if (removedIndex < 0)
            {
                return false;
            }

            entries.RemoveAt(removedIndex);
            AdjustSelectionAfterRemove(removedIndex + 1);
            return true;
        }
    }

    public bool MoveSelection(int delta)
    {
        lock (syncRoot)
        {
            if (entries.Count == 0)
            {
                selectedIndex = null;
                return false;
            }

            var previous = selectedIndex;
            if (previous is null)
            {
                selectedIndex = delta < 0 ? entries.Count : 1;
                return true;
            }

            selectedIndex = Math.Clamp(previous.Value + delta, 1, entries.Count);
            return selectedIndex != previous;
        }
    }

    public QueuedFollowUpDockEntrySnapshot? TryGetByIndex(int index)
    {
        lock (syncRoot)
        {
            if (index <= 0 || index > entries.Count)
            {
                return null;
            }

            var entry = entries[index - 1];
            return new QueuedFollowUpDockEntrySnapshot(index, entry.CorrelationId, entry.Preview);
        }
    }

    public QueuedFollowUpDockEntrySnapshot? TryGetSelected()
    {
        lock (syncRoot)
        {
            selectedIndex = ClampSelectedIndex(selectedIndex);
            return selectedIndex is int index
                ? TryGetByIndexUnsafe(index)
                : null;
        }
    }

    public bool Clear()
    {
        lock (syncRoot)
        {
            var changed = entries.Count > 0;
            entries.Clear();
            selectedIndex = null;
            return changed;
        }
    }

    private QueuedFollowUpDockEntrySnapshot? TryGetByIndexUnsafe(int index)
    {
        if (index <= 0 || index > entries.Count)
        {
            return null;
        }

        var entry = entries[index - 1];
        return new QueuedFollowUpDockEntrySnapshot(index, entry.CorrelationId, entry.Preview);
    }

    private int? ClampSelectedIndex(int? value)
    {
        if (value is null || entries.Count == 0)
        {
            return null;
        }

        return Math.Clamp(value.Value, 1, entries.Count);
    }

    private void AdjustSelectionAfterRemove(int removedIndex)
    {
        if (entries.Count == 0 || selectedIndex is null)
        {
            selectedIndex = null;
            return;
        }

        if (selectedIndex == removedIndex)
        {
            selectedIndex = Math.Min(removedIndex, entries.Count);
            return;
        }

        if (selectedIndex > removedIndex)
        {
            selectedIndex--;
        }

        selectedIndex = ClampSelectedIndex(selectedIndex);
    }

    private static int ResolveVisibleStart(int? selectedIndex, int count, int visibleCount)
    {
        if (count <= visibleCount || selectedIndex is null)
        {
            return 0;
        }

        var start = selectedIndex.Value - 2;
        return Math.Clamp(start, 0, count - visibleCount);
    }

    private sealed record QueuedFollowUpDockEntry(
        string CorrelationId,
        string Preview);
}

internal sealed record QueuedFollowUpDockEntrySnapshot(
    int Index,
    string CorrelationId,
    string Preview);
