using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 创建线程命令。
/// Command that creates a thread.
/// </summary>
public sealed record CreateThread(
    ThreadId ThreadId,
    CollaborationSpaceId CollaborationSpaceId,
    string Title,
    ParticipantRef? InitiatedByParticipant = null);

/// <summary>
/// 提交轮次命令。
/// Command that submits a turn.
/// </summary>
public sealed record SubmitTurn(
    ThreadId ThreadId,
    TurnId TurnId,
    InteractionEnvelopeRef InteractionEnvelope,
    ParticipantRef InitiatedByParticipant);

/// <summary>
/// 中断轮次命令。
/// Command that interrupts a turn.
/// </summary>
public sealed record InterruptTurn(ThreadId ThreadId, TurnId TurnId, string? Reason = null);

/// <summary>
/// 恢复线程命令。
/// Command that resumes a thread.
/// </summary>
public sealed record ResumeThread(ThreadId ThreadId, string? Reason = null, StructuredValue? Input = null);

/// <summary>
/// 分叉线程命令。
/// Command that forks a thread.
/// </summary>
public sealed record ForkThread(ThreadId SourceThreadId, ThreadId NewThreadId, string Title);

/// <summary>
/// 压缩线程命令。
/// Command that compacts a thread.
/// </summary>
public sealed record CompactThread(ThreadId ThreadId, string Summary);
