using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 查询工作流看板。
/// Query that fetches a workflow board.
/// </summary>
public sealed record GetWorkflowBoard(WorkflowId WorkflowId);

/// <summary>
/// 查询任务看板。
/// Query that fetches a task board.
/// </summary>
public sealed record GetTaskBoard(WorkflowId WorkflowId);

/// <summary>
/// 查询计划投影。
/// Query that fetches a plan projection.
/// </summary>
public sealed record GetPlanProjection(WorkflowId WorkflowId);
