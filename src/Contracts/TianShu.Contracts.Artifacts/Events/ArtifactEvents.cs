using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Artifacts;

/// <summary>
/// 产物已发布事件。
/// Event emitted when an artifact has been published.
/// </summary>
public sealed record ArtifactPublished(ArtifactId ArtifactId, CollaborationSpaceId CollaborationSpaceId);

/// <summary>
/// 产物已提升事件。
/// Event emitted when an artifact has been promoted.
/// </summary>
public sealed record ArtifactPromoted(ArtifactId ArtifactId, string TargetChannel)
{
    public string TargetChannel { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(TargetChannel, nameof(TargetChannel));
}
