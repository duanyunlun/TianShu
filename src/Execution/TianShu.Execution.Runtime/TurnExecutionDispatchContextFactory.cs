using TianShu.Contracts.Orchestration;

namespace TianShu.Execution.Runtime;

/// <summary>
/// Turn execution dispatch context 工厂，统一把已批准的执行入口物化为 turn 分派上下文。
/// Turn execution dispatch-context factory that materializes approved execution entries into turn dispatch context.
/// </summary>
public static class TurnExecutionDispatchContextFactory
{
    /// <summary>
    /// 从已批准的执行入口创建 turn 分派上下文。
    /// Creates turn dispatch context from an approved execution entry.
    /// </summary>
    public static TurnExecutionDispatchContext FromExecutionEntry(
        IReadOnlyList<StageDefinition> stages,
        StageExecutionRequest executionRequest,
        StageExecutorRuntimeContext runtimeContext)
    {
        var dispatchPlan = StageExecutorDispatchPlanFactory.Bind(
            stages,
            executionRequest,
            runtimeContext);
        return TurnExecutionDispatchContext.FromDispatchPlan(dispatchPlan);
    }
}
