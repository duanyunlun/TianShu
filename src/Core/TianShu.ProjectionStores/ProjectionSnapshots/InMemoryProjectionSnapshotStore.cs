using System.Collections.Concurrent;
using TianShu.Contracts.Projections;

namespace TianShu.ProjectionStores;

/// <summary>
/// 基于进程内内存的投影视图快照存储。
/// In-process memory-backed projection snapshot store.
/// </summary>
public sealed class InMemoryProjectionSnapshotStore : IProjectionSnapshotStore
{
    private readonly ConcurrentDictionary<ProjectionSnapshotKey, ProjectionSnapshotRecord> snapshots = new();

    /// <inheritdoc />
    public Task<ProjectionSnapshotRecord> UpsertAsync(
        ProjectionSnapshotKey key,
        ProjectionDelta delta,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(delta);

        var snapshot = snapshots.AddOrUpdate(
            key,
            static (currentKey, currentDelta) => new ProjectionSnapshotRecord(currentKey, delta: currentDelta),
            static (_, existing, currentDelta) => existing.WithDelta(currentDelta),
            delta);

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<ProjectionSnapshotRecord> ResetAsync(ProjectionReset reset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(reset);

        var key = new ProjectionSnapshotKey(reset.ScopeKind, reset.ScopeKey);
        var snapshot = snapshots.AddOrUpdate(
            key,
            static (_, currentReset) => new ProjectionSnapshotRecord(
                new ProjectionSnapshotKey(currentReset.ScopeKind, currentReset.ScopeKey),
                reset: currentReset),
            static (_, existing, currentReset) => existing.WithReset(currentReset),
            reset);

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public Task<ProjectionSnapshotRecord?> GetAsync(ProjectionSnapshotKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        snapshots.TryGetValue(key, out var snapshot);
        return Task.FromResult(snapshot);
    }
}
