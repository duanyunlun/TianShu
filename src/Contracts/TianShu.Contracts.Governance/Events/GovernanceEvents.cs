using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance;

/// <summary>
/// 审批已请求事件。
/// Event emitted when an approval has been requested.
/// </summary>
public sealed record ApprovalRequested(ApprovalId ApprovalId, ParticipantId RequestedFromParticipantId);

/// <summary>
/// 审批已解析事件。
/// Event emitted when an approval has been resolved.
/// </summary>
public sealed record ApprovalResolved(
    ApprovalId ApprovalId,
    ApprovalStatus Status,
    ParticipantId ResolvedByParticipantId);

/// <summary>
/// 用户补录已请求事件。
/// Event emitted when a user-input request has been created.
/// </summary>
public sealed record UserInputRequested(UserInputRequestId RequestId, ParticipantId RequestedFromParticipantId);
