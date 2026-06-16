using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Sessions;

/// <summary>
/// 创建会话命令。
/// Command that creates a session.
/// </summary>
public sealed record CreateSession(
    SessionId SessionId,
    CollaborationSpaceId CollaborationSpaceId,
    SessionProfile Profile,
    SessionMode Mode = SessionMode.Interactive,
    ConfigStackRef? ConfigStack = null,
    IReadOnlyList<ParticipantRef>? ActiveParticipants = null);

/// <summary>
/// 配置会话命令。
/// Command that configures a session.
/// </summary>
public sealed record ConfigureSession(
    SessionId SessionId,
    SessionProfile? Profile = null,
    ConfigStackRef? ConfigStack = null,
    IReadOnlyList<ParticipantRef>? ActiveParticipants = null);

/// <summary>
/// 设置会话模式命令。
/// Command that changes the session mode.
/// </summary>
public sealed record SetSessionMode(SessionId SessionId, SessionMode Mode);

/// <summary>
/// 关闭会话命令。
/// Command that closes a session.
/// </summary>
public sealed record CloseSession(SessionId SessionId);
