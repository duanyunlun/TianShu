using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 控制平面对话摘要查询。
/// Control-plane query that resolves a conversation summary artifact.
/// </summary>
public sealed record ControlPlaneConversationArtifactQuery
{
    /// <summary>
    /// 线程过滤条件。
    /// Optional thread filter.
    /// </summary>
    public ThreadId? ThreadId { get; init; }

    /// <summary>
    /// rollout 文件路径。
    /// Rollout file path.
    /// </summary>
    public string? RolloutPath { get; init; }
}

/// <summary>
/// 控制平面远端差异查询。
/// Control-plane query that resolves the diff against remote.
/// </summary>
public sealed record ControlPlaneGitDiffArtifactQuery
{
    /// <summary>
    /// 目标线程标识。
    /// Target thread identifier.
    /// </summary>
    public ThreadId ThreadId { get; init; }
}
