using TianShu.Contracts.Memory;

namespace TianShu.IdentityMemory;

/// <summary>
/// 候选提升策略，决定候选是否可自动成为 Active fact 或必须进入待审/取代提案。
/// Candidate promotion policy that decides whether a candidate can become an Active fact automatically.
/// </summary>
public sealed class MemoryPromotionPolicy
{
    private const decimal MinimumAutoPromoteConfidence = 0.8m;

    public MemoryPromotionDecision EvaluateCandidate(
        MemoryCandidate candidate,
        MemoryPromotionPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        var reasonCodes = new List<MemoryRiskReasonCode>();
        var targetScope = context.TargetScopeKind
            ?? candidate.ContextSignature?.ScopeKinds.FirstOrDefault()
            ?? MemoryPolicySafety.ResolveScopeKind(candidate.MemorySpaceId)
            ?? MemoryScopeKind.Workspace;
        var sourceStrength = MemoryPolicySafety.DeriveSourceStrength(candidate.Source, context.SourceStrength);

        if (context.TargetSpaceIsReadOnly)
        {
            reasonCodes.Add(MemoryRiskReasonCode.ReadOnlySpace);
        }

        if (!context.ProviderDescriptor.Capabilities.HasFlag(MemoryProviderCapability.Add)
            || !context.ProviderDescriptor.Features.HasFlag(MemoryProviderFeature.SourceTracking)
            || context.ProviderDescriptor.SupportedLifecycleStatuses.Count > 0
               && !context.ProviderDescriptor.SupportedLifecycleStatuses.Contains(MemoryLifecycleStatus.Active))
        {
            reasonCodes.Add(MemoryRiskReasonCode.ProviderCapabilityMissing);
        }

        if (candidate.Source is null)
        {
            reasonCodes.Add(MemoryRiskReasonCode.MissingSource);
        }
        else if (candidate.Source.SourceKind == MemorySourceKind.Unknown)
        {
            reasonCodes.Add(MemoryRiskReasonCode.WeakSource);
        }

        if (sourceStrength <= MemorySourceStrength.Weak)
        {
            reasonCodes.Add(MemoryRiskReasonCode.WeakSource);
        }

        if (candidate.Confidence < MinimumAutoPromoteConfidence)
        {
            reasonCodes.Add(MemoryRiskReasonCode.LowConfidence);
        }

        if (MemoryPolicySafety.IsScopeEscalation(context.SourceScopeKind, targetScope))
        {
            reasonCodes.Add(MemoryRiskReasonCode.ScopeEscalation);
        }

        if (MemoryPolicySafety.IsLongTermBehaviorChange(candidate.Key, targetScope))
        {
            reasonCodes.Add(MemoryRiskReasonCode.LongTermBehaviorChange);
        }

        if (MemoryPolicySafety.LooksSensitive(candidate.Key)
            || MemoryPolicySafety.LooksSensitive(candidate.Value)
            || candidate.Source is not null && MemoryPolicySafety.LooksSensitive(candidate.Source)
            || MemoryPolicySafety.LooksSensitive(candidate.ExtractionReason))
        {
            reasonCodes.Add(MemoryRiskReasonCode.SecretLikeContent);
            reasonCodes.Add(MemoryRiskReasonCode.SensitiveContent);
        }

        var activeConflict = context.ExistingFacts.FirstOrDefault(existing =>
            existing.LifecycleStatus == MemoryLifecycleStatus.Active
            && string.Equals(existing.MemorySpaceId.Value, candidate.MemorySpaceId.Value, StringComparison.Ordinal)
            && string.Equals(existing.Key, candidate.Key, StringComparison.Ordinal)
            && !string.Equals(existing.Value.GetString(), candidate.Value.GetString(), StringComparison.Ordinal));
        if (activeConflict is not null)
        {
            reasonCodes.Add(MemoryRiskReasonCode.ConflictsWithActiveFact);
        }

        var distinctReasons = reasonCodes.Distinct().ToArray();
        if (distinctReasons.Contains(MemoryRiskReasonCode.SecretLikeContent)
            || distinctReasons.Contains(MemoryRiskReasonCode.ReadOnlySpace)
            || distinctReasons.Contains(MemoryRiskReasonCode.ProviderCapabilityMissing))
        {
            return new MemoryPromotionDecision(
                MemoryPromotionDecisionKind.Reject,
                distinctReasons,
                MemoryLifecycleStatus.PendingReview,
                "Candidate is not safe to promote.");
        }

        if (activeConflict is not null)
        {
            return new MemoryPromotionDecision(
                MemoryPromotionDecisionKind.SupersedeProposal,
                distinctReasons,
                MemoryLifecycleStatus.PendingReview,
                "Candidate conflicts with an active fact and must be reviewed as a supersede proposal.");
        }

        var effectiveReasons = IsTrustedUserMemoryCandidate(candidate, sourceStrength)
            ? distinctReasons
                .Where(static reason => reason is not MemoryRiskReasonCode.ScopeEscalation
                    and not MemoryRiskReasonCode.LongTermBehaviorChange)
                .ToArray()
            : distinctReasons;

        if (effectiveReasons.Length == 0)
        {
            return new MemoryPromotionDecision(
                MemoryPromotionDecisionKind.AutoPromote,
                targetLifecycleStatus: MemoryLifecycleStatus.Active,
                explanation: "Candidate meets the default auto-promotion policy.");
        }

        return new MemoryPromotionDecision(
            MemoryPromotionDecisionKind.NeedsReview,
            effectiveReasons,
            MemoryLifecycleStatus.PendingReview,
            "Candidate requires review before promotion.");
    }

    private static bool IsTrustedUserMemoryCandidate(
        MemoryCandidate candidate,
        MemorySourceStrength sourceStrength)
        => (candidate.RuleId?.StartsWith("rule.explicit-", StringComparison.Ordinal) == true
            || candidate.RuleId?.StartsWith("rule.semantic-", StringComparison.Ordinal) == true)
           && candidate.Source?.SourceKind == MemorySourceKind.Conversation
           && string.Equals(candidate.Source.Role, "user", StringComparison.OrdinalIgnoreCase)
           && sourceStrength >= MemorySourceStrength.Normal
           && candidate.Confidence >= MinimumAutoPromoteConfidence;
}
