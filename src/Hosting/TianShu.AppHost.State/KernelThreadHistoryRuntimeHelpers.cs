using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.State;

internal sealed record KernelTrackedTurnHistory(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<KernelTurnItemRecord> Items);

internal sealed class KernelTurnHistoryAccumulator
{
    private readonly string threadId;
    private readonly string turnId;
    private readonly List<string> itemOrder = [];
    private readonly Dictionary<string, JsonObject> itemsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> recordTypesById = new(StringComparer.Ordinal);

    public KernelTurnHistoryAccumulator(string threadId, string turnId)
    {
        this.threadId = threadId;
        this.turnId = turnId;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public DateTimeOffset StartedAt { get; set; }

    public void Upsert(JsonElement item)
    {
        var payload = JsonNode.Parse(item.GetRawText()) as JsonObject;
        var itemId = NormalizeText(payload?["id"]?.GetValue<string>());
        var itemType = NormalizeText(payload?["type"]?.GetValue<string>());
        if (payload is null || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(itemType))
        {
            return;
        }

        payload["id"] = itemId;
        payload["type"] = itemType;
        recordTypesById.TryAdd(itemId!, itemType!);

        if (!itemsById.TryGetValue(itemId!, out var existing))
        {
            itemsById[itemId!] = payload;
            itemOrder.Add(itemId!);
            return;
        }

        foreach (var property in payload)
        {
            existing[property.Key] = property.Value?.DeepClone();
        }
    }

    public void OverrideItemRecordType(string itemId, string itemType)
    {
        var normalizedItemId = NormalizeText(itemId);
        var normalizedItemType = NormalizeText(itemType);
        if (string.IsNullOrWhiteSpace(normalizedItemId) || string.IsNullOrWhiteSpace(normalizedItemType))
        {
            return;
        }

        recordTypesById[normalizedItemId!] = normalizedItemType!;
    }

    public void AppendTextItem(string itemId, string itemType, string propertyName, string delta)
    {
        var item = GetOrCreateItem(itemId, itemType);
        var existing = item[propertyName]?.GetValue<string>() ?? string.Empty;
        item[propertyName] = existing + delta;
    }

    public void EnsureArraySlot(string itemId, string itemType, string propertyName, int index)
    {
        var item = GetOrCreateItem(itemId, itemType);
        var array = EnsureArray(item, propertyName);
        while (array.Count <= index)
        {
            array.Add(string.Empty);
        }
    }

    public void AppendArrayText(string itemId, string itemType, string propertyName, int index, string delta)
    {
        var item = GetOrCreateItem(itemId, itemType);
        var array = EnsureArray(item, propertyName);
        while (array.Count <= index)
        {
            array.Add(string.Empty);
        }

        var existing = array[index]?.GetValue<string>() ?? string.Empty;
        array[index] = existing + delta;
    }

    public bool TryGetItemText(string itemId, out string? text)
        => TryGetItemField(itemId, "text", out text);

    public bool TryGetItemField(string itemId, string propertyName, out string? text)
    {
        text = null;
        if (!itemsById.TryGetValue(itemId, out var item))
        {
            return false;
        }

        text = item[propertyName]?.GetValue<string>();
        return true;
    }

    public KernelTrackedTurnHistory ToSnapshot()
    {
        var items = new List<KernelTurnItemRecord>(itemOrder.Count);
        foreach (var itemId in itemOrder)
        {
            if (!itemsById.TryGetValue(itemId, out var item))
            {
                continue;
            }

            var itemType = NormalizeText(item["type"]?.GetValue<string>());
            if (string.IsNullOrWhiteSpace(itemType))
            {
                continue;
            }

            if (!recordTypesById.TryGetValue(itemId, out var recordType) || string.IsNullOrWhiteSpace(recordType))
            {
                recordType = itemType;
            }

            var payload = JsonSerializer.SerializeToElement(item);
            items.Add(new KernelTurnItemRecord
            {
                Id = itemId,
                Type = recordType!,
                Payload = payload,
            });
        }

        return new KernelTrackedTurnHistory(
            StartedAt,
            DateTimeOffset.UtcNow,
            items);
    }

    public KernelTurnRecord ToInProgressTurnRecord()
    {
        var snapshot = ToSnapshot();
        return new KernelTurnRecord
        {
            Id = turnId,
            StartedAt = snapshot.StartedAt,
            CompletedAt = snapshot.CompletedAt,
            Status = "inProgress",
            Items = snapshot.Items.ToList(),
        };
    }

    private JsonObject GetOrCreateItem(string itemId, string itemType)
    {
        if (itemsById.TryGetValue(itemId, out var existing))
        {
            return existing;
        }

        var created = new JsonObject
        {
            ["id"] = itemId,
            ["type"] = itemType,
        };
        itemsById[itemId] = created;
        itemOrder.Add(itemId);
        return created;
    }

    private static JsonArray EnsureArray(JsonObject item, string propertyName)
    {
        if (item[propertyName] is JsonArray array)
        {
            return array;
        }

        array = [];
        item[propertyName] = array;
        return array;
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

internal sealed class KernelThreadExecutionState
{
    private readonly string threadId;
    private readonly object gate = new();
    private string? currentTurnId;
    private KernelTurnHistoryAccumulator? currentTurnHistory;
    private bool rollbackPending;
    private readonly HashSet<string> pendingInterruptTurnIds = new(StringComparer.Ordinal);

    public KernelThreadExecutionState(string threadId)
    {
        this.threadId = threadId;
    }

    public bool IsEmpty
    {
        get
        {
            lock (gate)
            {
                return currentTurnHistory is null
                    && !rollbackPending
                    && pendingInterruptTurnIds.Count == 0;
            }
        }
    }

    public void MarkTurnStarted(string turnId)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).StartedAt = DateTimeOffset.UtcNow;
        }
    }

    public void SeedTurnUserMessage(string turnId, JsonElement item)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).Upsert(item);
        }
    }

    public void UpsertTurnItem(string turnId, JsonElement item)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).Upsert(item);
        }
    }

    public void OverrideTurnItemRecordType(string turnId, string itemId, string itemType)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).OverrideItemRecordType(itemId, itemType);
        }
    }

    public void AppendTurnTextItem(string turnId, string itemId, string itemType, string propertyName, string delta)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).AppendTextItem(itemId, itemType, propertyName, delta);
        }
    }

    public void EnsureTurnArraySlot(string turnId, string itemId, string itemType, string propertyName, int index)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).EnsureArraySlot(itemId, itemType, propertyName, index);
        }
    }

    public void AppendTurnArrayText(string turnId, string itemId, string itemType, string propertyName, int index, string delta)
    {
        lock (gate)
        {
            GetOrCreateTurnHistoryCore(turnId).AppendArrayText(itemId, itemType, propertyName, index, delta);
        }
    }

    public bool TryGetTrackedTurnItemText(string turnId, string itemId, out string? text)
    {
        lock (gate)
        {
            text = null;
            return TryGetTurnHistoryCore(turnId, out var history)
                && history.TryGetItemText(itemId, out text);
        }
    }

    public bool TryGetTrackedTurnItemField(string turnId, string itemId, string propertyName, out string? text)
    {
        lock (gate)
        {
            text = null;
            return TryGetTurnHistoryCore(turnId, out var history)
                && history.TryGetItemField(itemId, propertyName, out text);
        }
    }

    public KernelTrackedTurnHistory? FinalizeTurn(string turnId)
    {
        lock (gate)
        {
            pendingInterruptTurnIds.Remove(turnId);
            if (!TryGetTurnHistoryCore(turnId, out var history))
            {
                if (string.Equals(currentTurnId, turnId, StringComparison.Ordinal))
                {
                    currentTurnId = null;
                    currentTurnHistory = null;
                }

                return null;
            }

            currentTurnId = null;
            currentTurnHistory = null;
            return history.ToSnapshot();
        }
    }

    public KernelTurnRecord? BuildTrackedActiveTurnSnapshot(string turnId)
    {
        lock (gate)
        {
            return TryGetTurnHistoryCore(turnId, out var history)
                ? history.ToInProgressTurnRecord()
                : null;
        }
    }

    public bool TryBeginRollback()
    {
        lock (gate)
        {
            if (rollbackPending)
            {
                return false;
            }

            rollbackPending = true;
            return true;
        }
    }

    public void EndRollback()
    {
        lock (gate)
        {
            rollbackPending = false;
        }
    }

    public void RegisterPendingInterrupt(string turnId)
    {
        lock (gate)
        {
            pendingInterruptTurnIds.Add(turnId);
        }
    }

    public bool HasPendingInterrupt(string turnId)
    {
        lock (gate)
        {
            return pendingInterruptTurnIds.Contains(turnId);
        }
    }

    public void ClearPendingInterrupt(string turnId)
    {
        lock (gate)
        {
            pendingInterruptTurnIds.Remove(turnId);
        }
    }

    public bool HasTrackedTurnActivity(string turnId)
    {
        lock (gate)
        {
            return TryGetTurnHistoryCore(turnId, out _)
                || pendingInterruptTurnIds.Contains(turnId);
        }
    }

    private KernelTurnHistoryAccumulator GetOrCreateTurnHistoryCore(string turnId)
    {
        if (TryGetTurnHistoryCore(turnId, out var history))
        {
            return history;
        }

        currentTurnId = turnId;
        currentTurnHistory = new KernelTurnHistoryAccumulator(threadId, turnId);
        return currentTurnHistory;
    }

    private bool TryGetTurnHistoryCore(string turnId, out KernelTurnHistoryAccumulator history)
    {
        if (currentTurnHistory is not null && string.Equals(currentTurnId, turnId, StringComparison.Ordinal))
        {
            history = currentTurnHistory;
            return true;
        }

        history = null!;
        return false;
    }
}
