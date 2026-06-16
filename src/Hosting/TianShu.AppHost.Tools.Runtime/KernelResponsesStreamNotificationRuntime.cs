using System.Text.Json;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses stream 通知运行时，负责把 provider stream 事件投影为 AppHost item 通知。
/// Runtime that projects provider response stream events into AppHost item notifications.
/// </summary>
internal sealed class KernelResponsesStreamNotificationRuntime
{
    private readonly Func<TurnOperationState, Task> ensureAgentMessageStartedAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelResponsesStreamNotificationRuntime(
        Func<TurnOperationState, Task> ensureAgentMessageStartedAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.ensureAgentMessageStartedAsync = ensureAgentMessageStartedAsync ?? throw new ArgumentNullException(nameof(ensureAgentMessageStartedAsync));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
    }

    public async Task EmitPlanDeltaAsync(TurnOperationState state, string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        if (!state.PlanItemStarted && !state.PlanItemCompleted)
        {
            state.PlanItemStarted = true;
            await writeNotificationAsync("item/started", new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                item = new
                {
                    id = state.PlanItemId,
                    type = "plan",
                    text = string.Empty,
                },
            }, CancellationToken.None).ConfigureAwait(false);
        }

        await writeNotificationAsync("item/plan/delta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.PlanItemId,
            delta,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<string?> EmitAssistantDeltaAsync(TurnOperationState state, string delta)
    {
        delta = NormalizeVisibleAssistantDelta(state, delta) ?? string.Empty;
        if (string.IsNullOrEmpty(delta))
        {
            return null;
        }

        await ensureAgentMessageStartedAsync(state).ConfigureAwait(false);
        await writeNotificationAsync("item/agentMessage/delta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ItemId,
            delta,
            item = new
            {
                id = state.ItemId,
                type = "agentMessage",
                delta,
            },
        }, CancellationToken.None).ConfigureAwait(false);

        await writeNotificationAsync("item/delta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ItemId,
            type = "assistant_text",
            delta,
            item = new
            {
                id = state.ItemId,
                type = "assistant_text",
                delta,
            },
        }, CancellationToken.None).ConfigureAwait(false);

        return delta;
    }

    public async Task EmitProviderReasoningDeltaAsync(TurnOperationState state, string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return;
        }

        state.ProviderReasoningContent.Append(delta);
        await writeNotificationAsync("item/reasoning/textDelta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ReasoningItemId,
            delta,
            contentIndex = 0,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public Task EmitReasoningSummaryPartAddedAsync(TurnOperationState state, int summaryIndex)
        => writeNotificationAsync("item/reasoning/summaryPartAdded", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ReasoningItemId,
            summaryIndex,
        }, CancellationToken.None);

    public Task EmitReasoningSummaryTextDeltaAsync(TurnOperationState state, string delta, int summaryIndex)
        => writeNotificationAsync("item/reasoning/summaryTextDelta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ReasoningItemId,
            delta,
            summaryIndex,
        }, CancellationToken.None);

    public Task EmitReasoningTextDeltaAsync(TurnOperationState state, string delta, int contentIndex)
        => writeNotificationAsync("item/reasoning/textDelta", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            itemId = state.ReasoningItemId,
            delta,
            contentIndex,
        }, CancellationToken.None);

    public async Task EmitPresentableOutputItemNotificationAsync(
        TurnOperationState state,
        string method,
        JsonElement item,
        CancellationToken cancellationToken)
    {
        if (!ProviderOutputItemPresentationPolicy.ShouldEmitLifecycleNotification(item))
        {
            return;
        }

        await writeNotificationAsync(method, new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = item.Clone(),
        }, cancellationToken).ConfigureAwait(false);
    }

    private static string? NormalizeVisibleAssistantDelta(TurnOperationState state, string? delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return null;
        }

        if (!state.IsPlanMode || state.AgentMessageStarted)
        {
            return delta;
        }

        if (string.IsNullOrWhiteSpace(delta))
        {
            state.PendingLeadingAgentWhitespace += delta;
            return null;
        }

        if (state.PendingLeadingAgentWhitespace.Length == 0)
        {
            return delta;
        }

        var combined = state.PendingLeadingAgentWhitespace + delta;
        state.PendingLeadingAgentWhitespace = string.Empty;
        return combined;
    }
}
