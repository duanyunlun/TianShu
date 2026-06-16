namespace TianShu.Provider.Abstractions;

/// <summary>
/// 审批中的执行策略修订载体。
/// Execution-policy amendment payload used by approval flows.
/// </summary>
public sealed record ExecPolicyAmendmentPayload(
    IReadOnlyList<string> CommandPrefix);

/// <summary>
/// 审批中的网络策略修订载体。
/// Network-policy amendment payload used by approval flows.
/// </summary>
public sealed record NetworkPolicyAmendmentPayload(
    string Host,
    string Action);

/// <summary>
/// 审批决策选项载体。
/// Approval decision option payload.
/// </summary>
public sealed record ApprovalDecisionOptionPayload(
    string Type,
    ExecPolicyAmendmentPayload? ExecPolicyAmendment = null,
    NetworkPolicyAmendmentPayload? NetworkPolicyAmendment = null);
