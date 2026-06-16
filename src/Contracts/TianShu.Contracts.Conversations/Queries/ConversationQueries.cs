using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Conversations;

/// <summary>
/// 查询线程投影。
/// Query that fetches a thread projection.
/// </summary>
public sealed record GetThreadProjection(ThreadId ThreadId);

/// <summary>
/// 查询线程列表。
/// Query that lists threads.
/// </summary>
public sealed record ListThreads(CollaborationSpaceId? CollaborationSpaceId = null, bool IncludeArchived = false);
