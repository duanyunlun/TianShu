using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 查询产物列表。
/// Query that lists artifacts.
/// </summary>
public sealed record ListArtifacts(
    CollaborationSpaceId? CollaborationSpaceId = null,
    ParticipantId? ProducedByParticipantId = null);

/// <summary>
/// 查询产物详情。
/// Query that fetches artifact detail.
/// </summary>
public sealed record GetArtifactDetail(ArtifactId ArtifactId);
