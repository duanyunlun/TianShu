namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses follow-up input 运行时，负责工具调用后的下一轮 request input 构造。
/// Runtime that builds the next Responses request input after tool calls complete.
/// </summary>
internal sealed class KernelResponsesFollowUpInputRuntime
{
    private readonly Func<string, string, IReadOnlyList<object>, IReadOnlyList<object>, TurnRequestContext, CancellationToken, Task<List<object>?>> maybeBuildMidTurnAutoCompactedFollowUpInputAsync;
    private readonly KernelTurnSteerInputRuntime steerInputRuntime;

    public KernelResponsesFollowUpInputRuntime(
        Func<string, string, IReadOnlyList<object>, IReadOnlyList<object>, TurnRequestContext, CancellationToken, Task<List<object>?>> maybeBuildMidTurnAutoCompactedFollowUpInputAsync,
        KernelTurnSteerInputRuntime steerInputRuntime)
    {
        this.maybeBuildMidTurnAutoCompactedFollowUpInputAsync = maybeBuildMidTurnAutoCompactedFollowUpInputAsync
                                                                ?? throw new ArgumentNullException(nameof(maybeBuildMidTurnAutoCompactedFollowUpInputAsync));
        this.steerInputRuntime = steerInputRuntime ?? throw new ArgumentNullException(nameof(steerInputRuntime));
    }

    public async Task<KernelResponsesFollowUpInputResult> BuildAsync(
        TurnOperationState state,
        IReadOnlyList<object> requestInput,
        IReadOnlyList<object> responseItems,
        IReadOnlyList<object> nextInput,
        TurnRequestContext context,
        ContextBudgetProfile budgetProfile,
        string model,
        CancellationToken cancellationToken)
    {
        var compactedFollowUpInput = await maybeBuildMidTurnAutoCompactedFollowUpInputAsync(
            state.ThreadId,
            state.EffectiveUserText,
            responseItems,
            nextInput,
            context,
            cancellationToken).ConfigureAwait(false);

        List<object> nextRequestInput;
        ContextSlicingReport? requestSlicingReport;
        if (compactedFollowUpInput is not null)
        {
            nextRequestInput = compactedFollowUpInput;
            requestSlicingReport = null;
        }
        else
        {
            var slicedFollowUpInput = KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesFollowUpInput(
                requestInput,
                responseItems,
                nextInput,
                state.ThreadId,
                state.TurnId,
                budgetProfile,
                modelId: model,
                providerId: context.ModelProvider);
            nextRequestInput = slicedFollowUpInput.Input;
            requestSlicingReport = slicedFollowUpInput.Report;
        }

        var steerInputs = steerInputRuntime.DrainSteerInputs(state.TurnId);
        if (steerInputs.Count > 0)
        {
            nextRequestInput = await steerInputRuntime.AppendSteerInputsToResponsesFollowUpAsync(
                state,
                nextRequestInput,
                steerInputs).ConfigureAwait(false);
        }

        return new KernelResponsesFollowUpInputResult(nextRequestInput, requestSlicingReport);
    }
}

internal sealed record KernelResponsesFollowUpInputResult(
    List<object> Input,
    ContextSlicingReport? Report);
