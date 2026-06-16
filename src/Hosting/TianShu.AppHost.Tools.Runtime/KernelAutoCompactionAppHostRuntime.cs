using TianShu.AppHost.State;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelAutoCompactionAppHostRuntime
{
    private const int AutoCompactKeepRecentTurns = 12;

    private readonly KernelThreadStore threadStore;
    private readonly Func<string?, long?> resolveConfiguredModelAutoCompactTokenLimit;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelAutoCompactionAppHostRuntime(
        KernelThreadStore threadStore,
        Func<string?, long?> resolveConfiguredModelAutoCompactTokenLimit,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.threadStore = threadStore;
        this.resolveConfiguredModelAutoCompactTokenLimit = resolveConfiguredModelAutoCompactTokenLimit;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task MaybeRunPreSamplingAutoCompactAsync(
        string threadId,
        string pendingUserText,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var autoCompactLimit = resolveConfiguredModelAutoCompactTokenLimit(context.Cwd);
        if (autoCompactLimit is null || autoCompactLimit <= 0)
        {
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var estimatedPromptTokens = KernelAutoCompactionRuntimeHelpers.EstimatePromptTokenCount(
            KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(context),
            KernelTurnExecutionRuntimeHelpers.BuildProviderMessages(
                thread,
                pendingUserText,
                KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(context, false),
                context.ExplicitSkillInjections,
                null,
                KernelPromptContentFormat.PlainText));
        if (estimatedPromptTokens < autoCompactLimit.Value)
        {
            return;
        }

        _ = await TryCompactThreadWithNotificationsAsync(
            threadId,
            AutoCompactKeepRecentTurns,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<object>?> MaybeBuildMidTurnAutoCompactedFollowUpInputAsync(
        string threadId,
        string pendingUserText,
        IReadOnlyList<object> responseItems,
        IReadOnlyList<object> nextInput,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        if (nextInput.Count == 0)
        {
            return null;
        }

        var autoCompactLimit = resolveConfiguredModelAutoCompactTokenLimit(context.Cwd);
        if (autoCompactLimit is null || autoCompactLimit <= 0)
        {
            return null;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var priorInput = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(
            thread,
            pendingUserText,
            KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(context, false),
            context.ExplicitSkillInjections,
            context.InputItems);
        var followUpInput = KernelAutoCompactionRuntimeHelpers.BuildResponsesFollowUpInput(priorInput, responseItems, nextInput);
        var estimatedFollowUpTokens = KernelAutoCompactionRuntimeHelpers.EstimateResponsesFollowUpTokenCount(
            KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(context),
            followUpInput);
        if (estimatedFollowUpTokens < autoCompactLimit.Value)
        {
            return null;
        }

        var compacted = await TryCompactThreadWithNotificationsAsync(
            threadId,
            AutoCompactKeepRecentTurns,
            cancellationToken).ConfigureAwait(false);
        if (!compacted)
        {
            return null;
        }

        var compactedThread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var compactedPriorInput = KernelTurnExecutionRuntimeHelpers.BuildResponsesConversationInput(
            compactedThread,
            pendingUserText,
            KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(context, false),
            context.ExplicitSkillInjections,
            context.InputItems);
        return KernelAutoCompactionRuntimeHelpers.BuildResponsesFollowUpInput(compactedPriorInput, responseItems, nextInput);
    }

    private async Task<bool> TryCompactThreadWithNotificationsAsync(
        string threadId,
        int keepRecentTurns,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        var effectiveKeepRecentTurns = Math.Clamp(keepRecentTurns, 1, 64);
        if (record is null || record.Turns.Count <= effectiveKeepRecentTurns)
        {
            return false;
        }

        var compactionTurnId = $"compact_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";
        var compactionItemId = $"context_compaction_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId = compactionTurnId,
            item = new
            {
                id = compactionItemId,
                type = "contextCompaction",
            },
        }, CancellationToken.None).ConfigureAwait(false);

        var compacted = await threadStore
            .CompactThreadAsync(threadId, effectiveKeepRecentTurns, cancellationToken, compactionTurnId)
            .ConfigureAwait(false);
        var effectiveTurnId = compacted?.Turns
            .FirstOrDefault(static turn => turn.Id.StartsWith("compact_", StringComparison.OrdinalIgnoreCase))
            ?.Id
            ?? compactionTurnId;

        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId = effectiveTurnId,
            item = new
            {
                id = compactionItemId,
                type = "contextCompaction",
            },
        }, CancellationToken.None).ConfigureAwait(false);
        await writeNotificationAsync("thread/compacted", new
        {
            threadId,
            turnId = effectiveTurnId,
        }, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
