using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop routing runtime，负责把会话编排入口转换为可执行的 turn context。
/// AppHost core-loop routing runtime that converts a session orchestration entry into an executable turn context.
/// </summary>
internal sealed class AppHostCoreLoopRoutingRuntime
{
    private readonly AppHostCoreLoopRoutingRuntimeComposition composition;

    public AppHostCoreLoopRoutingRuntime(
        KernelThreadStore threadStore,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig,
        Func<string?, string?> normalize,
        Func<string, string> buildStageCorrelationId)
    {
        composition = new AppHostCoreLoopRoutingRuntimeComposition(
            threadStore,
            readRuntimeConfig,
            normalize,
            buildStageCorrelationId);
    }

    public async Task<TurnRequestContext> ApplyOrchestrationAndModelRouteAsync(
        string threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        string? requestedStageId,
        CancellationToken cancellationToken)
        => await composition.RoutingSessionRuntime
            .ApplyAsync(threadId, session, context, requestedStageId, cancellationToken)
            .ConfigureAwait(false);

    public Task<TurnRequestContext> ApplyDefaultOrchestrationAndModelRouteAsync(
        string threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        CancellationToken cancellationToken)
        => ApplyOrchestrationAndModelRouteAsync(
            threadId,
            session,
            context,
            composition.EntryIntentResolver.ResolveRequestedStageId(session, AppHostCoreLoopRoutingEntryIntent.DefaultTurn),
            cancellationToken);

    public Task<TurnRequestContext> ApplyReviewOrchestrationAndModelRouteAsync(
        string threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        CancellationToken cancellationToken)
        => ApplyOrchestrationAndModelRouteAsync(
            threadId,
            session,
            context,
            composition.EntryIntentResolver.ResolveRequestedStageId(session, AppHostCoreLoopRoutingEntryIntent.ReviewTurn),
            cancellationToken);

    public TurnRequestContext ApplyOrchestrationAndModelRoute(
        string? threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        string? requestedStageId)
        => composition.RoutingSessionRuntime.ApplyTransient(threadId, session, context, requestedStageId);

    public TurnRequestContext ApplyDefaultOrchestrationAndModelRoute(
        string? threadId,
        KernelThreadSessionState session,
        TurnRequestContext context)
        => ApplyOrchestrationAndModelRoute(
            threadId,
            session,
            context,
            composition.EntryIntentResolver.ResolveRequestedStageId(session, AppHostCoreLoopRoutingEntryIntent.DefaultTurn));
}
