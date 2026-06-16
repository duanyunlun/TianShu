using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration;

/// <summary>
/// 协作空间概览投影，供宿主层只读展示。
/// Read-only collaboration-space overview projection for host-facing display.
/// </summary>
public sealed record CollaborationSpaceOverviewProjection(
    CollaborationSpaceId SpaceId,
    string Key,
    string DisplayName,
    bool IsArchived);
