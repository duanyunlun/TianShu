using System.Collections.Concurrent;
using TianShu.Contracts.Diagnostics;

namespace TianShu.ProjectionStores;

/// <summary>
/// 基于进程内内存的回放检查点存储。
/// In-process memory-backed replay checkpoint store.
/// </summary>
public sealed class InMemoryReplayCheckpointStore : IReplayCheckpointStore
{
    private readonly ConcurrentDictionary<ReplayCheckpointKey, RecoveryCheckpoint> checkpoints = new();

    /// <inheritdoc />
    public Task<RecoveryCheckpoint> SaveAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(checkpoint);

        checkpoints[ReplayCheckpointKey.From(checkpoint)] = checkpoint;
        return Task.FromResult(checkpoint);
    }

    /// <inheritdoc />
    public Task<RecoveryCheckpoint?> GetAsync(ReplayCheckpointKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkpoints.TryGetValue(key, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    /// <inheritdoc />
    public Task RemoveAsync(ReplayCheckpointKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        checkpoints.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
