using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 控制平面线程列表查询。
/// Control-plane query that lists threads.
/// </summary>
public sealed record ControlPlaneThreadListQuery
{
    public int Limit { get; init; } = 20;

    public string? Cursor { get; init; }

    public bool Archived { get; init; }

    public string? WorkingDirectory { get; init; }

    public string SortKey { get; init; } = "created_at";

    public IReadOnlyList<string> ModelProviders { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ControlPlaneThreadSourceKind> SourceKinds { get; init; } = Array.Empty<ControlPlaneThreadSourceKind>();

    public string? SearchTerm { get; init; }
}

/// <summary>
/// 控制平面已加载线程列表查询。
/// Control-plane query that lists loaded threads.
/// </summary>
public sealed record ControlPlaneLoadedThreadListQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }
}

/// <summary>
/// 控制平面读取线程详情查询。
/// Control-plane query that reads thread details.
/// </summary>
public sealed record ControlPlaneReadThreadQuery
{
    public ThreadId ThreadId { get; init; }

    public bool IncludeTurns { get; init; }
}

/// <summary>
/// 控制平面模糊文件搜索查询。
/// Control-plane query that performs fuzzy file search.
/// </summary>
public sealed record ControlPlaneFuzzyFileSearchQuery
{
    public string Query { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public int? Limit { get; init; }

    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
}
