namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn operation chain 运行时，负责 turn 内部操作序列与 late steer 重新进入。
/// Runtime that orchestrates the per-turn operation sequence and late-steer re-entry.
/// </summary>
internal sealed class KernelTurnOperationChainRuntime
{
    private static readonly TurnOperationKind[] Operations =
    [
        TurnOperationKind.ResolveInput,
        TurnOperationKind.ResolveDependencies,
        TurnOperationKind.ExecuteAssistant,
        TurnOperationKind.StreamAssistantOutput,
    ];

    private readonly KernelTurnInputResolutionRuntime inputResolutionRuntime;
    private readonly KernelTurnDependencyResolutionRuntime dependencyResolutionRuntime;
    private readonly KernelTurnAssistantExecutionRuntime assistantExecutionRuntime;
    private readonly KernelTurnAssistantOutputStreamingRuntime assistantOutputStreamingRuntime;
    private readonly KernelTurnSteerInputRuntime steerInputRuntime;

    public KernelTurnOperationChainRuntime(
        KernelTurnInputResolutionRuntime inputResolutionRuntime,
        KernelTurnDependencyResolutionRuntime dependencyResolutionRuntime,
        KernelTurnAssistantExecutionRuntime assistantExecutionRuntime,
        KernelTurnAssistantOutputStreamingRuntime assistantOutputStreamingRuntime,
        KernelTurnSteerInputRuntime steerInputRuntime)
    {
        this.inputResolutionRuntime = inputResolutionRuntime ?? throw new ArgumentNullException(nameof(inputResolutionRuntime));
        this.dependencyResolutionRuntime = dependencyResolutionRuntime ?? throw new ArgumentNullException(nameof(dependencyResolutionRuntime));
        this.assistantExecutionRuntime = assistantExecutionRuntime ?? throw new ArgumentNullException(nameof(assistantExecutionRuntime));
        this.assistantOutputStreamingRuntime = assistantOutputStreamingRuntime ?? throw new ArgumentNullException(nameof(assistantOutputStreamingRuntime));
        this.steerInputRuntime = steerInputRuntime ?? throw new ArgumentNullException(nameof(steerInputRuntime));
    }

    public async Task<TurnOperationState> ExecuteAsync(
        TurnOperationState state,
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        using var dependencyScope = KernelDependencyEnvironmentScope.Push(turnContext.DependencyEnvironment);
        var effectiveContext = turnContext;
        const int maxPasses = 3;
        for (var pass = 0; pass < maxPasses; pass++)
        {
            foreach (var operation in Operations)
            {
                effectiveContext = await ExecuteOperationAsync(state, operation, effectiveContext, cancellationToken).ConfigureAwait(false);
            }

            if (!steerInputRuntime.TryMergeSteerInputsImmediately(state.TurnId, state.EffectiveUserText, out var steeredInput))
            {
                break;
            }

            state.OriginalUserText = steeredInput;
            state.EffectiveUserText = steeredInput;

            await steerInputRuntime
                .PublishLateSteerAcceptedAsync(state.ThreadId, state.TurnId, pass + 1)
                .ConfigureAwait(false);
        }

        return state;
    }

    public async Task<TurnRequestContext> ExecuteOperationAsync(
        TurnOperationState state,
        TurnOperationKind operation,
        TurnRequestContext turnContext,
        CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case TurnOperationKind.ResolveInput:
                {
                    await inputResolutionRuntime.ResolveAsync(state, cancellationToken).ConfigureAwait(false);
                    return turnContext;
                }

            case TurnOperationKind.ResolveDependencies:
                return await dependencyResolutionRuntime.ResolveAsync(state, turnContext, cancellationToken).ConfigureAwait(false);

            case TurnOperationKind.ExecuteAssistant:
                {
                    await assistantExecutionRuntime.ExecuteAsync(state, turnContext, cancellationToken).ConfigureAwait(false);
                    return turnContext;
                }

            case TurnOperationKind.StreamAssistantOutput:
                {
                    await assistantOutputStreamingRuntime.StreamAsync(state, cancellationToken).ConfigureAwait(false);
                    return turnContext;
                }

            default:
                throw new NotSupportedException($"unsupported turn operation: {operation}");
        }
    }
}
