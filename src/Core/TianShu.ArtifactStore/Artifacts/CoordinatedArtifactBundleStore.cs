using TianShu.Contracts.Artifacts;

namespace TianShu.ArtifactStore;

/// <summary>
/// 基于 metadata store 与 content store 的 artifact 联合提交协调器。
/// Artifact coordinated-commit store that composes a metadata store and a content store.
/// </summary>
public sealed class CoordinatedArtifactBundleStore : IArtifactBundleStore
{
    private readonly IRecoverableArtifactStore metadataStore;
    private readonly IArtifactContentStore contentStore;

    /// <summary>
    /// 初始化 artifact 联合提交协调器。
    /// Initializes the artifact coordinated-commit store.
    /// </summary>
    public CoordinatedArtifactBundleStore(
        IRecoverableArtifactStore metadataStore,
        IArtifactContentStore contentStore)
    {
        this.metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
        this.contentStore = contentStore ?? throw new ArgumentNullException(nameof(contentStore));
    }

    /// <inheritdoc />
    public async Task<ArtifactBundleCommitResult> PublishWithContentAsync(
        Artifact artifact,
        ArtifactContent content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(content);

        var previousSnapshot = await metadataStore.GetAsync(artifact.Id, cancellationToken).ConfigureAwait(false);
        var recoverableContentStore = contentStore as IRecoverableArtifactContentStore;
        var previousContentSnapshot = recoverableContentStore is null
            ? null
            : await recoverableContentStore.CaptureAsync(artifact.Id, cancellationToken).ConfigureAwait(false);
        var metadata = await metadataStore.PublishAsync(artifact, cancellationToken).ConfigureAwait(false);

        try
        {
            var contentBinding = await contentStore.WriteAsync(artifact.Id, content, cancellationToken).ConfigureAwait(false);
            return new ArtifactBundleCommitResult(metadata, contentBinding);
        }
        catch (Exception contentException)
        {
            var recoveryExceptions = new List<Exception> { contentException };

            try
            {
                if (recoverableContentStore is not null)
                {
                    await recoverableContentStore.RestoreAsync(artifact.Id, previousContentSnapshot, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception contentRollbackException)
            {
                recoveryExceptions.Add(contentRollbackException);
            }

            try
            {
                await metadataStore.RestoreAsync(artifact.Id, previousSnapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception metadataRollbackException)
            {
                recoveryExceptions.Add(metadataRollbackException);
            }

            if (recoveryExceptions.Count > 1)
            {
                throw new InvalidOperationException(
                    "Artifact metadata/content 联合提交失败，且补偿恢复未完成。",
                    new AggregateException(recoveryExceptions));
            }

            throw;
        }
    }
}
