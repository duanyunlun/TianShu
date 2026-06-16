using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 代理角色。
/// Agent role.
/// </summary>
public enum AgentRole
{
    Coordinator = 0,
    Implementer = 1,
    Reviewer = 2,
    Explorer = 3,
    Worker = 4,
}

/// <summary>
/// 委派模式。
/// Delegation mode.
/// </summary>
public enum DelegationMode
{
    Manual = 0,
    Automatic = 1,
    Escalated = 2,
}

/// <summary>
/// 代理谱系，表示代理之间的父子关系。
/// Agent lineage describing the parent-child relationship between agents.
/// </summary>
public sealed record AgentLineage
{
    /// <summary>
    /// 初始化代理谱系。
    /// Initializes agent lineage.
    /// </summary>
    public AgentLineage(AgentId? parentAgentId, int depth)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "深度不能为负。");
        }

        ParentAgentId = parentAgentId;
        Depth = depth;
    }

    public AgentId? ParentAgentId { get; }

    public int Depth { get; }
}

/// <summary>
/// 委派策略，表达代理是否允许并行和多级委派。
/// Delegation policy expressing whether an agent allows parallel and multi-level delegation.
/// </summary>
public sealed record DelegationPolicy
{
    /// <summary>
    /// 初始化委派策略。
    /// Initializes a delegation policy.
    /// </summary>
    public DelegationPolicy(DelegationMode mode, bool allowParallelDelegation, int maxChildren)
    {
        if (maxChildren < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChildren), "最大子代理数量不能为负。");
        }

        Mode = mode;
        AllowParallelDelegation = allowParallelDelegation;
        MaxChildren = maxChildren;
    }

    public DelegationMode Mode { get; }

    public bool AllowParallelDelegation { get; }

    public int MaxChildren { get; }
}

/// <summary>
/// 代理模型，表示一个正式执行主体。
/// Agent model representing a formal execution actor.
/// </summary>
public sealed record Agent
{
    /// <summary>
    /// 初始化代理模型。
    /// Initializes an agent model.
    /// </summary>
    public Agent(
        AgentId id,
        ParticipantRef agentParticipant,
        string displayName,
        AgentRole role,
        AgentLineage? lineage = null,
        DelegationPolicy? delegationPolicy = null,
        WorkflowId? assignedWorkflowId = null,
        bool isStopped = false)
    {
        Id = id;
        AgentParticipant = agentParticipant ?? throw new ArgumentNullException(nameof(agentParticipant));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Role = role;
        Lineage = lineage;
        DelegationPolicy = delegationPolicy;
        AssignedWorkflowId = assignedWorkflowId;
        IsStopped = isStopped;
    }

    public AgentId Id { get; }

    public ParticipantRef AgentParticipant { get; }

    public string DisplayName { get; }

    public AgentRole Role { get; }

    public AgentLineage? Lineage { get; }

    public DelegationPolicy? DelegationPolicy { get; }

    public WorkflowId? AssignedWorkflowId { get; }

    public bool IsStopped { get; }
}

/// <summary>
/// 团队模型，表示多个代理组成的协作单元。
/// Team model representing a collaboration unit composed of multiple agents.
/// </summary>
public sealed record Team
{
    /// <summary>
    /// 初始化团队模型。
    /// Initializes a team model.
    /// </summary>
    public Team(
        TeamId id,
        string displayName,
        IReadOnlyList<AgentId>? agentIds = null,
        WorkflowId? workflowId = null)
    {
        Id = id;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        AgentIds = agentIds ?? Array.Empty<AgentId>();
        WorkflowId = workflowId;
    }

    public TeamId Id { get; }

    public string DisplayName { get; }

    public IReadOnlyList<AgentId> AgentIds { get; }

    public WorkflowId? WorkflowId { get; }
}
