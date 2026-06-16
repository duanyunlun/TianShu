using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Primitives;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact 当前态存储边界，负责承接发布、读取、查询、提升与任务挂接。
/// Artifact current-state store boundary responsible for publish, read, query, promote, and task-attachment flows.
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// 发布一份 artifact，并返回当前最新记录。
    /// Publishes an artifact and returns the latest stored record.
    /// </summary>
    Task<ArtifactStoreRecord> PublishAsync(Artifact artifact, CancellationToken cancellationToken);

    /// <summary>
    /// 读取单个 artifact 当前记录。
    /// Reads the current record for a single artifact.
    /// </summary>
    Task<ArtifactStoreRecord?> GetAsync(ArtifactId artifactId, CancellationToken cancellationToken);

    /// <summary>
    /// 按最小过滤条件列出 artifact 记录。
    /// Lists artifact records using the minimal supported filters.
    /// </summary>
    Task<IReadOnlyList<ArtifactStoreRecord>> ListAsync(ListArtifacts query, CancellationToken cancellationToken);

    /// <summary>
    /// 将 artifact 提升到指定目标通道。
    /// Promotes an artifact into the target channel.
    /// </summary>
    Task<ArtifactStoreRecord> PromoteAsync(ArtifactId artifactId, string targetChannel, CancellationToken cancellationToken);

    /// <summary>
    /// 将 artifact 挂接到任务。
    /// Attaches an artifact to a task.
    /// </summary>
    Task<ArtifactStoreRecord> AttachToTaskAsync(ArtifactId artifactId, TaskId taskId, CancellationToken cancellationToken);
}
