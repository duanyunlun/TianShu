using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Agents;

/// <summary>
/// 控制平面代理花名册结果。
/// Control-plane agent-roster result.
/// </summary>
public sealed record ControlPlaneAgentRosterResult
{
    /// <summary>
    /// 代理列表。
    /// Agent list.
    /// </summary>
    public IReadOnlyList<ControlPlaneAgentDescriptor> Agents { get; init; } = Array.Empty<ControlPlaneAgentDescriptor>();

    /// <summary>
    /// 下一页游标。
    /// Next-page cursor.
    /// </summary>
    public string? NextCursor { get; init; }
}

/// <summary>
/// 控制平面代理线程注册结果。
/// Control-plane result returned after registering an agent thread.
/// </summary>
public sealed record ControlPlaneAgentThreadRegistrationResult
{
    /// <summary>
    /// 已注册的代理描述。
    /// Registered agent descriptor.
    /// </summary>
    public ControlPlaneAgentDescriptor? Agent { get; init; }
}

/// <summary>
/// 控制平面注册代理线程命令。
/// Control-plane command that registers an agent thread.
/// </summary>
public sealed record ControlPlaneRegisterAgentThreadCommand
{
    /// <summary>
    /// 线程标识。
    /// Thread identifier.
    /// </summary>
    public ThreadId ThreadId { get; init; }

    /// <summary>
    /// 代理昵称。
    /// Agent nickname.
    /// </summary>
    public string? AgentNickname { get; init; }

    /// <summary>
    /// 代理角色文本。
    /// Agent role text.
    /// </summary>
    public string? AgentRole { get; init; }
}

/// <summary>
/// 控制平面代理描述符。
/// Control-plane agent descriptor.
/// </summary>
public sealed record ControlPlaneAgentDescriptor
{
    /// <summary>
    /// 线程标识。
    /// Thread identifier.
    /// </summary>
    public ThreadId ThreadId { get; init; }

    /// <summary>
    /// 线程预览。
    /// Thread preview.
    /// </summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>
    /// 线程名称。
    /// Thread display name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 工作目录。
    /// Working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 会话路径。
    /// Session path.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 来源类型。
    /// Source kind.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// 代理昵称。
    /// Agent nickname.
    /// </summary>
    public string? AgentNickname { get; init; }

    /// <summary>
    /// 代理角色。
    /// Agent role.
    /// </summary>
    public string? AgentRole { get; init; }

    /// <summary>
    /// 创建时间。
    /// Creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// 更新时间。
    /// Last-updated timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// 是否临时线程。
    /// Whether the thread is ephemeral.
    /// </summary>
    public bool IsEphemeral { get; init; }

    /// <summary>
    /// 状态文本。
    /// Status text.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// 活跃标记集合。
    /// Active flag collection.
    /// </summary>
    public IReadOnlyList<string> ActiveFlags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 代理谱系。
    /// Agent lineage.
    /// </summary>
    public ControlPlaneAgentLineage? Lineage { get; init; }
}

/// <summary>
/// 控制平面代理谱系。
/// Control-plane lineage for an agent thread.
/// </summary>
public sealed record ControlPlaneAgentLineage
{
    /// <summary>
    /// 父线程标识。
    /// Parent thread identifier.
    /// </summary>
    public ThreadId? ParentThreadId { get; init; }

    /// <summary>
    /// 委派深度。
    /// Delegation depth.
    /// </summary>
    public int? Depth { get; init; }
}
