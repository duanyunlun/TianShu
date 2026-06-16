using System.Text.Json;
using TianShu.Contracts.Artifacts;
using TianShu.ProjectionStores;

namespace TianShu.ArtifactStore;

/// <summary>
/// 基于文件系统的 artifact metadata/content 联合提交实现。
/// File-system-backed implementation for coordinated artifact metadata/content commits.
/// </summary>
public sealed class FileSystemArtifactBundleStore : IArtifactBundleStore, IDisposable
{
    private readonly FileSystemArtifactStore metadataStore;
    private readonly FileSystemArtifactContentStore contentStore;
    private readonly CoordinatedArtifactBundleStore innerStore;

    /// <summary>
    /// 初始化文件系统 artifact 联合提交 store。
    /// Initializes the file-system-backed artifact coordinated-commit store.
    /// </summary>
    public FileSystemArtifactBundleStore(
        string rootDirectory,
        IProjectionSnapshotStore? projectionSnapshotStore = null,
        ArtifactContentStoreRetentionOptions? retentionOptions = null,
        ArtifactContentStoreSyncOptions? syncOptions = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        metadataStore = new FileSystemArtifactStore(rootDirectory, projectionSnapshotStore, serializerOptions);
        contentStore = new FileSystemArtifactContentStore(rootDirectory, retentionOptions, syncOptions, serializerOptions);
        innerStore = new CoordinatedArtifactBundleStore(metadataStore, contentStore);
    }

    /// <inheritdoc />
    public Task<ArtifactBundleCommitResult> PublishWithContentAsync(
        Artifact artifact,
        ArtifactContent content,
        CancellationToken cancellationToken)
        => innerStore.PublishWithContentAsync(artifact, content, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        contentStore.Dispose();
        metadataStore.Dispose();
    }
}
