using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 会话已创建事件。
/// Event emitted when a session has been created.
/// </summary>
public sealed record SessionCreated(SessionId SessionId, CollaborationSpaceId CollaborationSpaceId);

/// <summary>
/// 会话已配置事件。
/// Event emitted when a session has been configured.
/// </summary>
public sealed record SessionConfigured(SessionId SessionId);

/// <summary>
/// 会话模式已变更事件。
/// Event emitted when the session mode has changed.
/// </summary>
public sealed record SessionModeChanged(SessionId SessionId, SessionMode Mode);
