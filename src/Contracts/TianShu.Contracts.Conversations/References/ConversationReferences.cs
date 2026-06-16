using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 线程引用快照。
/// Thread reference snapshot.
/// </summary>
public sealed record ThreadRef(ThreadId Id, CollaborationSpaceRef CollaborationSpace, string Title)
{
    /// <summary>
    /// 从完整线程模型生成线程引用。
    /// Creates a thread reference from a full thread model.
    /// </summary>
    public static ThreadRef From(Thread thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        return new ThreadRef(thread.Id, thread.CollaborationSpace, thread.Title);
    }
}

/// <summary>
/// 轮次引用快照。
/// Turn reference snapshot.
/// </summary>
public sealed record TurnRef(TurnId Id, ThreadId ThreadId, ParticipantRef InitiatedByParticipant)
{
    /// <summary>
    /// 从完整轮次模型生成轮次引用。
    /// Creates a turn reference from a full turn model.
    /// </summary>
    public static TurnRef From(Turn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        return new TurnRef(turn.Id, turn.ThreadId, turn.InitiatedByParticipant);
    }
}
