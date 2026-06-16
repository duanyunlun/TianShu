using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 协作空间已创建事件。
/// Event emitted when a collaboration space has been created.
/// </summary>
public sealed record CollaborationSpaceCreated(CollaborationSpaceId SpaceId, string Key);

/// <summary>
/// 协作空间已配置事件。
/// Event emitted when a collaboration space has been configured.
/// </summary>
public sealed record CollaborationSpaceConfigured(CollaborationSpaceId SpaceId);

/// <summary>
/// 协作空间已归档事件。
/// Event emitted when a collaboration space has been archived.
/// </summary>
public sealed record CollaborationSpaceArchived(CollaborationSpaceId SpaceId);
