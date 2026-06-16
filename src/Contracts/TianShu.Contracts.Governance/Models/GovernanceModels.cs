using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance;

/// <summary>
/// 审批状态。
/// Approval status.
/// </summary>
public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
}

/// <summary>
/// 用户补录请求状态。
/// User-input request status.
/// </summary>
public enum UserInputRequestStatus
{
    Pending = 0,
    Fulfilled = 1,
    Cancelled = 2,
    Expired = 3,
}

/// <summary>
/// 策略决策种类。
/// Policy-decision kind.
/// </summary>
public enum PolicyDecisionKind
{
    Allow = 0,
    Deny = 1,
    Escalate = 2,
}

/// <summary>
/// 治理检查点种类。
/// Governance-checkpoint kind.
/// </summary>
public enum GovernanceCheckpointKind
{
    Approval = 0,
    UserInput = 1,
    RiskAcknowledgement = 2,
    PermissionGrant = 3,
}

/// <summary>
/// 审批模型。
/// Approval model.
/// </summary>
public sealed record Approval
{
    /// <summary>
    /// 初始化审批模型。
    /// Initializes an approval model.
    /// </summary>
    public Approval(
        ApprovalId id,
        string title,
        string reason,
        ParticipantRef requestedFromParticipant,
        ApprovalStatus status = ApprovalStatus.Pending,
        ParticipantRef? resolvedByParticipant = null,
        DateTimeOffset? requestedAt = null,
        DateTimeOffset? resolvedAt = null)
    {
        Id = id;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Reason = IdentifierGuard.AgainstNullOrWhiteSpace(reason, nameof(reason));
        RequestedFromParticipant = requestedFromParticipant ?? throw new ArgumentNullException(nameof(requestedFromParticipant));
        Status = status;
        ResolvedByParticipant = resolvedByParticipant;
        RequestedAt = requestedAt ?? DateTimeOffset.UtcNow;
        ResolvedAt = resolvedAt;
    }

    public ApprovalId Id { get; }

    public string Title { get; }

    public string Reason { get; }

    public ParticipantRef RequestedFromParticipant { get; }

    public ApprovalStatus Status { get; }

    public ParticipantRef? ResolvedByParticipant { get; }

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset? ResolvedAt { get; }
}

/// <summary>
/// 用户补录请求模型。
/// User-input request model.
/// </summary>
public sealed record UserInputRequest
{
    /// <summary>
    /// 初始化用户补录请求模型。
    /// Initializes a user-input request model.
    /// </summary>
    public UserInputRequest(
        UserInputRequestId id,
        string prompt,
        ParticipantRef requestedFromParticipant,
        UserInputRequestStatus status = UserInputRequestStatus.Pending,
        ParticipantRef? submittedByParticipant = null,
        DateTimeOffset? requestedAt = null)
    {
        Id = id;
        Prompt = IdentifierGuard.AgainstNullOrWhiteSpace(prompt, nameof(prompt));
        RequestedFromParticipant = requestedFromParticipant ?? throw new ArgumentNullException(nameof(requestedFromParticipant));
        Status = status;
        SubmittedByParticipant = submittedByParticipant;
        RequestedAt = requestedAt ?? DateTimeOffset.UtcNow;
    }

    public UserInputRequestId Id { get; }

    public string Prompt { get; }

    public ParticipantRef RequestedFromParticipant { get; }

    public UserInputRequestStatus Status { get; }

    public ParticipantRef? SubmittedByParticipant { get; }

    public DateTimeOffset RequestedAt { get; }
}

/// <summary>
/// 权限授予模型。
/// Permission-grant model.
/// </summary>
public sealed record PermissionGrant
{
    /// <summary>
    /// 初始化权限授予模型。
    /// Initializes a permission-grant model.
    /// </summary>
    public PermissionGrant(
        string scope,
        ParticipantRef grantedToParticipant,
        ParticipantRef? grantedByParticipant = null,
        DateTimeOffset? grantedAt = null,
        DateTimeOffset? expiresAt = null)
    {
        Scope = IdentifierGuard.AgainstNullOrWhiteSpace(scope, nameof(scope));
        GrantedToParticipant = grantedToParticipant ?? throw new ArgumentNullException(nameof(grantedToParticipant));
        GrantedByParticipant = grantedByParticipant;
        GrantedAt = grantedAt ?? DateTimeOffset.UtcNow;
        ExpiresAt = expiresAt;
    }

    public string Scope { get; }

    public ParticipantRef GrantedToParticipant { get; }

    public ParticipantRef? GrantedByParticipant { get; }

    public DateTimeOffset GrantedAt { get; }

    public DateTimeOffset? ExpiresAt { get; }
}

/// <summary>
/// 策略决策模型。
/// Policy-decision model.
/// </summary>
public sealed record PolicyDecision
{
    /// <summary>
    /// 初始化策略决策模型。
    /// Initializes a policy-decision model.
    /// </summary>
    public PolicyDecision(PolicyDecisionKind kind, string policyKey, string? reason = null)
    {
        Kind = kind;
        PolicyKey = IdentifierGuard.AgainstNullOrWhiteSpace(policyKey, nameof(policyKey));
        Reason = reason;
    }

    public PolicyDecisionKind Kind { get; }

    public string PolicyKey { get; }

    public string? Reason { get; }
}

/// <summary>
/// 治理检查点模型。
/// Governance-checkpoint model.
/// </summary>
public sealed record GovernanceCheckpoint
{
    /// <summary>
    /// 初始化治理检查点模型。
    /// Initializes a governance-checkpoint model.
    /// </summary>
    public GovernanceCheckpoint(
        CallId callId,
        GovernanceCheckpointKind kind,
        string title,
        ParticipantRef requestedFromParticipant,
        PolicyDecision? decision = null)
    {
        CallId = callId;
        Kind = kind;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        RequestedFromParticipant = requestedFromParticipant ?? throw new ArgumentNullException(nameof(requestedFromParticipant));
        Decision = decision;
    }

    public CallId CallId { get; }

    public GovernanceCheckpointKind Kind { get; }

    public string Title { get; }

    public ParticipantRef RequestedFromParticipant { get; }

    public PolicyDecision? Decision { get; }
}
