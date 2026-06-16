using TianShu.Contracts.Primitives;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Contracts.Host;

/// <summary>
/// 宿主线程列表查询。
/// Host query that lists threads.
/// </summary>
public sealed record HostListThreadsQuery
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
/// 宿主已加载线程列表查询。
/// Host query that lists loaded threads.
/// </summary>
public sealed record HostListLoadedThreadsQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }
}

/// <summary>
/// 宿主线程详情查询。
/// Host query that reads thread details.
/// </summary>
public sealed record HostReadThreadQuery
{
    public ThreadId ThreadId { get; init; }

    public bool IncludeTurns { get; init; }
}

/// <summary>
/// 宿主会话摘要工件查询。
/// Host query that reads a conversation-summary artifact.
/// </summary>
public sealed record HostReadConversationSummaryQuery
{
    public ThreadId? ThreadId { get; init; }

    public string? RolloutPath { get; init; }
}

/// <summary>
/// 宿主远端 Git Diff 工件查询。
/// Host query that reads a git-diff-to-remote artifact.
/// </summary>
public sealed record HostReadGitDiffToRemoteQuery
{
    public ThreadId ThreadId { get; init; }
}

/// <summary>
/// 宿主能力目录查询。
/// Host query that reads the capability catalog.
/// </summary>
public sealed record HostGetCapabilityCatalogQuery
{
    public string? WorkspacePath { get; init; }

    public bool IncludeHiddenModels { get; init; }

    public int ModelLimit { get; init; } = 200;
}

/// <summary>
/// 宿主引擎绑定解析查询。
/// Host query that resolves an engine binding.
/// </summary>
public sealed record HostResolveEngineBindingQuery
{
    public string? WorkspacePath { get; init; }

    public string? PreferredProviderKey { get; init; }

    public string? PreferredModelKey { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? ReasoningSummary { get; init; }

    public string? Verbosity { get; init; }

    public bool PreferWebsocketTransport { get; init; }
}

/// <summary>
/// 宿主代理列表查询。
/// Host query that lists agents.
/// </summary>
public sealed record HostListAgentsQuery
{
    public int? Limit { get; init; }

    public string? Cursor { get; init; }

    public bool IncludePrimaryThreads { get; init; }
}

/// <summary>
/// 宿主执行追踪查询。
/// Host query that reads one execution trace.
/// </summary>
public sealed record HostReadExecutionTraceQuery
{
    public ExecutionTraceId TraceId { get; init; }
}

/// <summary>
/// 宿主执行尝试摘要列表查询。
/// Host query that lists execution attempt summaries.
/// </summary>
public sealed record HostListAttemptSummariesQuery
{
    public ExecutionId ExecutionId { get; init; }
}
