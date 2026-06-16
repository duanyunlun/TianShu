using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 参与者引用快照，用于跨域传递最小身份与显示信息。
/// Participant reference snapshot used to move minimal identity and display information across domains.
/// </summary>
public sealed record ParticipantRef(ParticipantId Id, ParticipantKind Kind, string DisplayName)
{
    /// <summary>
    /// 从完整参与者对象生成最小引用。
    /// Creates a minimal reference from a full participant object.
    /// </summary>
    public static ParticipantRef From(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        return new ParticipantRef(participant.Id, participant.Kind, participant.DisplayName);
    }
}
