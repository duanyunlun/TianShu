using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Execution.Runtime;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop routing session runtime，负责串联会话级 routing 规划、状态提交与 turn context 投影。
/// AppHost core-loop routing session runtime that sequences session routing planning, state commit, and turn context projection.
/// </summary>
internal sealed class AppHostCoreLoopRoutingSessionRuntime
{
    private readonly AppHostCoreLoopOrchestrationStateStore orchestrationStateStore;
    private readonly AppHostCoreLoopRoutingEntryPlanner routingEntryPlanner;

    public AppHostCoreLoopRoutingSessionRuntime(
        AppHostCoreLoopOrchestrationStateStore orchestrationStateStore,
        AppHostCoreLoopRoutingEntryPlanner routingEntryPlanner)
    {
        this.orchestrationStateStore = orchestrationStateStore ?? throw new ArgumentNullException(nameof(orchestrationStateStore));
        this.routingEntryPlanner = routingEntryPlanner ?? throw new ArgumentNullException(nameof(routingEntryPlanner));
    }

    public async Task<TurnRequestContext> ApplyAsync(
        string threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        string? requestedStageId,
        CancellationToken cancellationToken)
    {
        var storedOrchestrationState = await orchestrationStateStore
            .ReadAsync(threadId, cancellationToken)
            .ConfigureAwait(false);
        var entry = routingEntryPlanner.Plan(
            threadId,
            session,
            context,
            requestedStageId,
            storedOrchestrationState);
        await orchestrationStateStore
            .CommitStepAsync(
                threadId,
                storedOrchestrationState,
                entry.Plan.OrchestrationStep.Decision,
                entry.Plan.OrchestrationStep.ContextPackage,
                cancellationToken)
            .ConfigureAwait(false);

        return TurnRequestContextExecutionDispatchProjection.Project(
            entry.RoutedContext,
            entry.DispatchContext);
    }

    public TurnRequestContext ApplyTransient(
        string? threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        string? requestedStageId)
    {
        var entry = routingEntryPlanner.Plan(
            threadId,
            session,
            context,
            requestedStageId,
            new AppHostCoreLoopStoredOrchestrationState(false, null, null, null));
        return TurnRequestContextExecutionDispatchProjection.Project(
            entry.RoutedContext,
            entry.DispatchContext);
    }
}
