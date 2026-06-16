using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Projections;

namespace TianShu.Execution.Runtime;

public interface IArtifactControlPlaneClient
{
    Task<Artifact> PublishArtifactAsync(PublishArtifact request, CancellationToken cancellationToken);

    Task<Artifact> PromoteArtifactAsync(PromoteArtifact request, CancellationToken cancellationToken);

    Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask request, CancellationToken cancellationToken);

    Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail request, CancellationToken cancellationToken);

    Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts request, CancellationToken cancellationToken);

    Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery request, CancellationToken cancellationToken);
}
