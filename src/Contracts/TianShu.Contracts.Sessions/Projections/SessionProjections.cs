using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 会话概览投影。
/// Session overview projection.
/// </summary>
public sealed record SessionOverviewProjection(
    SessionId SessionId,
    string Title,
    CollaborationSpaceRef CollaborationSpace,
    SessionMode Mode,
    ThreadId? ActiveThreadId = null,
    bool HasActiveTurn = false,
    bool IsClosed = false)
{
    public string Title { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Title, nameof(Title));
}
