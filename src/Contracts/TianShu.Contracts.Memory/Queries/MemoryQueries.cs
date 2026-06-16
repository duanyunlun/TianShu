using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// 查询记忆覆盖层。
/// Query that resolves a memory overlay.
/// </summary>
public sealed record ResolveMemoryOverlay(
    MemorySpaceId? MemorySpaceId = null,
    CollaborationSpaceId? CollaborationSpaceId = null,
    string? QueryText = null,
    MemorySearchMode SearchMode = MemorySearchMode.Keyword,
    bool AllowInjection = true);

/// <summary>
/// 查询记忆空间列表。
/// Query that lists memory spaces.
/// </summary>
public sealed record ListMemorySpaces(MemoryScopeKind? ScopeKind = null);

/// <summary>
/// 筛选结构化记忆事实。
/// Filters structured memory facts.
/// </summary>
public sealed record FilterMemory(
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    string? QueryText = null,
    MemorySearchMode SearchMode = MemorySearchMode.Structured,
    MemoryLifecycleStatus? LifecycleStatus = null,
    decimal? MinimumConfidence = null,
    MemorySourceKind? SourceKind = null,
    string? SourceId = null,
    string? Tag = null,
    long? MinimumUsageCount = null,
    DateTimeOffset? RecordedAfter = null,
    DateTimeOffset? RecordedBefore = null,
    DateTimeOffset? UpdatedAfter = null,
    DateTimeOffset? UpdatedBefore = null,
    DateTimeOffset? UsedAfter = null,
    DateTimeOffset? UsedBefore = null,
    MemoryScopeKind? ScopeKind = null,
    MemoryContextSignature? ContextSignature = null);

/// <summary>
/// 查询待审记忆的可见审核项。
/// Lists visible memory review items.
/// </summary>
public sealed record ListMemoryReviews(
    MemorySpaceId? MemorySpaceId = null,
    string? Key = null,
    MemoryLifecycleStatus? LifecycleStatus = MemoryLifecycleStatus.PendingReview,
    bool IncludeEvidence = true,
    bool IncludeSupersedeLinks = true,
    bool IncludeAudit = true);

/// <summary>
/// 列出可用记忆 provider。
/// Lists available memory providers.
/// </summary>
public sealed record ListMemoryProviders(MemoryScopeKind? ScopeKind = null);
