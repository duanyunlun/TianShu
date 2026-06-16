using TianShu.Contracts.Artifacts;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact metadata 与 content 的联合提交边界。
/// Coordinated commit boundary for artifact metadata and content.
/// </summary>
public interface IArtifactBundleStore
{
    /// <summary>
    /// 一次提交同时更新 artifact metadata 当前态与 content 当前绑定。
    /// Updates the current artifact metadata and current content binding in a single coordinated commit.
    /// </summary>
    Task<ArtifactBundleCommitResult> PublishWithContentAsync(
        Artifact artifact,
        ArtifactContent content,
        CancellationToken cancellationToken);
}
