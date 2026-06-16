using TianShu.Contracts.Orchestration;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Stage Executor dispatch plan 工厂，负责把 Stage 执行请求绑定到具体 executor 实现。
/// Stage executor dispatch-plan factory that binds stage execution requests to executor implementations.
/// </summary>
public static class StageExecutorDispatchPlanFactory
{
    /// <summary>
    /// 基于当前可见 Stage 定义创建 dispatch plan。
    /// Creates a dispatch plan from the currently visible stage definitions.
    /// </summary>
    public static StageExecutorDispatchPlan Bind(
        IReadOnlyList<StageDefinition> stages,
        StageExecutionRequest executionRequest,
        StageExecutorRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(executionRequest);
        ArgumentNullException.ThrowIfNull(runtimeContext);

        return StageExecutorDispatcher
            .FromStageDefinitions(stages)
            .Dispatch(executionRequest, runtimeContext);
    }
}
