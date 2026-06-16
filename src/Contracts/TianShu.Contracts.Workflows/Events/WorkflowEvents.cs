using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 工作流已创建事件。
/// Event emitted when a workflow has been created.
/// </summary>
public sealed record WorkflowCreated(WorkflowId WorkflowId, CollaborationSpaceId CollaborationSpaceId);

/// <summary>
/// 计划已发布事件。
/// Event emitted when a plan has been published.
/// </summary>
public sealed record PlanPublished(WorkflowId WorkflowId, string Title)
{
    public string Title { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Title, nameof(Title));
}

/// <summary>
/// 任务状态已变更事件。
/// Event emitted when the state of a task has changed.
/// </summary>
public sealed record TaskStateChanged(TaskId TaskId, TaskState State);

/// <summary>
/// 作业已派发事件。
/// Event emitted when a job has been dispatched.
/// </summary>
public sealed record JobDispatched(JobId JobId, WorkflowId WorkflowId);
