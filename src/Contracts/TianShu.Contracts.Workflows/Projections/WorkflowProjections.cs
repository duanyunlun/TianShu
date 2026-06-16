using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 工作流看板。
/// Workflow board.
/// </summary>
public sealed record WorkflowBoard(Workflow Workflow, IReadOnlyList<Task> Tasks)
{
    public IReadOnlyList<Task> Tasks { get; } = Tasks ?? Array.Empty<Task>();
}

/// <summary>
/// 任务看板。
/// Task board.
/// </summary>
public sealed record TaskBoard(WorkflowId WorkflowId, IReadOnlyList<Task> Tasks)
{
    public IReadOnlyList<Task> Tasks { get; } = Tasks ?? Array.Empty<Task>();
}

/// <summary>
/// 计划投影。
/// Plan projection.
/// </summary>
public sealed record PlanProjection(WorkflowId WorkflowId, Plan Plan);
