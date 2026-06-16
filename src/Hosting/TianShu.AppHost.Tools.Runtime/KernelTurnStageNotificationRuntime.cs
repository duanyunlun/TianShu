namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn Stage notification 运行时，负责把 Stage 执行中的用户可见事件投影为 AppHost notification。
/// Turn Stage notification runtime that projects user-visible events during Stage execution into AppHost notifications.
/// </summary>
internal sealed class KernelTurnStageNotificationRuntime
{
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, string, string, string?, string> getTrackedAgentMessageText;
    private readonly Func<TurnOperationState, Task> ensureAgentMessageStartedAsync;
    private readonly Func<TurnOperationState, Task> completePlanItemAsync;

    public KernelTurnStageNotificationRuntime(
        Func<string?, string?> normalize,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, string, string, string?, string> getTrackedAgentMessageText,
        Func<TurnOperationState, Task> ensureAgentMessageStartedAsync,
        Func<TurnOperationState, Task> completePlanItemAsync)
    {
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        this.getTrackedAgentMessageText = getTrackedAgentMessageText ?? throw new ArgumentNullException(nameof(getTrackedAgentMessageText));
        this.ensureAgentMessageStartedAsync = ensureAgentMessageStartedAsync ?? throw new ArgumentNullException(nameof(ensureAgentMessageStartedAsync));
        this.completePlanItemAsync = completePlanItemAsync ?? throw new ArgumentNullException(nameof(completePlanItemAsync));
    }

    public async Task PublishTurnStartedAsync(string threadId, string turnId)
        => await writeNotificationAsync("turn/started", new
        {
            threadId,
            turn = new
            {
                id = turnId,
                status = "inProgress",
                items = Array.Empty<object>(),
            },
        }, CancellationToken.None).ConfigureAwait(false);

    public async Task PublishReviewEnteredAsync(
        string threadId,
        string turnId,
        string reviewEnterItemId,
        string? reviewDisplayText)
    {
        var reviewItem = new
        {
            id = reviewEnterItemId,
            type = "enteredReviewMode",
            review = normalize(reviewDisplayText) ?? string.Empty,
        };

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item = reviewItem,
        }, CancellationToken.None).ConfigureAwait(false);
        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item = reviewItem,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task PublishCompletedAgentMessageAsync(
        TurnOperationState state,
        string assistantText)
    {
        if (!state.AgentMessageStarted && string.IsNullOrWhiteSpace(assistantText))
        {
            return;
        }

        await ensureAgentMessageStartedAsync(state).ConfigureAwait(false);

        var providerReasoningContent = normalize(state.ProviderReasoningContent.ToString());
        await writeNotificationAsync("rawResponseItem/completed", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.ItemId,
                type = "agentMessage",
                status = "completed",
                text = assistantText,
                reasoning_content = providerReasoningContent,
            },
        }, CancellationToken.None).ConfigureAwait(false);

        await writeNotificationAsync("item/completed", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.ItemId,
                type = "agentMessage",
                text = assistantText,
                reasoning_content = providerReasoningContent,
                phase = (string?)null,
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task PublishInterruptedAgentMessageAsync(TurnOperationState state)
    {
        var interruptedText = getTrackedAgentMessageText(state.ThreadId, state.TurnId, state.ItemId, null);
        await PublishTerminalAgentMessageAsync(state, interruptedText).ConfigureAwait(false);
    }

    public async Task PublishFailedAgentMessageAsync(
        TurnOperationState state,
        string reviewFailureMessage)
    {
        var failedText = getTrackedAgentMessageText(state.ThreadId, state.TurnId, state.ItemId, reviewFailureMessage);
        await PublishTerminalAgentMessageAsync(state, failedText).ConfigureAwait(false);
    }

    public async Task PublishTokenUsageUpdatedAsync(
        string threadId,
        string turnId,
        string effectiveUserText,
        string assistantText)
        => await writeNotificationAsync("thread/tokenUsage/updated", new
        {
            threadId,
            turnId,
            tokenUsage = BuildTokenUsagePayload(effectiveUserText, assistantText),
        }, CancellationToken.None).ConfigureAwait(false);

    public async Task CompletePlanItemAsync(TurnOperationState state)
        => await completePlanItemAsync(state).ConfigureAwait(false);

    public async Task PublishErrorAsync(
        string threadId,
        string turnId,
        string message)
        => await writeNotificationAsync("error", new
        {
            threadId,
            turnId,
            message,
            error = new
            {
                message,
            },
            willRetry = false,
        }, CancellationToken.None).ConfigureAwait(false);

    private async Task PublishTerminalAgentMessageAsync(
        TurnOperationState state,
        string text)
    {
        if (!state.AgentMessageStarted && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await ensureAgentMessageStartedAsync(state).ConfigureAwait(false);
        await writeNotificationAsync("item/completed", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            item = new
            {
                id = state.ItemId,
                type = "agentMessage",
                text,
                phase = (string?)null,
            },
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private static object BuildTokenUsagePayload(string inputText, string outputText)
    {
        static object ToBreakdown(int inputLength, int outputLength)
            => new
            {
                totalTokens = Math.Max(1, (inputLength + outputLength) / 3),
                inputTokens = Math.Max(1, inputLength / 3),
                cachedInputTokens = 0,
                outputTokens = Math.Max(1, outputLength / 3),
                reasoningOutputTokens = Math.Max(1, outputLength / 6),
            };

        var inputLength = NormalizeText(inputText)?.Length ?? 0;
        var outputLength = NormalizeText(outputText)?.Length ?? 0;

        return new
        {
            total = ToBreakdown(inputLength, outputLength),
            last = ToBreakdown(inputLength, outputLength),
            modelContextWindow = 128000,
            estimated = true,
            source = "text_length_estimate",
        };
    }

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
