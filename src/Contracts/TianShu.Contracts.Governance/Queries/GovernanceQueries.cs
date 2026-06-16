using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance;

/// <summary>
/// 查询待处理审批列表。
/// Query that lists pending approvals.
/// </summary>
public sealed record ListPendingApprovals(ParticipantId? RequestedFromParticipantId = null);

/// <summary>
/// 查询待处理补录请求列表。
/// Query that lists pending user-input requests.
/// </summary>
public sealed record ListUserInputRequests(ParticipantId? RequestedFromParticipantId = null);
