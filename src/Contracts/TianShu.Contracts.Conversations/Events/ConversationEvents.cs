using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 线程已创建事件。
/// Event emitted when a thread has been created.
/// </summary>
public sealed record ThreadCreated(ThreadId ThreadId, CollaborationSpaceId CollaborationSpaceId);

/// <summary>
/// 轮次已提交事件。
/// Event emitted when a turn has been submitted.
/// </summary>
public sealed record TurnSubmitted(ThreadId ThreadId, TurnId TurnId);

/// <summary>
/// 轮次已中断事件。
/// Event emitted when a turn has been interrupted.
/// </summary>
public sealed record TurnInterrupted(ThreadId ThreadId, TurnId TurnId, string? Reason = null);

/// <summary>
/// 线程已分叉事件。
/// Event emitted when a thread has been forked.
/// </summary>
public sealed record ThreadForked(ThreadId SourceThreadId, ThreadId NewThreadId);
