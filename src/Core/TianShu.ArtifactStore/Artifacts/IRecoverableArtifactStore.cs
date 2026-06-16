using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// 支持快照恢复的 artifact metadata store 边界。
/// Artifact metadata-store boundary that supports restoring a previously captured snapshot.
/// </summary>
public interface IRecoverableArtifactStore : IArtifactStore
{
    /// <summary>
    /// 将某个 artifact 的 metadata 当前态恢复到指定快照。
    /// Restores the current metadata state of an artifact to the specified snapshot.
    /// </summary>
    Task RestoreAsync(ArtifactId artifactId, ArtifactStoreRecord? snapshot, CancellationToken cancellationToken);
}
