using TianShu.Contracts.Artifacts;

namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact content store recovery snapshot.
/// Artifact content store 的恢复快照。
/// </summary>
public sealed record ArtifactContentStoreSnapshot
{
    /// <summary>
    /// Initializes a new recovery snapshot.
    /// 初始化 content store 恢复快照。
    /// </summary>
    public ArtifactContentStoreSnapshot(
        ArtifactContentBinding? currentBinding,
        IReadOnlyList<ArtifactContentBinding>? historyBindings = null)
    {
        CurrentBinding = currentBinding;
        HistoryBindings = historyBindings ?? Array.Empty<ArtifactContentBinding>();
    }

    /// <summary>
    /// Current binding before the coordinated write.
    /// 协调写入前的当前绑定。
    /// </summary>
    public ArtifactContentBinding? CurrentBinding { get; }

    /// <summary>
    /// Historical bindings before the coordinated write.
    /// 协调写入前的历史版本绑定。
    /// </summary>
    public IReadOnlyList<ArtifactContentBinding> HistoryBindings { get; }
}
