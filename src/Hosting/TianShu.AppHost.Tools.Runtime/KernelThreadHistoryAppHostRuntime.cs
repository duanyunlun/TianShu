using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.Execution.Runtime;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelThreadHistoryAppHostRuntime
{
    private readonly JsonSerializerOptions jsonOptions;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly ConcurrentDictionary<string, KernelThreadExecutionState> executionStatesByThread = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<JsonElement>> pendingTurnInterruptResponseIds = new(StringComparer.Ordinal);

    public KernelThreadHistoryAppHostRuntime(
        JsonSerializerOptions jsonOptions,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns)
    {
        this.jsonOptions = jsonOptions;
        this.runningTurns = runningTurns;
    }

    public void TryTrackTurnNotification(string method, object @params)
    {
        try
        {
            var parameters = JsonSerializer.SerializeToElement(@params, jsonOptions);
            TryTrackTurnNotification(method, parameters);
        }
        catch
        {
            // 历史跟踪只用于补齐 thread/read/resume/fork，不应影响主链路输出。
        }
    }

    public void TryTrackTurnNotification(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "turn/started":
                TrackTurnStarted(parameters);
                return;
            case "item/started":
            case "item/completed":
                TrackTurnItemNotification(parameters);
                return;
            case "item/agentMessage/delta":
                TrackAgentMessageDelta(parameters);
                return;
            case "item/plan/delta":
                TrackPlanDelta(parameters);
                return;
            case "item/reasoning/summaryPartAdded":
                TrackReasoningSummaryPartAdded(parameters);
                return;
            case "item/reasoning/summaryTextDelta":
                TrackReasoningSummaryTextDelta(parameters);
                return;
            case "item/reasoning/textDelta":
                TrackReasoningTextDelta(parameters);
                return;
        }
    }

    public void SeedTrackedTurnUserMessage(
        string threadId,
        string turnId,
        string? userText,
        IReadOnlyList<KernelTurnInputItem>? inputItems = null)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        var normalizedUserText = Normalize(userText);
        var serializedInputItems = inputItems?
            .Select(KernelThreadTransportParsers.SerializeTurnInputItem)
            .ToArray();
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedTurnId)
            || (string.IsNullOrWhiteSpace(normalizedUserText)
                && (serializedInputItems is null || serializedInputItems.Length == 0)))
        {
            return;
        }

        GetThreadExecutionState(normalizedThreadId!).SeedTurnUserMessage(normalizedTurnId!, JsonSerializer.SerializeToElement(new
        {
            id = normalizedTurnId,
            type = "userMessage",
            content = serializedInputItems is { Length: > 0 }
                ? serializedInputItems
                : new object[]
                {
                    new
                    {
                        type = "text",
                        text = normalizedUserText,
                    },
                },
        }));
    }

    public KernelTrackedTurnHistory? FinalizeTrackedTurnHistory(string threadId, string turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || !executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState))
        {
            return null;
        }

        var snapshot = executionState.FinalizeTurn(turnId);
        TryCleanupThreadExecutionState(normalizedThreadId);
        return snapshot;
    }

    public void OverrideTrackedTurnItemRecordType(string threadId, string turnId, string itemId, string recordType)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        var normalizedItemId = Normalize(itemId);
        var normalizedRecordType = Normalize(recordType);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedTurnId)
            || string.IsNullOrWhiteSpace(normalizedItemId)
            || string.IsNullOrWhiteSpace(normalizedRecordType))
        {
            return;
        }

        GetThreadExecutionState(normalizedThreadId!).OverrideTurnItemRecordType(
            normalizedTurnId!,
            normalizedItemId!,
            normalizedRecordType!);
    }

    public KernelTurnRecord? BuildTrackedActiveTurnSnapshot(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedTurnId)
            || !executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState))
        {
            return null;
        }

        return executionState.BuildTrackedActiveTurnSnapshot(normalizedTurnId!);
    }

    public string GetTrackedAgentMessageText(string threadId, string turnId, string itemId, string? fallback = null)
    {
        var normalizedThreadId = Normalize(threadId);
        if (!string.IsNullOrWhiteSpace(normalizedThreadId)
            && executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState)
            && executionState.TryGetTrackedTurnItemText(turnId, itemId, out var text)
            && !string.IsNullOrWhiteSpace(text))
        {
            return text!;
        }

        return Normalize(fallback) ?? string.Empty;
    }

    public string GetTrackedPlanText(string threadId, string turnId, string itemId, string? fallback = null)
    {
        var normalizedThreadId = Normalize(threadId);
        if (!string.IsNullOrWhiteSpace(normalizedThreadId)
            && executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState)
            && executionState.TryGetTrackedTurnItemField(turnId, itemId, "text", out var text)
            && !string.IsNullOrWhiteSpace(text))
        {
            return text!;
        }

        return Normalize(fallback) ?? string.Empty;
    }

    public bool TryBeginThreadRollback(string threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            return false;
        }

        return GetThreadExecutionState(normalizedThreadId!).TryBeginRollback();
    }

    public void EndThreadRollback(string threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || !executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState))
        {
            return;
        }

        executionState.EndRollback();
        TryCleanupThreadExecutionState(normalizedThreadId);
    }

    public void RegisterPendingTurnInterrupt(string threadId, string turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return;
        }

        GetThreadExecutionState(normalizedThreadId!).RegisterPendingInterrupt(normalizedTurnId!);
    }

    public bool HasPendingTurnInterrupt(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        return !string.IsNullOrWhiteSpace(normalizedThreadId)
            && !string.IsNullOrWhiteSpace(normalizedTurnId)
            && executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState)
            && executionState.HasPendingInterrupt(normalizedTurnId!);
    }

    public void ClearPendingTurnInterrupt(string threadId, string turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || string.IsNullOrWhiteSpace(normalizedTurnId)
            || !executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState))
        {
            return;
        }

        executionState.ClearPendingInterrupt(normalizedTurnId!);
        TryCleanupThreadExecutionState(normalizedThreadId);
    }

    public void RegisterPendingTurnInterruptResponse(string threadId, string turnId, JsonElement responseId)
    {
        var key = BuildPendingTurnInterruptResponseKey(threadId, turnId);
        if (key is null)
        {
            return;
        }

        pendingTurnInterruptResponseIds
            .GetOrAdd(key, static _ => new ConcurrentQueue<JsonElement>())
            .Enqueue(responseId.Clone());
    }

    public IReadOnlyList<JsonElement> DrainPendingTurnInterruptResponses(string? threadId, string? turnId)
    {
        var key = BuildPendingTurnInterruptResponseKey(threadId, turnId);
        if (key is null || !pendingTurnInterruptResponseIds.TryRemove(key, out var queue))
        {
            return Array.Empty<JsonElement>();
        }

        var drained = new List<JsonElement>();
        while (queue.TryDequeue(out var responseId))
        {
            drained.Add(responseId);
        }

        return drained;
    }

    public void ClearPendingTurnInterruptResponses(string? threadId, string? turnId)
    {
        var key = BuildPendingTurnInterruptResponseKey(threadId, turnId);
        if (key is null)
        {
            return;
        }

        pendingTurnInterruptResponseIds.TryRemove(key, out _);
    }

    public bool HasTrackedTurnActivity(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        return !string.IsNullOrWhiteSpace(normalizedThreadId)
            && !string.IsNullOrWhiteSpace(normalizedTurnId)
            && executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState)
            && executionState.HasTrackedTurnActivity(normalizedTurnId!);
    }

    private void TrackTurnStarted(JsonElement parameters)
    {
        var threadId = Normalize(ReadString(parameters, "threadId"));
        var turnId = TryReadObject(parameters, "turn", out var turn)
            ? Normalize(ReadString(turn, "id"))
            : Normalize(ReadString(parameters, "turnId"));
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
        {
            return;
        }

        GetThreadExecutionState(threadId!).MarkTurnStarted(turnId!);
    }

    private void TrackTurnItemNotification(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        if (!TryReadJsonProperty(parameters, "item", out var item) || item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        executionState.UpsertTurnItem(turnId, item);
    }

    private void TrackAgentMessageDelta(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        var itemId = Normalize(ReadString(parameters, "itemId"));
        var delta = ReadString(parameters, "delta") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(itemId) || delta.Length == 0)
        {
            return;
        }

        executionState.AppendTurnTextItem(turnId, itemId!, "agentMessage", "text", delta);
    }

    private void TrackPlanDelta(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        var itemId = Normalize(ReadString(parameters, "itemId"));
        var delta = ReadString(parameters, "delta") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(itemId) || delta.Length == 0)
        {
            return;
        }

        executionState.AppendTurnTextItem(turnId, itemId!, "plan", "text", delta);
    }

    private void TrackReasoningSummaryPartAdded(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        var itemId = Normalize(ReadString(parameters, "itemId"));
        var summaryIndex = ReadInt(parameters, "summaryIndex");
        if (string.IsNullOrWhiteSpace(itemId) || summaryIndex is null || summaryIndex < 0)
        {
            return;
        }

        executionState.EnsureTurnArraySlot(turnId, itemId!, "reasoning", "summary", summaryIndex.Value);
    }

    private void TrackReasoningSummaryTextDelta(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        var itemId = Normalize(ReadString(parameters, "itemId"));
        var delta = ReadString(parameters, "delta") ?? string.Empty;
        var summaryIndex = ReadInt(parameters, "summaryIndex");
        if (string.IsNullOrWhiteSpace(itemId) || summaryIndex is null || summaryIndex < 0 || delta.Length == 0)
        {
            return;
        }

        executionState.AppendTurnArrayText(turnId, itemId!, "reasoning", "summary", summaryIndex.Value, delta);
    }

    private void TrackReasoningTextDelta(JsonElement parameters)
    {
        if (!TryResolveTrackedTurn(parameters, out var executionState, out var turnId))
        {
            return;
        }

        var itemId = Normalize(ReadString(parameters, "itemId"));
        var delta = ReadString(parameters, "delta") ?? string.Empty;
        var contentIndex = ReadInt(parameters, "contentIndex");
        if (string.IsNullOrWhiteSpace(itemId) || contentIndex is null || contentIndex < 0 || delta.Length == 0)
        {
            return;
        }

        executionState.AppendTurnArrayText(turnId, itemId!, "reasoning", "content", contentIndex.Value, delta);
    }

    private bool TryResolveTrackedTurn(JsonElement parameters, out KernelThreadExecutionState executionState, out string turnId)
    {
        executionState = null!;
        turnId = string.Empty;
        var threadId = Normalize(ReadString(parameters, "threadId"));
        var normalizedTurnId = Normalize(ReadString(parameters, "turnId"));
        if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(normalizedTurnId))
        {
            return false;
        }

        turnId = normalizedTurnId!;
        if (runningTurns.ContainsKey(turnId))
        {
            executionState = GetThreadExecutionState(threadId!);
            return true;
        }

        if (!executionStatesByThread.TryGetValue(threadId!, out var existingState)
            || !existingState.HasTrackedTurnActivity(turnId))
        {
            return false;
        }

        executionState = existingState;
        return true;
    }

    private KernelThreadExecutionState GetThreadExecutionState(string threadId)
        => executionStatesByThread.GetOrAdd(threadId, static id => new KernelThreadExecutionState(id));

    private string? BuildPendingTurnInterruptResponseKey(string? threadId, string? turnId)
    {
        var normalizedThreadId = Normalize(threadId);
        var normalizedTurnId = Normalize(turnId);
        return string.IsNullOrWhiteSpace(normalizedThreadId) || string.IsNullOrWhiteSpace(normalizedTurnId)
            ? null
            : $"{normalizedThreadId}:{normalizedTurnId}";
    }

    private void TryCleanupThreadExecutionState(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId)
            || !executionStatesByThread.TryGetValue(normalizedThreadId!, out var executionState)
            || !executionState.IsEmpty)
        {
            return;
        }

        executionStatesByThread.TryRemove(normalizedThreadId!, out _);
    }

    private static bool TryReadJsonProperty(JsonElement json, string propertyName, out JsonElement value)
    {
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property))
        {
            value = property;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        if (TryReadJsonProperty(json, propertyName, out var property) && property.ValueKind == JsonValueKind.Object)
        {
            value = property;
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out var property)
            ? property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                JsonValueKind.Null => null,
                _ => null,
            }
            : null;

    private static int? ReadInt(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var parsed) => parsed,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }
}
