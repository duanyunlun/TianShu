namespace TianShu.Contracts.Agents;

/// <summary>
/// 控制平面代理列表查询。
/// Control-plane query that lists agent threads.
/// </summary>
public sealed record ControlPlaneAgentListQuery
{
    /// <summary>
    /// 分页大小。
    /// Page size.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// 分页游标。
    /// Paging cursor.
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// 是否包含主线程。
    /// Whether primary threads should be included.
    /// </summary>
    public bool IncludePrimaryThreads { get; init; }
}
