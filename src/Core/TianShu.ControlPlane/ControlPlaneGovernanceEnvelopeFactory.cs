using TianShu.Contracts.Governance;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.ControlPlane;

/// <summary>
/// Control Plane 创建 GovernanceEnvelope 的正式请求模型。
/// Formal Control Plane request model for creating a GovernanceEnvelope.
/// </summary>
public sealed record ControlPlaneGovernanceEnvelopeRequest
{
    public ControlPlaneGovernanceEnvelopeRequest(
        string envelopeId,
        IReadOnlyList<string>? policyIds = null,
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.Unspecified,
        bool requiresHumanGate = true,
        IReadOnlyList<ApprovalId>? approvalIds = null,
        IReadOnlyList<AuditRecordId>? auditRecordIds = null,
        IReadOnlyList<PolicyDecision>? policyDecisions = null)
    {
        EnvelopeId = envelopeId;
        PolicyIds = policyIds ?? Array.Empty<string>();
        AllowedToolIds = allowedToolIds ?? Array.Empty<string>();
        AllowedModuleIds = allowedModuleIds ?? Array.Empty<string>();
        MaxSideEffectLevel = maxSideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
        ApprovalIds = approvalIds ?? Array.Empty<ApprovalId>();
        AuditRecordIds = auditRecordIds ?? Array.Empty<AuditRecordId>();
        PolicyDecisions = policyDecisions ?? Array.Empty<PolicyDecision>();
    }

    public string EnvelopeId { get; }

    public IReadOnlyList<string> PolicyIds { get; }

    public IReadOnlyList<string> AllowedToolIds { get; }

    public IReadOnlyList<string> AllowedModuleIds { get; }

    public SideEffectLevel MaxSideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public IReadOnlyList<ApprovalId> ApprovalIds { get; }

    public IReadOnlyList<AuditRecordId> AuditRecordIds { get; }

    public IReadOnlyList<PolicyDecision> PolicyDecisions { get; }
}

/// <summary>
/// Control Plane 治理信封工厂，只负责归一化 envelope，不执行 Kernel 编排。
/// Control Plane governance-envelope factory that only normalizes envelopes and never performs Kernel orchestration.
/// </summary>
public static class ControlPlaneGovernanceEnvelopeFactory
{
    public static GovernanceEnvelope Create(ControlPlaneGovernanceEnvelopeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new GovernanceEnvelope(
            request.EnvelopeId,
            policyIds: Normalize(request.PolicyIds),
            allowedToolIds: Normalize(request.AllowedToolIds),
            allowedModuleIds: Normalize(request.AllowedModuleIds),
            maxSideEffectLevel: request.MaxSideEffectLevel,
            requiresHumanGate: request.RequiresHumanGate,
            approvalIds: request.ApprovalIds,
            auditRecordIds: request.AuditRecordIds,
            policyDecisions: request.PolicyDecisions);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> values)
        => values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}
