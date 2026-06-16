using TianShu.Contracts.Artifacts;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact metadata/content 联合提交结果。
/// Coordinated artifact metadata/content commit result.
/// </summary>
public sealed record ArtifactBundleCommitResult
{
    /// <summary>
    /// 初始化联合提交结果。
    /// Initializes the coordinated commit result.
    /// </summary>
    public ArtifactBundleCommitResult(ArtifactStoreRecord metadata, ArtifactContentBinding content)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// 提交后的 metadata 当前态。
    /// Current metadata state after the commit.
    /// </summary>
    public ArtifactStoreRecord Metadata { get; }

    /// <summary>
    /// 提交后的 content 当前绑定。
    /// Current content binding after the commit.
    /// </summary>
    public ArtifactContentBinding Content { get; }
}
