using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Adaptive Orchestration Layer 抽象，只能产出 KernelProposalSet。
/// Adaptive Orchestration Layer abstraction that may only produce KernelProposalSet.
/// </summary>
public interface IAdaptiveOrchestrator
{
    /// <summary>
    /// 为当前核心意图提出候选编排方案。
    /// Proposes candidate orchestration plans for the current core intent.
    /// </summary>
    Task<KernelProposalSet> ProposeAsync(CoreIntent intent, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 基于验证拒绝或修正要求生成新的 proposal。
    /// Produces revised proposals from validation rejection or revision feedback.
    /// </summary>
    Task<KernelProposalSet> ReviseAsync(KernelValidationResult validationResult, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default);
}
