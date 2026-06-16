using TianShu.AppHost.State;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop routing runtime composition，负责组装 routing facade 所需的内部对象图。
/// AppHost core-loop routing runtime composition that builds the internal object graph used by the routing facade.
/// </summary>
internal sealed class AppHostCoreLoopRoutingRuntimeComposition
{
    public AppHostCoreLoopRoutingRuntimeComposition(
        KernelThreadStore threadStore,
        Func<string?, Dictionary<string, object?>> readRuntimeConfig,
        Func<string?, string?> normalize,
        Func<string, string> buildStageCorrelationId)
    {
        ArgumentNullException.ThrowIfNull(readRuntimeConfig);
        ArgumentNullException.ThrowIfNull(normalize);
        ArgumentNullException.ThrowIfNull(buildStageCorrelationId);

        var orchestrationStateStore = new AppHostCoreLoopOrchestrationStateStore(threadStore);
        EntryIntentResolver = new AppHostCoreLoopEntryIntentResolver(normalize);
        var routingEntryPlanner = new AppHostCoreLoopRoutingEntryPlanner(
            new AppHostCoreLoopRuntimeConfigReader(readRuntimeConfig),
            normalize,
            buildStageCorrelationId,
            new AppHostCoreLoopOrchestrationStateProjector(),
            new AppHostCoreLoopModelRouteResolver(normalize));
        RoutingSessionRuntime = new AppHostCoreLoopRoutingSessionRuntime(
            orchestrationStateStore,
            routingEntryPlanner);
    }

    public AppHostCoreLoopEntryIntentResolver EntryIntentResolver { get; }

    public AppHostCoreLoopRoutingSessionRuntime RoutingSessionRuntime { get; }
}
