using System.Collections.Concurrent;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Primitives;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// 基于进程内内存的 artifact 当前态存储。
/// In-process memory-backed artifact current-state store.
/// </summary>
public sealed class InMemoryArtifactStore : IRecoverableArtifactStore
{
    private readonly ConcurrentDictionary<ArtifactId, ArtifactStoreRecord> records = new();
    private readonly IProjectionSnapshotStore? projectionSnapshotStore;

    /// <summary>
    /// 初始化内存 artifact store。
    /// Initializes the in-memory artifact store.
    /// </summary>
    public InMemoryArtifactStore(IProjectionSnapshotStore? projectionSnapshotStore = null)
    {
        this.projectionSnapshotStore = projectionSnapshotStore;
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> PublishAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);

        var publishedArtifact = NormalizeLifecycleState(artifact, ArtifactLifecycleState.Published);
        var record = records.AddOrUpdate(
            publishedArtifact.Id,
            static (_, currentArtifact) => new ArtifactStoreRecord(currentArtifact),
            static (_, existing, currentArtifact) => existing.WithArtifact(currentArtifact),
            publishedArtifact);

        await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public Task<ArtifactStoreRecord?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        records.TryGetValue(artifactId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ArtifactStoreRecord>> ListAsync(ListArtifacts query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        var results = records.Values
            .Where(record => query.CollaborationSpaceId is null
                || string.Equals(record.Artifact.CollaborationSpace.Id.Value, query.CollaborationSpaceId.Value, StringComparison.Ordinal))
            .Where(record => query.ProducedByParticipantId is null
                || string.Equals(record.Artifact.ProducedByParticipant?.Id.Value, query.ProducedByParticipantId.Value, StringComparison.Ordinal))
            .OrderByDescending(static record => record.UpdatedAt)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ArtifactStoreRecord>>(results);
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> PromoteAsync(ArtifactId artifactId, string targetChannel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedChannel = IdentifierGuard.AgainstNullOrWhiteSpace(targetChannel, nameof(targetChannel));

        var record = records.AddOrUpdate(
            artifactId,
            static (_, _) => throw new KeyNotFoundException("未找到待提升的 artifact。"),
            static (_, existing, currentTargetChannel) =>
            {
                var promotedArtifact = NormalizeLifecycleState(existing.Artifact, ArtifactLifecycleState.Promoted);
                var nextRecord = existing.WithArtifact(promotedArtifact);
                return nextRecord.WithPromotionChannel(currentTargetChannel);
            },
            normalizedChannel);

        await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task<ArtifactStoreRecord> AttachToTaskAsync(ArtifactId artifactId, TaskId taskId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var record = records.AddOrUpdate(
            artifactId,
            static (_, _) => throw new KeyNotFoundException("未找到待挂接的 artifact。"),
            static (_, existing, currentTaskId) => existing.WithTaskAttachment(currentTaskId),
            taskId);

        await MaterializeProjectionsAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(ArtifactId artifactId, ArtifactStoreRecord? snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        records.TryGetValue(artifactId, out var currentRecord);
        if (snapshot is null)
        {
            records.TryRemove(artifactId, out _);
        }
        else
        {
            records[artifactId] = snapshot;
        }

        await RefreshProjectionStateAfterRestoreAsync(currentRecord, snapshot, cancellationToken).ConfigureAwait(false);
    }

    private async Task MaterializeProjectionsAsync(ArtifactStoreRecord record, CancellationToken cancellationToken)
    {
        var collaborationRecords = records.Values
            .Where(current => string.Equals(
                current.Artifact.CollaborationSpace.Id.Value,
                record.Artifact.CollaborationSpace.Id.Value,
                StringComparison.Ordinal))
            .ToArray();

        await ArtifactStoreProjectionMaterializer.MaterializeAsync(
            projectionSnapshotStore,
            record,
            collaborationRecords,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshProjectionStateAfterRestoreAsync(
        ArtifactStoreRecord? currentRecord,
        ArtifactStoreRecord? restoredSnapshot,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return;
        }

        if (restoredSnapshot is null)
        {
            await ArtifactStoreProjectionMaterializer.ResetArtifactAsync(
                projectionSnapshotStore,
                currentRecord?.Artifact.Id ?? throw new InvalidOperationException("恢复空快照时缺少当前 artifact。"),
                reason: "artifact_store_restore",
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ArtifactStoreProjectionMaterializer.MaterializeArtifactAsync(
                projectionSnapshotStore,
                restoredSnapshot,
                cancellationToken).ConfigureAwait(false);
        }

        var affectedSpaces = new Dictionary<string, CollaborationSpaceRef>(StringComparer.Ordinal);
        if (currentRecord is not null)
        {
            affectedSpaces[currentRecord.Artifact.CollaborationSpace.Id.Value] = currentRecord.Artifact.CollaborationSpace;
        }

        if (restoredSnapshot is not null)
        {
            affectedSpaces[restoredSnapshot.Artifact.CollaborationSpace.Id.Value] = restoredSnapshot.Artifact.CollaborationSpace;
        }

        foreach (var collaborationSpace in affectedSpaces.Values)
        {
            var collaborationRecords = records.Values
                .Where(current => string.Equals(
                    current.Artifact.CollaborationSpace.Id.Value,
                    collaborationSpace.Id.Value,
                    StringComparison.Ordinal))
                .ToArray();

            if (collaborationRecords.Length == 0)
            {
                await ArtifactStoreProjectionMaterializer.ResetCollectionAsync(
                    projectionSnapshotStore,
                    collaborationSpace,
                    reason: "artifact_store_restore",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            await ArtifactStoreProjectionMaterializer.MaterializeCollectionAsync(
                projectionSnapshotStore,
                collaborationSpace,
                collaborationRecords,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Artifact NormalizeLifecycleState(Artifact artifact, ArtifactLifecycleState state)
        => new(
            artifact.Id,
            artifact.CollaborationSpace,
            artifact.Name,
            artifact.Kind,
            artifact.ProducedByParticipant,
            artifact.Lineage,
            state,
            artifact.ExecutionTrace,
            artifact.Metadata);
}
