using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 参与者已完成绑定。
/// Event emitted when a participant has been bound to a scope.
/// </summary>
public sealed record ParticipantBound(ParticipantId ParticipantId);

/// <summary>
/// 参与者角色已变更。
/// Event emitted when a participant role has changed.
/// </summary>
public sealed record ParticipantRoleChanged(ParticipantId ParticipantId, string Role);

/// <summary>
/// 参与者在线状态已变更。
/// Event emitted when a participant presence state has changed.
/// </summary>
public sealed record ParticipantPresenceChanged(ParticipantId ParticipantId, bool IsPresent);
