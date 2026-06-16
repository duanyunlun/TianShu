using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// StageGraph 解释器抽象，将已验证 StageGraph 物化为 ExecutionPlan。
/// StageGraph interpreter abstraction that materializes a validated StageGraph into an ExecutionPlan.
/// </summary>
public interface IStageGraphInterpreter
{
    /// <summary>
    /// 将已验证 StageGraph 解释为 Execution Runtime 可消费的执行计划。
    /// Interprets a validated StageGraph into an execution plan consumable by Execution Runtime.
    /// </summary>
    Task<ExecutionPlan> InterpretAsync(StageGraph graph, KernelInterpreterContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据当前 Stage 结果决定下一跳，不直接执行任何外部能力。
    /// Decides the next transition from the current stage result without executing external capabilities.
    /// </summary>
    Task<StageTransitionDecision> DecideNextStageAsync(StageGraph graph, StageResult currentStageResult, KernelInterpreterContext context, CancellationToken cancellationToken = default);
}
