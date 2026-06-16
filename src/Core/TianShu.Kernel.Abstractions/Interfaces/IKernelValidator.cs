using TianShu.Contracts.Execution;
using TianShu.Contracts.Kernel;

namespace TianShu.Kernel.Abstractions;

/// <summary>
/// Kernel 验证器抽象，覆盖 intent、proposal、graph、operation、runtime step 和 strategy transition。
/// Kernel validator abstraction for intents, proposals, graphs, operations, runtime steps, and strategy transitions.
/// </summary>
public interface IKernelValidator
{
    Task<KernelValidationResult> ValidateIntentAsync(CoreIntent intent, KernelValidationContext context, CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateProposalAsync(KernelProposal proposal, KernelValidationContext context, CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateGraphAsync(StageGraph graph, KernelValidationContext context, CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateOperationAsync(KernelOperation operation, KernelValidationContext context, CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateRuntimeStepAsync(RuntimeStep step, KernelValidationContext context, CancellationToken cancellationToken = default);

    Task<KernelValidationResult> ValidateStrategyTransitionAsync(
        StrategyRecord strategy,
        StrategyLifecycleState targetState,
        IReadOnlyList<StrategyTransitionEvidence> evidence,
        KernelValidationContext context,
        CancellationToken cancellationToken = default);
}
