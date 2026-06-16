namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact 内容历史保留选项。
/// Retention options for artifact-content history snapshots.
/// </summary>
public sealed class ArtifactContentStoreRetentionOptions
{
    /// <summary>
    /// 初始化保留选项。
    /// Initializes retention options.
    /// </summary>
    public ArtifactContentStoreRetentionOptions(int? maxHistoryVersions = null)
    {
        if (maxHistoryVersions is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHistoryVersions), "保留版本数必须大于零。");
        }

        MaxHistoryVersions = maxHistoryVersions;
    }

    /// <summary>
    /// 最多保留的历史版本数量；为空表示不限制。
    /// Maximum number of history versions to keep; null means unlimited.
    /// </summary>
    public int? MaxHistoryVersions { get; }
}
