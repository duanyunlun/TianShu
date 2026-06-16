using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 参与者只读投影。
/// Read-only participant projection.
/// </summary>
public sealed record ParticipantProjection(
    ParticipantId ParticipantId,
    ParticipantKind Kind,
    string DisplayName,
    string Role);
