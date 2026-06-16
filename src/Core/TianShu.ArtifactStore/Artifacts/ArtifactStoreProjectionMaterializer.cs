using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact store 投影视图物化辅助器，统一负责单 artifact 与聚合 artifact collection 的 snapshot 写入。
/// Artifact-store projection materializer that centrally writes both single-artifact and aggregate artifact-collection snapshots.
/// </summary>
internal static class ArtifactStoreProjectionMaterializer
{
    /// <summary>
    /// 将当前 artifact 记录及其协作空间聚合记录物化为 projection snapshots。
    /// Materializes the current artifact record and its collaboration-space aggregate records into projection snapshots.
    /// </summary>
    public static async Task MaterializeAsync(
        IProjectionSnapshotStore? projectionSnapshotStore,
        ArtifactStoreRecord record,
        IReadOnlyList<ArtifactStoreRecord> collaborationRecords,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(collaborationRecords);

        await MaterializeArtifactAsync(projectionSnapshotStore, record, cancellationToken).ConfigureAwait(false);
        await MaterializeCollectionAsync(
            projectionSnapshotStore,
            record.Artifact.CollaborationSpace,
            collaborationRecords,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 仅物化单 artifact 快照。
    /// Materializes only the single-artifact snapshot.
    /// </summary>
    public static async Task MaterializeArtifactAsync(
        IProjectionSnapshotStore? projectionSnapshotStore,
        ArtifactStoreRecord record,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(record);

        await projectionSnapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Artifact, record.Artifact.Id.Value),
            new ProjectionDelta(
                new ArtifactProjectionPayload(
                    new ArtifactProjection(
                        record.Artifact.Id,
                        record.Artifact.Name,
                        record.Artifact.Kind,
                        record.Artifact.State,
                        record.Artifact.CollaborationSpace,
                        record.Artifact.ProducedByParticipant))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 仅物化某个协作空间下的 artifact 集合快照。
    /// Materializes only the artifact-collection snapshot for a collaboration space.
    /// </summary>
    public static async Task MaterializeCollectionAsync(
        IProjectionSnapshotStore? projectionSnapshotStore,
        CollaborationSpaceRef collaborationSpace,
        IReadOnlyList<ArtifactStoreRecord> collaborationRecords,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(collaborationSpace);
        ArgumentNullException.ThrowIfNull(collaborationRecords);

        var items = collaborationRecords
            .OrderByDescending(static current => current.UpdatedAt)
            .ThenBy(static current => current.Artifact.Name, StringComparer.Ordinal)
            .Select(static current => new ArtifactCollectionItem(
                current.Artifact.Id,
                current.Artifact.Name,
                current.Artifact.Kind,
                current.Artifact.State,
                current.Artifact.ProducedByParticipant,
                current.PromotionChannels,
                current.AttachedTaskIds,
                current.UpdatedAt))
            .ToArray();

        await projectionSnapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.ArtifactCollection, collaborationSpace.Id.Value),
            new ProjectionDelta(
                new ArtifactCollectionProjectionPayload(
                    new ArtifactCollectionProjection(
                        collaborationSpace,
                        items))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重置单 artifact 快照。
    /// Resets the single-artifact snapshot.
    /// </summary>
    public static Task ResetArtifactAsync(
        IProjectionSnapshotStore? projectionSnapshotStore,
        ArtifactId artifactId,
        string reason,
        CancellationToken cancellationToken)
        => projectionSnapshotStore is null
            ? Task.CompletedTask
            : projectionSnapshotStore.ResetAsync(
                new ProjectionReset(ProjectionScopeKind.Artifact, artifactId.Value, reason),
                cancellationToken);

    /// <summary>
    /// 重置某个协作空间下的 artifact 集合快照。
    /// Resets the artifact-collection snapshot for a collaboration space.
    /// </summary>
    public static Task ResetCollectionAsync(
        IProjectionSnapshotStore? projectionSnapshotStore,
        CollaborationSpaceRef collaborationSpace,
        string reason,
        CancellationToken cancellationToken)
        => projectionSnapshotStore is null
            ? Task.CompletedTask
            : projectionSnapshotStore.ResetAsync(
                new ProjectionReset(ProjectionScopeKind.ArtifactCollection, collaborationSpace.Id.Value, reason),
                cancellationToken);
}
