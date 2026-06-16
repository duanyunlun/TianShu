using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// 执行事件种类，表达执行生命周期中的关键状态推进节点。
/// Execution event kind describing the key lifecycle transitions of an execution.
/// </summary>
public enum ExecutionEventKind
{
    Queued = 0,
    Started = 1,
    Progressed = 2,
    Blocked = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}

/// <summary>
/// 统一执行事件契约，用于 typed-first 方式暴露最小事件公共面。
/// Unified execution-event contract exposing the minimal shared surface in a typed-first way.
/// </summary>
public interface IExecutionEvent
{
    ExecutionEventKind Kind { get; }

    ExecutionId ExecutionId { get; }

    DateTimeOffset Timestamp { get; }

    string? Message { get; }

    StructuredValue? Data { get; }
}

/// <summary>
/// 执行已入队事件。
/// Event emitted when an execution has been queued.
/// </summary>
public sealed record ExecutionQueued : IExecutionEvent
{
    /// <summary>
    /// 初始化执行已入队事件。
    /// Initializes the execution-queued event.
    /// </summary>
    public ExecutionQueued(ExecutionId executionId, ExecutionContext context, DateTimeOffset? queuedAt = null)
    {
        ExecutionId = executionId;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        QueuedAt = queuedAt ?? DateTimeOffset.UtcNow;
    }

    public ExecutionId ExecutionId { get; }

    public ExecutionContext Context { get; }

    public DateTimeOffset QueuedAt { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Queued;

    public DateTimeOffset Timestamp => QueuedAt;

    public string? Message => null;

    public StructuredValue? Data => null;
}

/// <summary>
/// 执行已开始事件。
/// Event emitted when an execution has started.
/// </summary>
public sealed record ExecutionStarted : IExecutionEvent
{
    /// <summary>
    /// 初始化执行已开始事件。
    /// Initializes the execution-started event.
    /// </summary>
    public ExecutionStarted(
        ExecutionId executionId,
        int attemptNumber,
        DateTimeOffset startedAt,
        string? message = null,
        StructuredValue? data = null)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "尝试次数必须从 1 开始。");
        }

        ExecutionId = executionId;
        AttemptNumber = attemptNumber;
        StartedAt = startedAt;
        Message = message;
        Data = data;
    }

    public ExecutionId ExecutionId { get; }

    public int AttemptNumber { get; }

    public DateTimeOffset StartedAt { get; }

    public string? Message { get; }

    public StructuredValue? Data { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Started;

    public DateTimeOffset Timestamp => StartedAt;
}

/// <summary>
/// 执行进度事件。
/// Event emitted when an execution has progressed.
/// </summary>
public sealed record ExecutionProgressed : IExecutionEvent
{
    /// <summary>
    /// 初始化执行进度事件。
    /// Initializes the execution-progressed event.
    /// </summary>
    public ExecutionProgressed(
        ExecutionId executionId,
        string stage,
        DateTimeOffset? occurredAt = null,
        string? message = null,
        StructuredValue? data = null)
    {
        ExecutionId = executionId;
        Stage = IdentifierGuard.AgainstNullOrWhiteSpace(stage, nameof(stage));
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
        Message = message;
        Data = data;
    }

    public ExecutionId ExecutionId { get; }

    public string Stage { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? Message { get; }

    public StructuredValue? Data { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Progressed;

    public DateTimeOffset Timestamp => OccurredAt;
}

/// <summary>
/// 执行阻塞事件。
/// Event emitted when an execution has been blocked.
/// </summary>
public sealed record ExecutionBlocked : IExecutionEvent
{
    /// <summary>
    /// 初始化执行阻塞事件。
    /// Initializes the execution-blocked event.
    /// </summary>
    public ExecutionBlocked(
        ExecutionId executionId,
        CallId blockingCallId,
        string reason,
        DateTimeOffset? occurredAt = null,
        ResumeHint? resumeHint = null)
    {
        ExecutionId = executionId;
        BlockingCallId = blockingCallId;
        Reason = IdentifierGuard.AgainstNullOrWhiteSpace(reason, nameof(reason));
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
        ResumeHint = resumeHint;
    }

    public ExecutionId ExecutionId { get; }

    public CallId BlockingCallId { get; }

    public string Reason { get; }

    public DateTimeOffset OccurredAt { get; }

    public ResumeHint? ResumeHint { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Blocked;

    public DateTimeOffset Timestamp => OccurredAt;

    public string? Message => Reason;

    public StructuredValue? Data => ResumeHint?.ResumeInput;
}

/// <summary>
/// 执行完成事件。
/// Event emitted when an execution has completed.
/// </summary>
public sealed record ExecutionCompleted : IExecutionEvent
{
    /// <summary>
    /// 初始化执行完成事件。
    /// Initializes the execution-completed event.
    /// </summary>
    public ExecutionCompleted(
        ExecutionId executionId,
        DateTimeOffset? occurredAt = null,
        ExecutionOutputRef? output = null,
        string? message = null,
        StructuredValue? data = null)
    {
        ExecutionId = executionId;
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
        Output = output;
        Message = message;
        Data = data;
    }

    public ExecutionId ExecutionId { get; }

    public DateTimeOffset OccurredAt { get; }

    public ExecutionOutputRef? Output { get; }

    public string? Message { get; }

    public StructuredValue? Data { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Completed;

    public DateTimeOffset Timestamp => OccurredAt;
}

/// <summary>
/// 执行失败事件。
/// Event emitted when an execution has failed.
/// </summary>
public sealed record ExecutionFailed : IExecutionEvent
{
    /// <summary>
    /// 初始化执行失败事件。
    /// Initializes the execution-failed event.
    /// </summary>
    public ExecutionFailed(
        ExecutionId executionId,
        ExecutionFailure failure,
        int attemptNumber,
        DateTimeOffset? occurredAt = null)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "尝试次数必须从 1 开始。");
        }

        ExecutionId = executionId;
        Failure = failure ?? throw new ArgumentNullException(nameof(failure));
        AttemptNumber = attemptNumber;
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
    }

    public ExecutionId ExecutionId { get; }

    public ExecutionFailure Failure { get; }

    public int AttemptNumber { get; }

    public DateTimeOffset OccurredAt { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Failed;

    public DateTimeOffset Timestamp => OccurredAt;

    public string? Message => Failure.Message;

    public StructuredValue? Data => Failure.Details;
}

/// <summary>
/// 执行取消事件。
/// Event emitted when an execution has been cancelled.
/// </summary>
public sealed record ExecutionCancelled : IExecutionEvent
{
    /// <summary>
    /// 初始化执行取消事件。
    /// Initializes the execution-cancelled event.
    /// </summary>
    public ExecutionCancelled(ExecutionId executionId, DateTimeOffset? occurredAt = null, string? reason = null)
    {
        ExecutionId = executionId;
        OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
        Reason = reason;
    }

    public ExecutionId ExecutionId { get; }

    public DateTimeOffset OccurredAt { get; }

    public string? Reason { get; }

    public ExecutionEventKind Kind => ExecutionEventKind.Cancelled;

    public DateTimeOffset Timestamp => OccurredAt;

    public string? Message => Reason;

    public StructuredValue? Data => null;
}
