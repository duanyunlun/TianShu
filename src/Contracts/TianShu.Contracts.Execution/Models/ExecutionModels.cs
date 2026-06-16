using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// 执行种类，表示执行引擎当前承载的运行任务类型。
/// Execution kind describing which kind of runtime workload is currently carried by the execution engine.
/// </summary>
public enum ExecutionKind
{
    ProviderTurn = 0,
    ToolInvocation = 1,
    EnvironmentAction = 2,
    ArtifactProcessing = 3,
    ProjectionRebuild = 4,
}

/// <summary>
/// 执行上下文，表达一次执行请求所属的协作域和发起来源。
/// Execution context describing the collaboration scope and initiator of an execution request.
/// </summary>
public sealed record ExecutionContext
{
    /// <summary>
    /// 初始化执行上下文。
    /// Initializes an execution context.
    /// </summary>
    public ExecutionContext(
        CollaborationSpaceRef collaborationSpace,
        InteractionEnvelopeRef interactionEnvelope,
        ParticipantRef initiatedByParticipant,
        ThreadId? threadId = null,
        TurnId? turnId = null,
        WorkflowId? workflowId = null,
        JobId? jobId = null,
        string? workingDirectory = null,
        MetadataBag? metadata = null)
    {
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        InteractionEnvelope = interactionEnvelope ?? throw new ArgumentNullException(nameof(interactionEnvelope));
        InitiatedByParticipant = initiatedByParticipant ?? throw new ArgumentNullException(nameof(initiatedByParticipant));
        ThreadId = threadId;
        TurnId = turnId;
        WorkflowId = workflowId;
        JobId = jobId;
        WorkingDirectory = workingDirectory;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public InteractionEnvelopeRef InteractionEnvelope { get; }

    public ParticipantRef InitiatedByParticipant { get; }

    public ThreadId? ThreadId { get; }

    public TurnId? TurnId { get; }

    public WorkflowId? WorkflowId { get; }

    public JobId? JobId { get; }

    public string? WorkingDirectory { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 执行请求，表达控制平面对执行引擎的正式运行指令。
/// Execution request expressing the formal runtime instruction from the control plane to the execution engine.
/// </summary>
public sealed record ExecutionRequest
{
    /// <summary>
    /// 初始化执行请求。
    /// Initializes an execution request.
    /// </summary>
    public ExecutionRequest(
        ExecutionId executionId,
        ExecutionKind kind,
        string action,
        ExecutionContext context,
        StructuredValue? input = null,
        DateTimeOffset? createdAt = null)
    {
        ExecutionId = executionId;
        Kind = kind;
        Action = IdentifierGuard.AgainstNullOrWhiteSpace(action, nameof(action));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Input = input;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public ExecutionId ExecutionId { get; }

    public ExecutionKind Kind { get; }

    public string Action { get; }

    public ExecutionContext Context { get; }

    public StructuredValue? Input { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 执行尝试，表达一次执行在重试链路中的当前 attempt。
/// Execution attempt describing the current attempt within a retry chain.
/// </summary>
public sealed record ExecutionAttempt
{
    /// <summary>
    /// 初始化执行尝试。
    /// Initializes an execution attempt.
    /// </summary>
    public ExecutionAttempt(
        int attemptNumber,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt = null,
        string? nativeHandleId = null)
    {
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber), "尝试次数必须从 1 开始。");
        }

        AttemptNumber = attemptNumber;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        NativeHandleId = nativeHandleId;
    }

    public int AttemptNumber { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public string? NativeHandleId { get; }
}

/// <summary>
/// 恢复线索，表达执行被阻塞后如何恢复。
/// Resume hint describing how an execution can be resumed after being blocked.
/// </summary>
public sealed record ResumeHint(
    CallId? BlockingCallId = null,
    string? Reason = null,
    StructuredValue? ResumeInput = null);

/// <summary>
/// 执行失败模型。
/// Execution failure model.
/// </summary>
public sealed record ExecutionFailure
{
    /// <summary>
    /// 初始化执行失败模型。
    /// Initializes an execution failure model.
    /// </summary>
    public ExecutionFailure(
        string code,
        string message,
        bool isRetryable = false,
        StructuredValue? details = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        IsRetryable = isRetryable;
        Details = details;
    }

    public string Code { get; }

    public string Message { get; }

    public bool IsRetryable { get; }

    public StructuredValue? Details { get; }
}

/// <summary>
/// 执行句柄，表达执行引擎暴露给控制平面的当前运行状态锚点。
/// Execution handle representing the current runtime anchor exposed by the execution engine to the control plane.
/// </summary>
public sealed record ExecutionHandle
{
    /// <summary>
    /// 初始化执行句柄。
    /// Initializes an execution handle.
    /// </summary>
    public ExecutionHandle(
        ExecutionId executionId,
        ExecutionKind kind,
        ExecutionContext context,
        ExecutionAttempt currentAttempt,
        MetadataBag? metadata = null)
    {
        ExecutionId = executionId;
        Kind = kind;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        CurrentAttempt = currentAttempt ?? throw new ArgumentNullException(nameof(currentAttempt));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public ExecutionId ExecutionId { get; }

    public ExecutionKind Kind { get; }

    public ExecutionContext Context { get; }

    public ExecutionAttempt CurrentAttempt { get; }

    public MetadataBag Metadata { get; }
}
