using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 查询单个会话概览。
/// Query that fetches a single session overview.
/// </summary>
public sealed record GetSessionOverview(SessionId SessionId);

/// <summary>
/// 查询会话列表。
/// Query that lists sessions.
/// </summary>
public sealed record ListSessions(CollaborationSpaceId? CollaborationSpaceId = null, bool IncludeClosed = false);
