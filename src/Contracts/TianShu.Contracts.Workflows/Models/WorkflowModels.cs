using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 工作流状态。
/// Workflow state.
/// </summary>
public enum WorkflowState
{
    Draft = 0,
    Active = 1,
    Paused = 2,
    Completed = 3,
    Archived = 4,
}

/// <summary>
/// 任务状态。
/// Task state.
/// </summary>
public enum TaskState
{
    Todo = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3,
    Cancelled = 4,
}

/// <summary>
/// 验证门状态。
/// Verification-gate state.
/// </summary>
public enum VerificationGateState
{
    Pending = 0,
    Passed = 1,
    Failed = 2,
    Waived = 3,
}

/// <summary>
/// 工作流模型。
/// Workflow model.
/// </summary>
public sealed record Workflow
{
    /// <summary>
    /// 初始化工作流模型。
    /// Initializes a workflow model.
    /// </summary>
    public Workflow(
        WorkflowId id,
        CollaborationSpaceRef collaborationSpace,
        string displayName,
        WorkflowState state = WorkflowState.Draft,
        ParticipantRef? ownerParticipant = null,
        ThreadId? threadId = null)
    {
        Id = id;
        CollaborationSpace = collaborationSpace ?? throw new ArgumentNullException(nameof(collaborationSpace));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        State = state;
        OwnerParticipant = ownerParticipant;
        ThreadId = threadId;
    }

    public WorkflowId Id { get; }

    public CollaborationSpaceRef CollaborationSpace { get; }

    public string DisplayName { get; }

    public WorkflowState State { get; }

    public ParticipantRef? OwnerParticipant { get; }

    public ThreadId? ThreadId { get; }
}

/// <summary>
/// 验证门模型。
/// Verification-gate model.
/// </summary>
public sealed record VerificationGate(string Name, VerificationGateState State, string? Criteria = null)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));
}

/// <summary>
/// 计划步骤模型。
/// Plan-step model.
/// </summary>
public sealed record PlanStep
{
    /// <summary>
    /// 初始化计划步骤。
    /// Initializes a plan step.
    /// </summary>
    public PlanStep(int order, string title, string? description = null, VerificationGate? verificationGate = null)
    {
        if (order < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "步骤顺序不能为负。");
        }

        Order = order;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Description = description;
        VerificationGate = verificationGate;
    }

    public int Order { get; }

    public string Title { get; }

    public string? Description { get; }

    public VerificationGate? VerificationGate { get; }
}

/// <summary>
/// 计划模型。
/// Plan model.
/// </summary>
public sealed record Plan
{
    /// <summary>
    /// 初始化计划模型。
    /// Initializes a plan model.
    /// </summary>
    public Plan(string title, IReadOnlyList<PlanStep> steps, DateTimeOffset? publishedAt = null)
    {
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Steps = steps ?? Array.Empty<PlanStep>();
        PublishedAt = publishedAt;
    }

    public string Title { get; }

    public IReadOnlyList<PlanStep> Steps { get; }

    public DateTimeOffset? PublishedAt { get; }
}

/// <summary>
/// 工作流任务模型。
/// Workflow task model.
/// </summary>
public sealed record Task
{
    /// <summary>
    /// 初始化工作流任务。
    /// Initializes a workflow task.
    /// </summary>
    public Task(
        TaskId id,
        WorkflowId workflowId,
        string title,
        TaskState state = TaskState.Todo,
        ParticipantRef? ownerParticipant = null,
        ArtifactRef? outputArtifact = null)
    {
        Id = id;
        WorkflowId = workflowId;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        State = state;
        OwnerParticipant = ownerParticipant;
        OutputArtifact = outputArtifact;
    }

    public TaskId Id { get; }

    public WorkflowId WorkflowId { get; }

    public string Title { get; }

    public TaskState State { get; }

    public ParticipantRef? OwnerParticipant { get; }

    public ArtifactRef? OutputArtifact { get; }
}

/// <summary>
/// 工作流作业模型。
/// Workflow job model.
/// </summary>
public sealed record Job
{
    /// <summary>
    /// 初始化工作流作业。
    /// Initializes a workflow job.
    /// </summary>
    public Job(
        JobId id,
        WorkflowId workflowId,
        string title,
        string kind,
        ParticipantRef? ownerParticipant = null,
        ThreadId? threadId = null)
    {
        Id = id;
        WorkflowId = workflowId;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Kind = IdentifierGuard.AgainstNullOrWhiteSpace(kind, nameof(kind));
        OwnerParticipant = ownerParticipant;
        ThreadId = threadId;
    }

    public JobId Id { get; }

    public WorkflowId WorkflowId { get; }

    public string Title { get; }

    public string Kind { get; }

    public ParticipantRef? OwnerParticipant { get; }

    public ThreadId? ThreadId { get; }
}
