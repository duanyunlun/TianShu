using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact 正文存储边界，负责承接当前内容的 typed 写入与读取。
/// Artifact content-store boundary responsible for typed writes and reads of the current content binding.
/// </summary>
public interface IArtifactContentStore
{
    /// <summary>
    /// 写入某个 artifact 的当前内容，并返回最新绑定。
    /// Writes the current content for an artifact and returns the latest binding.
    /// </summary>
    Task<ArtifactContentBinding> WriteAsync(ArtifactId artifactId, ArtifactContent content, CancellationToken cancellationToken);

    /// <summary>
    /// 读取某个 artifact 的当前内容绑定。
    /// Reads the current content binding for an artifact.
    /// </summary>
    Task<ArtifactContentBinding?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken);

    /// <summary>
    /// 读取某个 artifact 的指定历史版本。
    /// Reads a specific historical version for an artifact.
    /// </summary>
    Task<ArtifactContentBinding?> GetVersionAsync(ArtifactId artifactId, long version, CancellationToken cancellationToken);

    /// <summary>
    /// 列出某个 artifact 已保存的历史版本快照。
    /// Lists persisted historical version snapshots for an artifact.
    /// </summary>
    Task<IReadOnlyList<ArtifactContentBinding>> ListVersionsAsync(ArtifactId artifactId, CancellationToken cancellationToken);
}
