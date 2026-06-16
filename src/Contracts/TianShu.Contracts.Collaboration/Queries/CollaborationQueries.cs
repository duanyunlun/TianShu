using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 查询单个协作空间概览。
/// Query that fetches a single collaboration-space overview.
/// </summary>
public sealed record GetCollaborationSpaceOverview(CollaborationSpaceId SpaceId);

/// <summary>
/// 查询单个协作空间展示投影。
/// Query that fetches a single collaboration-space view projection.
/// </summary>
public sealed record GetCollaborationSpaceProjection(CollaborationSpaceId SpaceId);

/// <summary>
/// 查询协作空间列表。
/// Query that lists collaboration spaces.
/// </summary>
public sealed record ListCollaborationSpaces(bool IncludeArchived = false);
