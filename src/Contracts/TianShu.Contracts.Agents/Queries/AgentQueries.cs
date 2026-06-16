using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 查询代理花名册。
/// Query that fetches the agent roster.
/// </summary>
public sealed record GetAgentRoster(WorkflowId? WorkflowId = null);

/// <summary>
/// 查询团队投影。
/// Query that fetches a team projection.
/// </summary>
public sealed record GetTeamProjection(TeamId TeamId);
