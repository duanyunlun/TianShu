using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 查询单个参与者投影。
/// Query that fetches a single participant projection.
/// </summary>
public sealed record GetParticipantProjection(ParticipantId ParticipantId);

/// <summary>
/// 查询单个参与者展示投影。
/// Query that fetches a single participant view projection.
/// </summary>
public sealed record GetParticipantViewProjection(ParticipantId ParticipantId);

/// <summary>
/// 查询某个协作空间内的参与者列表。
/// Query that lists participants inside a collaboration space.
/// </summary>
public sealed record ListParticipantsInScope(CollaborationSpaceId CollaborationSpaceId);
