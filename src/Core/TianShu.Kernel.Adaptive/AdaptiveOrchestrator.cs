using TianShu.Contracts.Kernel;
using TianShu.Kernel.Abstractions;
using TianShu.Kernel.Adaptive.Tools;

namespace TianShu.Kernel.Adaptive;

/// <summary>
/// Adaptive Orchestration Layer 默认实现，只收集 KernelTool 产出的 proposal。
/// Default Adaptive Orchestration Layer implementation that only collects proposals produced by KernelTool.
/// </summary>
public sealed class AdaptiveOrchestrator : IAdaptiveOrchestrator
{
    private readonly IKernelTool composeStageGraphTool;
    private readonly IKernelTool reviseStageGraphTool;

    public AdaptiveOrchestrator(IKernelTool? composeStageGraphTool = null, IKernelTool? reviseStageGraphTool = null)
    {
        this.composeStageGraphTool = composeStageGraphTool ?? new ComposeStageGraphKernelTool();
        this.reviseStageGraphTool = reviseStageGraphTool ?? new ReviseStageGraphKernelTool();
    }

    public async Task<KernelProposalSet> ProposeAsync(CoreIntent intent, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default)
    {
        var result = await composeStageGraphTool.InvokeKernelAsync(
            new KernelToolInvocation(intent, state, options),
            cancellationToken).ConfigureAwait(false);

        return new KernelProposalSet(result.Proposal is null ? Array.Empty<KernelProposal>() : new[] { result.Proposal }, "adaptive.default.compose_stage_graph");
    }

    public async Task<KernelProposalSet> ReviseAsync(KernelValidationResult validationResult, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default)
    {
        if (state.PendingProposals.Count == 0)
        {
            return new KernelProposalSet(rationaleRef: "adaptive.default.no_pending_proposal");
        }

        var sourceIntentId = state.SourceIntentId;
        var result = await reviseStageGraphTool.InvokeKernelAsync(
            new KernelToolInvocation(sourceIntentId, state, options),
            cancellationToken).ConfigureAwait(false);

        return new KernelProposalSet(result.Proposal is null ? Array.Empty<KernelProposal>() : new[] { result.Proposal }, validationResult.RationaleRef);
    }
}
