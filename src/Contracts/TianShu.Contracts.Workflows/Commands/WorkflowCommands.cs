using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 创建工作流命令。
/// Command that creates a workflow.
/// </summary>
public sealed record CreateWorkflow(
    WorkflowId WorkflowId,
    CollaborationSpaceId CollaborationSpaceId,
    string DisplayName,
    ParticipantRef? OwnerParticipant = null,
    ThreadId? ThreadId = null);

/// <summary>
/// 发布计划命令。
/// Command that publishes a plan.
/// </summary>
public sealed record PublishPlan(WorkflowId WorkflowId, Plan Plan);

/// <summary>
/// 创建任务命令。
/// Command that creates a workflow task.
/// </summary>
public sealed record CreateTask(Task Task);

/// <summary>
/// 更新任务状态命令。
/// Command that updates the state of a task.
/// </summary>
public sealed record UpdateTaskState(TaskId TaskId, TaskState State, ParticipantRef? OwnerParticipant = null);

/// <summary>
/// 派发作业命令。
/// Command that dispatches a job.
/// </summary>
public sealed record DispatchJob(
    JobId JobId,
    WorkflowId WorkflowId,
    string Title,
    string Kind,
    ParticipantRef? OwnerParticipant = null,
    ThreadId? ThreadId = null);
