using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants;

/// <summary>
/// 参与者种类，统一表达人类、代理、服务和自动化主体。
/// Participant kind unifying human, agent, service, and automation actors.
/// </summary>
public enum ParticipantKind
{
    Human = 0,
    Agent = 1,
    Service = 2,
    Automation = 3,
}

/// <summary>
/// 代理谱系，记录当前代理参与者与其上游参与者的关系。
/// Agent lineage capturing the relationship between the current agent participant and its upstream participant.
/// </summary>
public sealed record AgentLineage
{
    /// <summary>
    /// 初始化代理谱系。
    /// Initializes the agent lineage.
    /// </summary>
    public AgentLineage(ParticipantId? parentParticipantId, int depth)
    {
        if (depth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "深度不能为负。");
        }

        ParentParticipantId = parentParticipantId;
        Depth = depth;
    }

    public ParticipantId? ParentParticipantId { get; }

    public int Depth { get; }
}

/// <summary>
/// 统一参与者基类，表示任何会进入协作状态空间的主体。
/// Unified participant base type representing any actor that can enter the collaboration state space.
/// </summary>
public abstract record Participant
{
    /// <summary>
    /// 初始化参与者并校验显示名与角色。
    /// Initializes the participant while validating display name and role.
    /// </summary>
    protected Participant(
        ParticipantId id,
        ParticipantKind kind,
        string displayName,
        string role,
        string? description = null,
        MetadataBag? metadata = null)
    {
        Id = id;
        Kind = kind;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Role = IdentifierGuard.AgainstNullOrWhiteSpace(role, nameof(role));
        Description = description;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public ParticipantId Id { get; }

    public ParticipantKind Kind { get; }

    public string DisplayName { get; }

    public string Role { get; }

    public string? Description { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 人类参与者。
/// Human participant.
/// </summary>
public sealed record HumanParticipant : Participant
{
    public HumanParticipant(
        ParticipantId id,
        string displayName,
        string role,
        string? description = null,
        MetadataBag? metadata = null)
        : base(id, ParticipantKind.Human, displayName, role, description, metadata)
    {
    }
}

/// <summary>
/// 代理参与者，表示一个正式的代理执行主体。
/// Agent participant representing a formal agent execution actor.
/// </summary>
public sealed record AgentParticipant : Participant
{
    public AgentParticipant(
        ParticipantId id,
        string displayName,
        AgentId agentId,
        string role,
        AgentLineage? lineage = null,
        string? description = null,
        MetadataBag? metadata = null)
        : base(id, ParticipantKind.Agent, displayName, role, description, metadata)
    {
        AgentId = agentId;
        Lineage = lineage;
    }

    public AgentId AgentId { get; }

    public AgentLineage? Lineage { get; }
}

/// <summary>
/// 服务参与者，表示系统中的长期服务型主体。
/// Service participant representing a long-lived service actor in the system.
/// </summary>
public sealed record ServiceParticipant : Participant
{
    public ServiceParticipant(
        ParticipantId id,
        string displayName,
        string role,
        string? description = null,
        MetadataBag? metadata = null)
        : base(id, ParticipantKind.Service, displayName, role, description, metadata)
    {
    }
}

/// <summary>
/// 自动化参与者，表示由计划或外部触发驱动的自动主体。
/// Automation participant representing an automated actor driven by plans or external triggers.
/// </summary>
public sealed record AutomationParticipant : Participant
{
    public AutomationParticipant(
        ParticipantId id,
        string displayName,
        string role,
        string? description = null,
        MetadataBag? metadata = null)
        : base(id, ParticipantKind.Automation, displayName, role, description, metadata)
    {
    }
}
