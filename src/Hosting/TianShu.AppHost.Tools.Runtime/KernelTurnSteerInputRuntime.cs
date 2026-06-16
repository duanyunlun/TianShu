namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn steer input 运行时，负责合并 late steer input 并发布对应用户消息投影。
/// Runtime that merges late steer input and emits committed user message projections.
/// </summary>
internal sealed class KernelTurnSteerInputRuntime
{
    private readonly Func<string, IReadOnlyList<string>> drainSteerInputs;
    private readonly Func<string, string, string, object> createResponsesMessage;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<long> nextUserMessageItemSequence;
    private readonly Func<string?, string?> normalize;

    public KernelTurnSteerInputRuntime(
        Func<string, IReadOnlyList<string>> drainSteerInputs,
        Func<string, string, string, object> createResponsesMessage,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<long> nextUserMessageItemSequence,
        Func<string?, string?> normalize)
    {
        this.drainSteerInputs = drainSteerInputs ?? throw new ArgumentNullException(nameof(drainSteerInputs));
        this.createResponsesMessage = createResponsesMessage ?? throw new ArgumentNullException(nameof(createResponsesMessage));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        this.nextUserMessageItemSequence = nextUserMessageItemSequence ?? throw new ArgumentNullException(nameof(nextUserMessageItemSequence));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
    }

    public IReadOnlyList<string> DrainSteerInputs(string turnId)
        => drainSteerInputs(turnId);

    public async Task<string> MergeSteerInputsAsync(string turnId, string baseInput, CancellationToken cancellationToken)
    {
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        var steerInputs = drainSteerInputs(turnId);
        return AppendUserInputText(baseInput, steerInputs, normalize);
    }

    public bool TryMergeSteerInputsImmediately(string turnId, string baseInput, out string mergedInput)
    {
        mergedInput = baseInput;
        var steerInputs = drainSteerInputs(turnId);
        if (steerInputs.Count == 0)
        {
            return false;
        }

        mergedInput = AppendUserInputText(baseInput, steerInputs, normalize);
        return !string.Equals(mergedInput, baseInput, StringComparison.Ordinal);
    }

    public Task PublishLateSteerAcceptedAsync(string threadId, string turnId, int iteration)
        => writeNotificationAsync("turn/steered", new
        {
            threadId,
            turnId,
            status = "accepted",
            source = "late_steer_input",
            iteration,
        }, CancellationToken.None);

    public async Task<List<object>> AppendSteerInputsToResponsesFollowUpAsync(
        TurnOperationState state,
        IReadOnlyList<object> followUpInput,
        IReadOnlyList<string> steerInputs)
    {
        var updatedInput = new List<object>(followUpInput.Count + steerInputs.Count);
        updatedInput.AddRange(followUpInput);

        state.OriginalUserText = AppendUserInputText(state.OriginalUserText, steerInputs, normalize);
        state.EffectiveUserText = AppendUserInputText(state.EffectiveUserText, steerInputs, normalize);

        await writeNotificationAsync("turn/steered", new
        {
            threadId = state.ThreadId,
            turnId = state.TurnId,
            status = "accepted",
            source = "after_next_tool_call",
            count = steerInputs.Count,
        }, CancellationToken.None).ConfigureAwait(false);

        foreach (var steerText in steerInputs)
        {
            updatedInput.Add(createResponsesMessage("user", "input_text", steerText));
            await WriteCommittedUserMessageItemAsync(state.ThreadId, state.TurnId, steerText).ConfigureAwait(false);
        }

        return updatedInput;
    }

    public async Task WriteCommittedUserMessageItemAsync(string threadId, string turnId, string text)
    {
        var item = new
        {
            id = NextUserMessageItemId(turnId),
            type = "userMessage",
            content = new object[]
            {
                new
                {
                    type = "text",
                    text,
                    text_elements = Array.Empty<object>(),
                },
            },
        };

        await writeNotificationAsync("item/started", new
        {
            threadId,
            turnId,
            item,
        }, CancellationToken.None).ConfigureAwait(false);

        await writeNotificationAsync("item/completed", new
        {
            threadId,
            turnId,
            item,
        }, CancellationToken.None).ConfigureAwait(false);
    }

    public string NextUserMessageItemId(string turnId)
    {
        var sequence = nextUserMessageItemSequence();
        return $"user_{turnId}_{sequence}";
    }

    private static string AppendUserInputText(
        string baseInput,
        IReadOnlyList<string> additionalInputs,
        Func<string?, string?> normalize)
    {
        if (additionalInputs.Count == 0)
        {
            return baseInput;
        }

        var parts = new List<string>(additionalInputs.Count + 1);
        var normalizedBase = normalize(baseInput);
        if (!string.IsNullOrWhiteSpace(normalizedBase))
        {
            parts.Add(normalizedBase!);
        }

        foreach (var input in additionalInputs)
        {
            var normalizedInput = normalize(input);
            if (!string.IsNullOrWhiteSpace(normalizedInput))
            {
                parts.Add(normalizedInput!);
            }
        }

        return parts.Count == 0
            ? baseInput
            : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }
}
