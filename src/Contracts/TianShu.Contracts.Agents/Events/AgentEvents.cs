using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 代理已创建事件。
/// Event emitted when an agent has been spawned.
/// </summary>
public sealed record AgentSpawned(AgentId AgentId, ParticipantId ParticipantId);

/// <summary>
/// 代理已停止事件。
/// Event emitted when an agent has been stopped.
/// </summary>
public sealed record AgentStopped(AgentId AgentId);

/// <summary>
/// 团队已创建事件。
/// Event emitted when a team has been created.
/// </summary>
public sealed record TeamCreated(TeamId TeamId);
