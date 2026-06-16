using TianShu.Contracts.Agents;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Workflows;

namespace TianShu.Contracts.Projections;

/// <summary>
/// 订阅令牌，表示一个投影订阅实例。
/// Subscription token that represents a projection-subscription instance.
/// </summary>
public readonly record struct SubscriptionToken
{
    public SubscriptionToken(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(SubscriptionToken value) => value.Value;
}

/// <summary>
/// 投影作用域种类。
/// Projection scope kind.
/// </summary>
public enum ProjectionScopeKind
{
    CollaborationSpace = 0,
    Thread = 1,
    WorkflowBoard = 2,
    TaskBoard = 3,
    Participant = 4,
    AgentRoster = 5,
    ApprovalQueue = 6,
    Artifact = 7,
    ArtifactCollection = 8,
    Plan = 9,
    Team = 10,
}

/// <summary>
/// 投影订阅请求。
/// Projection-subscription request.
/// </summary>
public sealed record ProjectionSubscription
{
    /// <summary>
    /// 初始化投影订阅请求。
    /// Initializes a projection-subscription request.
    /// </summary>
    public ProjectionSubscription(
        SubscriptionToken token,
        ProjectionScopeKind scopeKind,
        string scopeKey,
        ProjectionCursor? cursor = null,
        bool includeHistory = false)
    {
        Token = token;
        ScopeKind = scopeKind;
        ScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(scopeKey, nameof(scopeKey));
        Cursor = cursor;
        IncludeHistory = includeHistory;
    }

    public SubscriptionToken Token { get; }

    public ProjectionScopeKind ScopeKind { get; }

    public string ScopeKey { get; }

    public ProjectionCursor? Cursor { get; }

    public bool IncludeHistory { get; }
}

/// <summary>
/// 投影视图统一包络基类。
/// Unified base type for projection payloads.
/// </summary>
public abstract record ProjectionPayload(string Kind);

/// <summary>
/// 协作空间投影视图包络。
/// Projection payload for collaboration-space projections.
/// </summary>
public sealed record CollaborationSpaceProjectionPayload(CollaborationSpaceProjection Projection)
    : ProjectionPayload("collaboration_space");

/// <summary>
/// 线程投影视图包络。
/// Projection payload for thread projections.
/// </summary>
public sealed record ThreadProjectionPayload(ThreadProjection Projection)
    : ProjectionPayload("thread");

/// <summary>
/// 工作流看板投影视图包络。
/// Projection payload for workflow-board projections.
/// </summary>
public sealed record WorkflowBoardProjectionPayload(WorkflowBoardProjection Projection)
    : ProjectionPayload("workflow_board");

/// <summary>
/// 任务看板投影视图包络。
/// Projection payload for task-board projections.
/// </summary>
public sealed record TaskBoardProjectionPayload(TaskBoardProjection Projection)
    : ProjectionPayload("task_board");

/// <summary>
/// 计划投影视图包络。
/// Projection payload for plan projections.
/// </summary>
public sealed record PlanProjectionPayload(PlanProjection Projection)
    : ProjectionPayload("plan");

/// <summary>
/// 参与者投影视图包络。
/// Projection payload for participant projections.
/// </summary>
public sealed record ParticipantProjectionPayload(ParticipantProjection Projection)
    : ProjectionPayload("participant");

/// <summary>
/// 代理花名册投影视图包络。
/// Projection payload for agent-roster projections.
/// </summary>
public sealed record AgentRosterProjectionPayload(AgentRosterProjection Projection)
    : ProjectionPayload("agent_roster");

/// <summary>
/// 团队投影视图包络。
/// Projection payload for team projections.
/// </summary>
public sealed record TeamProjectionPayload(TeamProjection Projection)
    : ProjectionPayload("team");

/// <summary>
/// 审批队列投影视图包络。
/// Projection payload for approval-queue projections.
/// </summary>
public sealed record ApprovalQueueProjectionPayload(ApprovalQueueProjection Projection)
    : ProjectionPayload("approval_queue");

/// <summary>
/// 产物投影视图包络。
/// Projection payload for artifact projections.
/// </summary>
public sealed record ArtifactProjectionPayload(ArtifactProjection Projection)
    : ProjectionPayload("artifact");

/// <summary>
/// Artifact 集合投影视图包络。
/// Projection payload for artifact-collection projections.
/// </summary>
public sealed record ArtifactCollectionProjectionPayload(ArtifactCollectionProjection Projection)
    : ProjectionPayload("artifact_collection");

/// <summary>
/// 投影增量。
/// Projection delta.
/// </summary>
public sealed record ProjectionDelta(ProjectionPayload Payload, ProjectionCursor? Cursor = null);

/// <summary>
/// 投影重置事件。
/// Projection reset event.
/// </summary>
public sealed record ProjectionReset
{
    /// <summary>
    /// 初始化投影重置事件。
    /// Initializes a projection-reset event.
    /// </summary>
    public ProjectionReset(
        ProjectionScopeKind scopeKind,
        string scopeKey,
        string reason,
        ProjectionCursor? cursor = null)
    {
        ScopeKind = scopeKind;
        ScopeKey = IdentifierGuard.AgainstNullOrWhiteSpace(scopeKey, nameof(scopeKey));
        Reason = IdentifierGuard.AgainstNullOrWhiteSpace(reason, nameof(reason));
        Cursor = cursor;
    }

    public ProjectionScopeKind ScopeKind { get; }

    public string ScopeKey { get; }

    public string Reason { get; }

    public ProjectionCursor? Cursor { get; }
}
