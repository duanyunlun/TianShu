using TianShu.Contracts.Interactions;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance;

/// <summary>
/// 控制平面审批决策。
/// Control-plane approval decision.
/// </summary>
public enum ControlPlaneApprovalDecision
{
    Approve = 0,
    ApproveForSession = 1,
    ApproveAndRemember = 2,
    ApproveWithExecutionPolicyAmendment = 3,
    ApplyNetworkPolicyAmendment = 4,
    Decline = 5,
    Cancel = 6,
}

/// <summary>
/// 控制平面权限授予范围。
/// Control-plane permission-grant scope.
/// </summary>
public enum ControlPlanePermissionScope
{
    Turn = 0,
    Session = 1,
}

/// <summary>
/// 控制平面审批解析命令。
/// Control-plane command that resolves an approval request.
/// </summary>
public sealed record ControlPlaneApprovalResolution
{
    /// <summary>
    /// 归一化后的交互包络。
    /// Normalized interaction envelope.
    /// </summary>
    public InteractionEnvelope? Envelope { get; init; }

    /// <summary>
    /// 调用标识。
    /// Call identifier.
    /// </summary>
    public CallId CallId { get; init; }

    /// <summary>
    /// 审批决策。
    /// Approval decision.
    /// </summary>
    public ControlPlaneApprovalDecision Decision { get; init; }

    /// <summary>
    /// 附加说明。
    /// Additional note.
    /// </summary>
    public string? Note { get; init; }

    /// <summary>
    /// 命令前缀修订。
    /// Command-prefix amendment.
    /// </summary>
    public IReadOnlyList<string> CommandPrefix { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 网络策略主机。
    /// Network-policy host.
    /// </summary>
    public string? NetworkHost { get; init; }

    /// <summary>
    /// 网络策略动作。
    /// Network-policy action.
    /// </summary>
    public string? NetworkAction { get; init; }
}

/// <summary>
/// 控制平面权限授予命令。
/// Control-plane command that resolves a permission request.
/// </summary>
public sealed record ControlPlanePermissionGrant
{
    /// <summary>
    /// 归一化后的交互包络。
    /// Normalized interaction envelope.
    /// </summary>
    public InteractionEnvelope? Envelope { get; init; }

    /// <summary>
    /// 调用标识。
    /// Call identifier.
    /// </summary>
    public CallId CallId { get; init; }

    /// <summary>
    /// 权限载荷。
    /// Permission payload.
    /// </summary>
    public IReadOnlyDictionary<string, StructuredValue> Permissions { get; init; } =
        new Dictionary<string, StructuredValue>(StringComparer.Ordinal);

    /// <summary>
    /// 生效范围。
    /// Effective scope.
    /// </summary>
    public ControlPlanePermissionScope Scope { get; init; } = ControlPlanePermissionScope.Turn;
}

/// <summary>
/// 控制平面用户补录提交命令。
/// Control-plane command that submits requested user input.
/// </summary>
public sealed record ControlPlaneUserInputSubmission
{
    /// <summary>
    /// 归一化后的交互包络。
    /// Normalized interaction envelope.
    /// </summary>
    public InteractionEnvelope? Envelope { get; init; }

    /// <summary>
    /// 调用标识。
    /// Call identifier.
    /// </summary>
    public CallId CallId { get; init; }

    /// <summary>
    /// 回答载荷。
    /// Answer payload.
    /// </summary>
    public IReadOnlyDictionary<string, StructuredValue> Answers { get; init; } =
        new Dictionary<string, StructuredValue>(StringComparer.Ordinal);
}
