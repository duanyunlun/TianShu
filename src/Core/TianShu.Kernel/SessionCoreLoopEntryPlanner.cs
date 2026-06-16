using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;

namespace TianShu.Kernel;

/// <summary>
/// Core Loop 入口规划器，统一完成 Observe / Decide / Project / Route / Create Execution Request 的前半循环。
/// Core-loop entry planner that unifies Observe / Decide / Project / Route / Create Execution Request.
/// </summary>
public sealed class SessionCoreLoopEntryPlanner
{
    private readonly IReadOnlyList<StageDefinition> stages;
    private readonly SessionOrchestrator orchestrator;
    private readonly SessionContextProjector contextProjector;
    private readonly SessionExecutionRequestFactory executionRequestFactory;
    private readonly IReadOnlyList<ModelRouteKind> registeredRouteKinds;

    /// <summary>
    /// 使用当前可见 Stage 定义初始化 Core Loop 入口规划器。
    /// Initializes the core-loop entry planner with the currently visible stage definitions.
    /// </summary>
    public SessionCoreLoopEntryPlanner(IEnumerable<StageDefinition> stageDefinitions)
    {
        ArgumentNullException.ThrowIfNull(stageDefinitions);

        stages = stageDefinitions.ToArray();
        orchestrator = new SessionOrchestrator(stages);
        contextProjector = new SessionContextProjector();
        executionRequestFactory = new SessionExecutionRequestFactory();
        registeredRouteKinds = stages
            .Select(static stage => stage.ModelRouteKind)
            .DistinctBy(static routeKind => routeKind.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// 规划一次 Stage 执行入口，并生成正式执行请求与 Stage Executor 运行上下文。
    /// Plans one stage execution entry and creates the formal execution request and stage executor runtime context.
    /// </summary>
    public SessionCoreLoopEntryPlan PlanEntry(
        SessionOrchestrationInput input,
        Func<SessionCoreLoopRouteRequest, SessionCoreLoopRouteResult> resolveModelRoute,
        DateTimeOffset? startedAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(resolveModelRoute);

        var orchestrationDecision = orchestrator.PlanNext(input);
        var contextPackage = contextProjector.Project(
            BuildStableId("ctx", input.CorrelationId, orchestrationDecision.Stage.Id),
            orchestrationDecision.Stage,
            input.SessionId,
            input.ThreadId,
            input.Checkpoints,
            input.ContextLedgerSegments,
            input.ContextBudgetTokens,
            input.ObservedState);
        var orchestrationStep = new SessionOrchestrationStep(
            orchestrationDecision.Decision,
            contextPackage,
            orchestrationDecision.Stage);
        var routeRequest = new SessionCoreLoopRouteRequest(
            orchestrationStep.Stage,
            orchestrationStep.Stage.ModelRouteKind,
            registeredRouteKinds);
        var routeResult = resolveModelRoute(routeRequest)
                          ?? throw new InvalidOperationException("模型路由解析不能返回空结果。");
        var executionRequest = executionRequestFactory.Create(
            orchestrationStep,
            routeResult.ModelRoute);
        var executorRuntimeContext = StageExecutorRuntimeContext.FromExecutionRequest(
            executionRequest,
            routeResult.DiagnosticsCorrelationId,
            startedAt);

        return new SessionCoreLoopEntryPlan(
            stages,
            orchestrationStep,
            registeredRouteKinds,
            routeResult.ModelRoute,
            executionRequest,
            executorRuntimeContext);
    }

    private static string BuildStableId(string prefix, string correlationId, string stageId)
        => $"{prefix}-{Normalize(correlationId) ?? "session"}-{stageId}";

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Core Loop 模型路由请求，表达当前 Stage 需要解析的模型绑定通道。
/// Core-loop model route request that describes the model binding lane required by the current stage.
/// </summary>
public sealed record SessionCoreLoopRouteRequest(
    StageDefinition Stage,
    ModelRouteKind RouteKind,
    IReadOnlyList<ModelRouteKind> RegisteredRouteKinds);

/// <summary>
/// Core Loop 模型路由结果，封装模型路由解析结果与诊断关联标识。
/// Core-loop model route result wrapping the model route resolution and diagnostics correlation id.
/// </summary>
public sealed record SessionCoreLoopRouteResult
{
    /// <summary>
    /// 初始化 Core Loop 模型路由结果。
    /// Initializes the core-loop model route result.
    /// </summary>
    public SessionCoreLoopRouteResult(
        ModelRouteResolutionResult modelRoute,
        string? diagnosticsCorrelationId = null)
    {
        ModelRoute = modelRoute ?? throw new ArgumentNullException(nameof(modelRoute));
        DiagnosticsCorrelationId = string.IsNullOrWhiteSpace(diagnosticsCorrelationId)
            ? null
            : diagnosticsCorrelationId.Trim();
    }

    public ModelRouteResolutionResult ModelRoute { get; }

    public string? DiagnosticsCorrelationId { get; }
}

/// <summary>
/// Core Loop 入口计划，固定进入 Stage Executor 前必须一致的全部中间结果。
/// Core-loop entry plan that pins all intermediate results before entering the stage executor.
/// </summary>
public sealed record SessionCoreLoopEntryPlan
{
    /// <summary>
    /// 初始化 Core Loop 入口计划。
    /// Initializes the core-loop entry plan.
    /// </summary>
    public SessionCoreLoopEntryPlan(
        IReadOnlyList<StageDefinition>? stages,
        SessionOrchestrationStep orchestrationStep,
        IReadOnlyList<ModelRouteKind>? registeredRouteKinds,
        ModelRouteResolutionResult modelRoute,
        StageExecutionRequest executionRequest,
        StageExecutorRuntimeContext executorRuntimeContext)
    {
        Stages = stages ?? Array.Empty<StageDefinition>();
        OrchestrationStep = orchestrationStep ?? throw new ArgumentNullException(nameof(orchestrationStep));
        RegisteredRouteKinds = registeredRouteKinds ?? Array.Empty<ModelRouteKind>();
        ModelRoute = modelRoute ?? throw new ArgumentNullException(nameof(modelRoute));
        ExecutionRequest = executionRequest ?? throw new ArgumentNullException(nameof(executionRequest));
        ExecutorRuntimeContext = executorRuntimeContext ?? throw new ArgumentNullException(nameof(executorRuntimeContext));
    }

    public IReadOnlyList<StageDefinition> Stages { get; }

    public SessionOrchestrationStep OrchestrationStep { get; }

    public IReadOnlyList<ModelRouteKind> RegisteredRouteKinds { get; }

    public ModelRouteResolutionResult ModelRoute { get; }

    public StageExecutionRequest ExecutionRequest { get; }

    public StageExecutorRuntimeContext ExecutorRuntimeContext { get; }
}
