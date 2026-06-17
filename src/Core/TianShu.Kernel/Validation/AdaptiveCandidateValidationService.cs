using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Kernel.Abstractions;

namespace TianShu.Kernel.Validation;

/// <summary>
/// 默认 Adaptive 候选验证服务，复用 Stable Kernel Core 的验证器并产出结构化报告。
/// Default adaptive candidate validation service that reuses the Stable Kernel Core validator and emits structured reports.
/// </summary>
public sealed class AdaptiveCandidateValidationService : IAdaptiveCandidateValidationService
{
    private static readonly AdaptiveCandidateValidationCheckKind[] StageGraphCheckKinds =
    [
        AdaptiveCandidateValidationCheckKind.DeterministicKernel,
        AdaptiveCandidateValidationCheckKind.Governance,
        AdaptiveCandidateValidationCheckKind.Budget,
        AdaptiveCandidateValidationCheckKind.Capability,
    ];

    private readonly IKernelValidator validator;

    public AdaptiveCandidateValidationService(IKernelValidator? validator = null)
    {
        this.validator = validator ?? new KernelValidator();
    }

    public async Task<AdaptiveCandidateValidationReport> ValidateCandidatesAsync(
        AdaptiveCandidateValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var records = new List<AdaptiveCandidateValidationRecord>(request.ProposalSet.Proposals.Count);
        foreach (var proposal in request.ProposalSet.Proposals)
        {
            records.Add(await ValidateCandidateAsync(proposal, request.Context, cancellationToken).ConfigureAwait(false));
        }

        return new AdaptiveCandidateValidationReport(records, request.ProposalSet.RationaleRef);
    }

    private async Task<AdaptiveCandidateValidationRecord> ValidateCandidateAsync(
        KernelProposal proposal,
        KernelValidationContext context,
        CancellationToken cancellationToken)
    {
        if (proposal is null)
        {
            var missingProposal = Rejected(
                "kernel.candidate.missing_proposal",
                "Adaptive 候选不能为空。",
                "proposal");
            return new AdaptiveCandidateValidationRecord(
                new KernelProposalId("proposal.missing"),
                KernelProposalKind.Unspecified,
                AdaptiveCandidateValidationStatus.Rejected,
                new[]
                {
                    Check(
                        AdaptiveCandidateValidationCheckKind.Schema,
                        AdaptiveCandidateValidationStatus.Rejected,
                        missingProposal,
                        "proposal"),
                });
        }

        var checks = new List<AdaptiveCandidateValidationCheckRecord>();
        var proposalValidation = await validator.ValidateProposalAsync(proposal, context, cancellationToken).ConfigureAwait(false);
        checks.Add(Check(
            AdaptiveCandidateValidationCheckKind.Schema,
            StatusFor(proposalValidation),
            proposalValidation,
            "proposal"));

        if (!proposalValidation.IsApproved)
        {
            return Record(proposal, AdaptiveCandidateValidationStatus.Rejected, checks);
        }

        if (proposal is not StageGraphProposal stageGraphProposal)
        {
            var unsupported = Rejected(
                "kernel.candidate.unsupported_proposal_kind",
                "P30.3 候选验证闭环只接受 StageGraph proposal。",
                proposal.ProposalKind.ToString());
            checks.Add(Check(
                AdaptiveCandidateValidationCheckKind.DeterministicKernel,
                AdaptiveCandidateValidationStatus.Rejected,
                unsupported,
                "proposal.proposalKind"));

            return Record(proposal, AdaptiveCandidateValidationStatus.Rejected, checks);
        }

        var graphContext = new KernelValidationContext(
            context.Intent,
            context.State,
            stageGraphProposal.Graph,
            policySet: stageGraphProposal.Graph.Policies,
            metadata: context.Metadata);
        var graphValidation = await validator.ValidateGraphAsync(stageGraphProposal.Graph, graphContext, cancellationToken).ConfigureAwait(false);

        if (graphValidation.IsApproved)
        {
            foreach (var checkKind in StageGraphCheckKinds)
            {
                checks.Add(Check(checkKind, AdaptiveCandidateValidationStatus.Accepted, graphValidation, stageGraphProposal.Graph.GraphId.Value));
            }

            return Record(stageGraphProposal, AdaptiveCandidateValidationStatus.Accepted, checks, stageGraphProposal.Graph.GraphId);
        }

        var rejectedKind = ClassifyRejectedCheck(graphValidation);
        checks.Add(Check(rejectedKind, AdaptiveCandidateValidationStatus.Rejected, graphValidation, stageGraphProposal.Graph.GraphId.Value));
        foreach (var skippedKind in StageGraphCheckKinds.Where(kind => kind != rejectedKind))
        {
            checks.Add(Check(
                skippedKind,
                AdaptiveCandidateValidationStatus.Skipped,
                NeedsRevision(
                    "kernel.candidate.check_skipped_after_rejection",
                    "前置候选检查失败，后续检查未作为批准证据。",
                    stageGraphProposal.Graph.GraphId.Value),
                stageGraphProposal.Graph.GraphId.Value));
        }

        return Record(stageGraphProposal, AdaptiveCandidateValidationStatus.Rejected, checks, stageGraphProposal.Graph.GraphId);
    }

    private static AdaptiveCandidateValidationRecord Record(
        KernelProposal proposal,
        AdaptiveCandidateValidationStatus status,
        IReadOnlyList<AdaptiveCandidateValidationCheckRecord> checks,
        StageGraphId? graphId = null)
        => new(proposal.ProposalId, proposal.ProposalKind, status, checks, graphId);

    private static AdaptiveCandidateValidationCheckRecord Check(
        AdaptiveCandidateValidationCheckKind checkKind,
        AdaptiveCandidateValidationStatus status,
        KernelValidationResult result,
        string? sourceRef)
        => new(checkKind, status, result, sourceRef);

    private static AdaptiveCandidateValidationStatus StatusFor(KernelValidationResult result)
        => result.IsApproved ? AdaptiveCandidateValidationStatus.Accepted : AdaptiveCandidateValidationStatus.Rejected;

    private static KernelValidationResult Rejected(string code, string message, string? sourceRef)
        => new(
            KernelValidationDecision.Rejected,
            new[] { new KernelValidationIssue(code, message, KernelValidationIssueSeverity.Error, sourceRef) });

    private static KernelValidationResult NeedsRevision(string code, string message, string? sourceRef)
        => new(
            KernelValidationDecision.NeedsRevision,
            new[] { new KernelValidationIssue(code, message, KernelValidationIssueSeverity.Info, sourceRef) });

    private static AdaptiveCandidateValidationCheckKind ClassifyRejectedCheck(KernelValidationResult result)
    {
        var code = result.Issues.FirstOrDefault()?.Code ?? string.Empty;
        if (code.StartsWith("kernel.proposal.", StringComparison.Ordinal))
        {
            return AdaptiveCandidateValidationCheckKind.Schema;
        }

        if (code.Contains("budget", StringComparison.Ordinal)
            || code.Contains("unbounded", StringComparison.Ordinal))
        {
            return AdaptiveCandidateValidationCheckKind.Budget;
        }

        if (code.Contains("tool", StringComparison.Ordinal)
            || code.Contains("module", StringComparison.Ordinal)
            || code.Contains("capability", StringComparison.Ordinal)
            || code.Contains("model_route", StringComparison.Ordinal))
        {
            return AdaptiveCandidateValidationCheckKind.Capability;
        }

        if (code.Contains("governance", StringComparison.Ordinal)
            || code.Contains("human_gate", StringComparison.Ordinal)
            || code.Contains("side_effect", StringComparison.Ordinal)
            || code.Contains("policy_not_in", StringComparison.Ordinal))
        {
            return AdaptiveCandidateValidationCheckKind.Governance;
        }

        return AdaptiveCandidateValidationCheckKind.DeterministicKernel;
    }
}
