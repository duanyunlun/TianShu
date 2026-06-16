using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 录入风险策略，负责把证据分类为可保留、待审或拒绝。
/// Ingestion risk policy that classifies evidence as retainable, review-required, or rejected.
/// </summary>
public sealed class MemoryIngestionPolicy
{
    public MemoryIngestionDecision EvaluateEvidence(
        MemoryEvidenceRecord evidence,
        MemoryIngestionPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);

        var reasonCodes = new List<MemoryRiskReasonCode>();
        var targetScope = context.TargetScopeKind ?? evidence.ScopeKind ?? MemoryPolicySafety.ResolveScopeKind(evidence.MemorySpaceId);
        var sourceStrength = MemoryPolicySafety.DeriveSourceStrength(evidence.Source, context.SourceStrength);

        if (context.TargetSpaceIsReadOnly)
        {
            reasonCodes.Add(MemoryRiskReasonCode.ReadOnlySpace);
        }

        if (!context.ProviderDescriptor.Features.HasFlag(MemoryProviderFeature.SourceTracking))
        {
            reasonCodes.Add(MemoryRiskReasonCode.ProviderCapabilityMissing);
        }

        if (evidence.Source.SourceKind == MemorySourceKind.Unknown)
        {
            reasonCodes.Add(MemoryRiskReasonCode.MissingSource);
        }

        if (sourceStrength <= MemorySourceStrength.Weak)
        {
            reasonCodes.Add(MemoryRiskReasonCode.WeakSource);
        }

        if (MemoryPolicySafety.LooksSensitive(evidence.SafeSummary) || MemoryPolicySafety.LooksSensitive(evidence.Source))
        {
            reasonCodes.Add(MemoryRiskReasonCode.SecretLikeContent);
            reasonCodes.Add(MemoryRiskReasonCode.SensitiveContent);
        }

        if (targetScope is { } target && MemoryPolicySafety.IsScopeEscalation(context.SourceScopeKind, target))
        {
            reasonCodes.Add(MemoryRiskReasonCode.ScopeEscalation);
        }

        if (evidence.EvidenceKind == MemoryEvidenceKind.CommandFailure)
        {
            reasonCodes.Add(MemoryRiskReasonCode.SingleUnverifiedFailure);
        }

        var distinctReasons = reasonCodes.Distinct().ToArray();
        if (distinctReasons.Contains(MemoryRiskReasonCode.SecretLikeContent))
        {
            return new MemoryIngestionDecision(
                MemoryIngestionDecisionKind.Reject,
                distinctReasons,
                MemoryLifecycleStatus.PendingReview,
                "Secret-like evidence must not enter ordinary memory ingestion.");
        }

        if (distinctReasons.Contains(MemoryRiskReasonCode.ReadOnlySpace)
            || distinctReasons.Contains(MemoryRiskReasonCode.ProviderCapabilityMissing))
        {
            return new MemoryIngestionDecision(
                MemoryIngestionDecisionKind.Reject,
                distinctReasons,
                MemoryLifecycleStatus.PendingReview,
                "Provider or target space cannot safely ingest this evidence.");
        }

        return new MemoryIngestionDecision(
            MemoryIngestionDecisionKind.AcceptEvidence,
            distinctReasons,
            MemoryLifecycleStatus.PendingReview,
            distinctReasons.Length == 0
                ? "Evidence can be retained for later promotion."
                : "Evidence is retained but requires policy-aware promotion.");
    }
}
