using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Projections;

namespace TianShu.ControlPlane.Abstractions.Artifacts;

public interface IArtifactControlPlane
{
    Task<Artifact> PublishArtifactAsync(PublishArtifact command, CancellationToken cancellationToken);

    Task<Artifact> PromoteArtifactAsync(PromoteArtifact command, CancellationToken cancellationToken);

    Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask command, CancellationToken cancellationToken);

    Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail query, CancellationToken cancellationToken);

    Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts query, CancellationToken cancellationToken);

    Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery query, CancellationToken cancellationToken);
}
