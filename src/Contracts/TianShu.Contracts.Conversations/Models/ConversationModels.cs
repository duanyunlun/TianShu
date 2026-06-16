using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 轮次状态，表示一条线程内当前轮次的推进情况。
/// Turn state describing the progression of a turn within a thread.
/// </summary>
public enum TurnState
{
    Submitted = 0,
    Running = 1,
    Interrupted = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>
/// 线程模型，表示一条可恢复、可分叉的交互轨道。
/// Thread model representing a resumable and forkable interaction lane.
/// </summary>
public sealed record Thread
{
    /// <summary>
    /// 初始化线程模型。
    /// Initializes a thread model.
    /// </summary>
    public Thread(
        ThreadId id,
        CollaborationSpaceRef collaborationSpace,
        string title,
        ParticipantRef? initiatedByParticipant = null,
        bool isArchived = false)
    {
        Id = id;
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        InitiatedByParticipant = initiatedByParticipant;
        IsArchived = isArchived;
    }

    public ThreadId Id { get; }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public string Title { get; }

    public ParticipantRef? InitiatedByParticipant { get; }

    public bool IsArchived { get; }
}

/// <summary>
/// 轮次模型，表示线程中的一次具体推进。
/// Turn model representing a concrete advancement within a thread.
/// </summary>
public sealed record Turn
{
    /// <summary>
    /// 初始化轮次模型。
    /// Initializes a turn model.
    /// </summary>
    public Turn(
        TurnId id,
        ThreadId threadId,
        InteractionEnvelopeRef interactionEnvelope,
        ParticipantRef initiatedByParticipant,
        CollaborationSpaceRef collaborationSpace,
        TurnState state = TurnState.Submitted,
        string? summary = null,
        DateTimeOffset? createdAt = null)
    {
        Id = id;
        ThreadId = threadId;
        InteractionEnvelope = interactionEnvelope ?? throw new ArgumentNullException(nameof(interactionEnvelope));
        InitiatedByParticipant = initiatedByParticipant ?? throw new ArgumentNullException(nameof(initiatedByParticipant));
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        State = state;
        Summary = summary;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public TurnId Id { get; }

    public ThreadId ThreadId { get; }

    public InteractionEnvelopeRef InteractionEnvelope { get; }

    public ParticipantRef InitiatedByParticipant { get; }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public TurnState State { get; }

    public string? Summary { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 线程快照，表示线程的只读摘要信息。
/// Thread snapshot representing the read-only summary of a thread.
/// </summary>
public sealed record ThreadSnapshot(ThreadRef Thread, TurnId? ActiveTurnId, int TurnCount, bool HasActiveTurn);
