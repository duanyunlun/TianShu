using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Projections;
using TianShu.ControlPlane.Abstractions.Artifacts;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public Task<Artifact> PublishArtifactAsync(
        PublishArtifact command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.PublishArtifactAsync(command, cancellationToken);
    }

    public Task<Artifact> PromoteArtifactAsync(
        PromoteArtifact command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.PromoteArtifactAsync(command, cancellationToken);
    }

    public Task<Artifact> AttachArtifactToTaskAsync(
        AttachArtifactToTask command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.AttachArtifactToTaskAsync(command, cancellationToken);
    }

    public Task<ArtifactProjection?> GetArtifactProjectionAsync(
        GetArtifactDetail query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetArtifactProjectionAsync(query, cancellationToken);
    }

    public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(
        ListArtifacts query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetArtifactCollectionProjectionAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(
        ControlPlaneConversationArtifactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.GetConversationSummaryAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(
        ControlPlaneGitDiffArtifactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await runtime.GetGitDiffToRemoteAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
