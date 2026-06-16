namespace TianShu.ArtifactStore;

/// <summary>
/// Artifact 内容存储的同步选项。
/// Synchronization options for artifact-content storage.
/// </summary>
public sealed class ArtifactContentStoreSyncOptions
{
    /// <summary>
    /// 初始化同步选项。
    /// Initializes synchronization options.
    /// </summary>
    public ArtifactContentStoreSyncOptions(bool enableCrossProcessSync = false)
    {
        EnableCrossProcessSync = enableCrossProcessSync;
    }

    /// <summary>
    /// 是否启用单机多实例级别的跨进程同步。
    /// Indicates whether single-machine multi-instance cross-process synchronization is enabled.
    /// </summary>
    public bool EnableCrossProcessSync { get; }
}
