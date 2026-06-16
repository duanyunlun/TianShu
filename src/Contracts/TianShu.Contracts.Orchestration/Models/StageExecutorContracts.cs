using TianShu.Contracts.Catalog;

namespace TianShu.Contracts.Orchestration;

/// <summary>
/// Stage Executor 分派类型，描述 executor binding 最终指向的执行形态。
/// Stage executor dispatch kind that describes the execution shape selected by an executor binding.
/// </summary>
public enum StageExecutorDispatchKind
{
    ModelTurn = 0,
    LocalCapability = 1,
    ToolRuntime = 2,
    ExternalExecutor = 3,
}

/// <summary>
/// Stage Executor 运行上下文，承载执行请求进入执行层后需要回写 checkpoint 的稳定标识。
/// Stage executor runtime context carrying the stable identifiers needed to write a checkpoint after execution.
/// </summary>
public sealed record StageExecutorRuntimeContext
{
    /// <summary>
    /// 初始化 Stage Executor 运行上下文。
    /// Initializes the stage executor runtime context.
    /// </summary>
    public StageExecutorRuntimeContext(
        string executionId,
        string stageId,
        string executorBinding,
        DateTimeOffset startedAt,
        string? decisionId = null,
        string? contextPackageId = null,
        string? modelRouteSetId = null,
        ModelRouteKind? modelRouteKind = null,
        string? modelRouteDiagnosticsCorrelationId = null,
        string? executorImplementationId = null,
        StageExecutorDispatchKind? executorDispatchKind = null)
    {
        ExecutionId = NormalizeRequired(executionId, nameof(executionId));
        StageId = NormalizeRequired(stageId, nameof(stageId));
        ExecutorBinding = NormalizeRequired(executorBinding, nameof(executorBinding));
        StartedAt = startedAt;
        DecisionId = Normalize(decisionId);
        ContextPackageId = Normalize(contextPackageId);
        ModelRouteSetId = Normalize(modelRouteSetId);
        ModelRouteKind = modelRouteKind;
        ModelRouteDiagnosticsCorrelationId = Normalize(modelRouteDiagnosticsCorrelationId);
        ExecutorImplementationId = Normalize(executorImplementationId);
        ExecutorDispatchKind = executorDispatchKind;
    }

    public string ExecutionId { get; }

    public string StageId { get; }

    public string ExecutorBinding { get; }

    public DateTimeOffset StartedAt { get; }

    public string? DecisionId { get; }

    public string? ContextPackageId { get; }

    public string? ModelRouteSetId { get; }

    public ModelRouteKind? ModelRouteKind { get; }

    public string? ModelRouteDiagnosticsCorrelationId { get; }

    public string? ExecutorImplementationId { get; }

    public StageExecutorDispatchKind? ExecutorDispatchKind { get; }

    /// <summary>
    /// 从正式 Stage 执行请求创建运行上下文。
    /// Creates a runtime context from the formal stage execution request.
    /// </summary>
    public static StageExecutorRuntimeContext FromExecutionRequest(
        StageExecutionRequest request,
        string? modelRouteDiagnosticsCorrelationId = null,
        DateTimeOffset? startedAt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StageExecutorRuntimeContext(
            request.ExecutionId,
            request.StageId,
            request.ExecutorBinding,
            startedAt ?? DateTimeOffset.UtcNow,
            decisionId: request.Decision.DecisionId,
            contextPackageId: request.ContextPackage.PackageId,
            modelRouteSetId: request.ModelRoute.RouteSetId,
            modelRouteKind: request.ModelRoute.RouteKind,
            modelRouteDiagnosticsCorrelationId: modelRouteDiagnosticsCorrelationId);
    }

    /// <summary>
    /// 返回带有 executor dispatch 解析结果的新运行上下文。
    /// Returns a new runtime context with executor dispatch resolution attached.
    /// </summary>
    public StageExecutorRuntimeContext WithExecutorDispatch(
        string executorImplementationId,
        StageExecutorDispatchKind executorDispatchKind)
        => new(
            ExecutionId,
            StageId,
            ExecutorBinding,
            StartedAt,
            DecisionId,
            ContextPackageId,
            ModelRouteSetId,
            ModelRouteKind,
            ModelRouteDiagnosticsCorrelationId,
            executorImplementationId,
            executorDispatchKind);

    private static string NormalizeRequired(string value, string paramName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Stage Executor 运行上下文标识不能为空。", paramName)
            : value.Trim();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
