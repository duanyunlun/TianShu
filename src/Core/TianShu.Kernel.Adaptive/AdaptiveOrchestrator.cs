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
    private readonly IAdaptiveStageGraphCandidateGenerator candidateGenerator;
    private readonly IKernelTool reviseStageGraphTool;

    public AdaptiveOrchestrator(IKernelTool? composeStageGraphTool = null, IKernelTool? reviseStageGraphTool = null)
        : this(candidateGenerator: null, composeStageGraphTool, reviseStageGraphTool)
    {
    }

    public AdaptiveOrchestrator(IAdaptiveStageGraphCandidateGenerator candidateGenerator, IKernelTool? reviseStageGraphTool = null)
        : this(candidateGenerator, composeStageGraphTool: null, reviseStageGraphTool)
    {
    }

    private AdaptiveOrchestrator(
        IAdaptiveStageGraphCandidateGenerator? candidateGenerator = null,
        IKernelTool? composeStageGraphTool = null,
        IKernelTool? reviseStageGraphTool = null)
    {
        this.candidateGenerator = candidateGenerator ?? new DefaultAdaptiveStageGraphCandidateGenerator(composeStageGraphTool);
        this.reviseStageGraphTool = reviseStageGraphTool ?? new ReviseStageGraphKernelTool();
    }

    public async Task<KernelProposalSet> ProposeAsync(CoreIntent intent, KernelRunState state, KernelRunOptions options, CancellationToken cancellationToken = default)
    {
        var proposals = await candidateGenerator.GenerateCandidatesAsync(intent, state, options, cancellationToken).ConfigureAwait(false);

        return new KernelProposalSet(proposals, "adaptive.default.compose_stage_graph.candidates");
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
