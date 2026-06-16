namespace TianShu.Contracts.Agents;

/// <summary>
/// 代理花名册快照。
/// Agent-roster snapshot.
/// </summary>
public sealed record AgentRosterSnapshot(IReadOnlyList<Agent> Agents)
{
    public IReadOnlyList<Agent> Agents { get; } = Agents ?? Array.Empty<Agent>();
}

/// <summary>
/// 团队投影。
/// Team projection.
/// </summary>
public sealed record TeamProjection(Team Team, IReadOnlyList<Agent> Members)
{
    public IReadOnlyList<Agent> Members { get; } = Members ?? Array.Empty<Agent>();
}
