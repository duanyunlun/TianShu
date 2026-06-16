using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Stable Kernel Core 的稳定入口，只批准已治理的 Kernel 输入与输出。
/// Stable entry point for approving governed Kernel inputs and outputs.
/// </summary>
public interface IStableKernelCore
{
    /// <summary>
    /// 运行一次已归一化核心意图，并返回 Kernel 可投影的运行结果。
    /// Runs one normalized core intent and returns the Kernel-projected result.
    /// </summary>
    Task<KernelRunResult> RunAsync(CoreIntent intent, KernelRunOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 审查 Adaptive Orchestration Layer 提出的 proposal。
    /// Reviews a proposal produced by the Adaptive Orchestration Layer.
    /// </summary>
    Task<KernelValidationResult> ReviewProposalAsync(KernelValidationContext context, KernelProposal proposal, CancellationToken cancellationToken = default);

    /// <summary>
    /// 审查运行中的 Kernel operation 是否可以物化为执行步骤。
    /// Reviews whether an in-run Kernel operation may materialize into execution steps.
    /// </summary>
    Task<KernelValidationResult> ReviewOperationAsync(KernelValidationContext context, KernelOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批准已解释出的 ExecutionPlan，作为交给 Execution Runtime 前的最后闸门。
    /// Approves an interpreted ExecutionPlan as the last gate before Execution Runtime.
    /// </summary>
    Task<KernelValidationResult> ReviewExecutionPlanAsync(KernelValidationContext context, ExecutionPlan executionPlan, CancellationToken cancellationToken = default);
}
