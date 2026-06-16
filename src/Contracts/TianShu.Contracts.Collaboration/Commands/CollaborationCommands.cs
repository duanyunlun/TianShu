using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 创建协作空间命令。
/// Command that creates a collaboration space.
/// </summary>
public sealed record CreateCollaborationSpace(
    CollaborationSpaceId SpaceId,
    string Key,
    string DisplayName,
    CollaborationSpaceProfile Profile,
    CollaborationDefaultSet Defaults,
    CollaborationPolicyRef? PolicyRef = null);

/// <summary>
/// 配置协作空间命令。
/// Command that updates collaboration-space settings.
/// </summary>
public sealed record ConfigureCollaborationSpace(
    CollaborationSpaceId SpaceId,
    string? DisplayName = null,
    CollaborationSpaceProfile? Profile = null,
    CollaborationDefaultSet? Defaults = null,
    CollaborationPolicyRef? PolicyRef = null);

/// <summary>
/// 归档协作空间命令。
/// Command that archives a collaboration space.
/// </summary>
public sealed record ArchiveCollaborationSpace(CollaborationSpaceId SpaceId);
