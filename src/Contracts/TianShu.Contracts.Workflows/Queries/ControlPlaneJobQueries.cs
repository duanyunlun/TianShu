using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Workflows;

/// <summary>
/// 控制平面读取作业查询。
/// Control-plane query that reads a job snapshot.
/// </summary>
public sealed record ControlPlaneReadJobQuery
{
    /// <summary>
    /// 待读取的作业标识。
    /// Identifier of the job to read.
    /// </summary>
    public JobId JobId { get; init; }
}

/// <summary>
/// 控制平面列出作业查询。
/// Control-plane query that lists job snapshots.
/// </summary>
public sealed record ControlPlaneListJobsQuery
{
    /// <summary>
    /// 状态过滤；为空时默认列出未完成作业。
    /// Status filter; when omitted, active jobs are listed by default.
    /// </summary>
    public IReadOnlyList<string> Statuses { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 最大返回数量；为空时使用执行侧默认值。
    /// Maximum number of returned jobs; execution side default is used when omitted.
    /// </summary>
    public int? Limit { get; init; }
}
