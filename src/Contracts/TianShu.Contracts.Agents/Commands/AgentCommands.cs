using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 创建子代理命令。
/// Command that spawns an agent.
/// </summary>
public sealed record SpawnAgent(
    AgentId AgentId,
    ParticipantRef AgentParticipant,
    string DisplayName,
    AgentRole Role,
    AgentId? ParentAgentId = null,
    WorkflowId? AssignedWorkflowId = null,
    DelegationPolicy? DelegationPolicy = null);

/// <summary>
/// 恢复代理命令。
/// Command that resumes an agent.
/// </summary>
public sealed record ResumeAgent(AgentId AgentId);

/// <summary>
/// 停止代理命令。
/// Command that stops an agent.
/// </summary>
public sealed record StopAgent(AgentId AgentId, string? Reason = null);

/// <summary>
/// 创建团队命令。
/// Command that creates a team.
/// </summary>
public sealed record CreateTeam(TeamId TeamId, string DisplayName, WorkflowId? WorkflowId = null);

/// <summary>
/// 指派任务负责人命令。
/// Command that assigns an agent as the owner of a task.
/// </summary>
public sealed record AssignTaskOwner(TaskId TaskId, AgentId AgentId);
