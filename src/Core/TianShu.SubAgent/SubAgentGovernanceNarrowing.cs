using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.SubAgent;

/// <summary>
/// Sub-agent 子治理收窄工具，保证子 envelope 不超过父 envelope。
/// Sub-agent governance narrowing helper that keeps the child envelope within the parent envelope.
/// </summary>
public static class SubAgentGovernanceNarrowing
{
    public static GovernanceEnvelope Narrow(
        GovernanceEnvelope parent,
        GovernanceEnvelope requested,
        string childRunId,
        bool requiresHumanGate)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentException.ThrowIfNullOrWhiteSpace(childRunId);

        var requestedToolIds = requested.AllowedToolIds.Count == 0 ? parent.AllowedToolIds : requested.AllowedToolIds;
        var requestedModuleIds = requested.AllowedModuleIds.Count == 0 ? parent.AllowedModuleIds : requested.AllowedModuleIds;
        var policyIds = Intersect(parent.PolicyIds, requested.PolicyIds);
        var toolIds = Intersect(parent.AllowedToolIds, requestedToolIds)
            .Where(static toolId => !string.Equals(toolId, "spawn_agent", StringComparison.Ordinal))
            .ToArray();
        var moduleIds = Intersect(parent.AllowedModuleIds, requestedModuleIds);
        var approvalIds = requested.ApprovalIds.Count == 0
            ? parent.ApprovalIds
            : parent.ApprovalIds
                .Where(parentApproval => requested.ApprovalIds.Any(requestedApproval => string.Equals(requestedApproval.Value, parentApproval.Value, StringComparison.Ordinal)))
                .ToArray();
        var auditRecordIds = requested.AuditRecordIds.Count == 0
            ? parent.AuditRecordIds
            : parent.AuditRecordIds
                .Where(parentAudit => requested.AuditRecordIds.Any(requestedAudit => string.Equals(requestedAudit.Value, parentAudit.Value, StringComparison.Ordinal)))
                .ToArray();

        return new GovernanceEnvelope(
            $"governance-subagent-{childRunId}",
            policyIds,
            toolIds,
            moduleIds,
            Min(parent.MaxSideEffectLevel, requested.MaxSideEffectLevel),
            parent.RequiresHumanGate || requested.RequiresHumanGate || requiresHumanGate,
            approvalIds,
            auditRecordIds);
    }

    private static IReadOnlyList<string> Intersect(IReadOnlyList<string> parent, IReadOnlyList<string> requested)
        => parent
            .Where(parentValue => requested.Contains(parentValue, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static SideEffectLevel Min(SideEffectLevel left, SideEffectLevel right)
        => left <= right ? left : right;
}
