using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// Recoverable artifact content store boundary for stronger coordinated-commit compensation.
/// 用于更强联合提交补偿语义的可恢复 artifact content store 边界。
/// </summary>
public interface IRecoverableArtifactContentStore : IArtifactContentStore
{
    /// <summary>
    /// Captures the current content snapshot before a coordinated mutation.
    /// 在协调写入前捕获 content 当前恢复快照。
    /// </summary>
    Task<ArtifactContentStoreSnapshot?> CaptureAsync(ArtifactId artifactId, CancellationToken cancellationToken);

    /// <summary>
    /// Restores the current content state to a previously captured snapshot.
    /// 将 content 当前状态恢复到先前捕获的快照。
    /// </summary>
    Task RestoreAsync(ArtifactId artifactId, ArtifactContentStoreSnapshot? snapshot, CancellationToken cancellationToken);
}
