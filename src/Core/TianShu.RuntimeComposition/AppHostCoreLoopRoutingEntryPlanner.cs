using TianShu.AppHost.State;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Execution.Runtime;
using TianShu.Kernel;

namespace TianShu.RuntimeComposition;

/// <summary>
/// AppHost Core Loop routing entry planner，负责把会话入口固化为可路由的执行入口。
/// AppHost core-loop routing entry planner that pins a session entry into a routable execution entry.
/// </summary>
internal sealed class AppHostCoreLoopRoutingEntryPlanner
{
    private readonly AppHostCoreLoopRuntimeConfigReader runtimeConfigReader;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, string> buildStageCorrelationId;
    private readonly AppHostCoreLoopOrchestrationStateProjector orchestrationStateProjector;
    private readonly AppHostCoreLoopModelRouteResolver modelRouteResolver;

    public AppHostCoreLoopRoutingEntryPlanner(
        AppHostCoreLoopRuntimeConfigReader runtimeConfigReader,
        Func<string?, string?> normalize,
        Func<string, string> buildStageCorrelationId,
        AppHostCoreLoopOrchestrationStateProjector orchestrationStateProjector,
        AppHostCoreLoopModelRouteResolver modelRouteResolver)
    {
        this.runtimeConfigReader = runtimeConfigReader ?? throw new ArgumentNullException(nameof(runtimeConfigReader));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.buildStageCorrelationId = buildStageCorrelationId ?? throw new ArgumentNullException(nameof(buildStageCorrelationId));
        this.orchestrationStateProjector = orchestrationStateProjector ?? throw new ArgumentNullException(nameof(orchestrationStateProjector));
        this.modelRouteResolver = modelRouteResolver ?? throw new ArgumentNullException(nameof(modelRouteResolver));
    }

    public AppHostCoreLoopRoutingEntry Plan(
        string? threadId,
        KernelThreadSessionState session,
        TurnRequestContext context,
        string? requestedStageId,
        AppHostCoreLoopStoredOrchestrationState storedState)
    {
        ArgumentNullException.ThrowIfNull(storedState);

        var rawConfig = runtimeConfigReader.Read(session.Cwd);
        var normalizedThreadId = normalize(threadId) ?? "transient-thread";
        var input = orchestrationStateProjector.ProjectInput(
            normalizedThreadId,
            buildStageCorrelationId(normalizedThreadId),
            requestedStageId,
            storedState.Orchestration);

        AppHostCoreLoopModelRouteResolution? routeResolution = null;
        var plan = SessionCoreLoopRoutingPlanFactory.Plan(
            new SessionCoreLoopRoutingPlanRequest(
                rawConfig,
                input,
                routeRequest =>
                {
                    routeResolution = modelRouteResolver.Resolve(
                        threadId,
                        session,
                        routeRequest.RouteKind,
                        context,
                        routeRequest.RegisteredRouteKinds,
                        rawConfig);
                    return new SessionCoreLoopRouteResult(
                        routeResolution.Result,
                        routeResolution.Context.ModelRouteDiagnosticsCorrelationId);
                },
                workspaceCwd: normalize(context.Cwd) ?? normalize(session.Cwd) ?? normalize(storedState.Cwd),
                workspaceSandboxMode: context.SandboxMode,
                workspaceWebSearchMode: context.WebSearchMode,
                workspaceWindowsSandboxLevel: context.WindowsSandboxLevel.ToString(),
                allowLoginShell: context.AllowLoginShell,
                artifactRefs: KernelThreadObservedStateProjectionFactory.ProjectArtifactRefs(storedState.Orchestration),
                memoryMode: storedState.MemoryMode,
                approvalPolicy: context.ApprovalPolicy?.ToString(),
                policySandboxMode: context.SandboxMode,
                policyWebSearchMode: context.WebSearchMode,
                defaultModeRequestUserInputEnabled: context.DefaultModeRequestUserInputEnabled,
                startedAt: DateTimeOffset.UtcNow));
        var dispatchContext = TurnExecutionDispatchContextFactory.FromExecutionEntry(
            plan.Stages,
            plan.ExecutionRequest,
            plan.ExecutorRuntimeContext);

        return new AppHostCoreLoopRoutingEntry(
            plan,
            dispatchContext,
            routeResolution?.Context ?? context);
    }

}

internal sealed record AppHostCoreLoopRoutingEntry(
    SessionCoreLoopEntryPlan Plan,
    TurnExecutionDispatchContext DispatchContext,
    TurnRequestContext RoutedContext);
