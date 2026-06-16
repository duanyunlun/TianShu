using TianShu.Contracts.Diagnostics;

namespace TianShu.ProjectionStores;

/// <summary>
/// 回放检查点存储，负责维护“当前可恢复位置”这一运行时状态。
/// Replay checkpoint store responsible for maintaining the runtime state of the current resumable position.
/// </summary>
public interface IReplayCheckpointStore
{
    /// <summary>
    /// 保存或覆盖一个恢复检查点。
    /// Saves or replaces a recovery checkpoint.
    /// </summary>
    Task<RecoveryCheckpoint> SaveAsync(RecoveryCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    /// 读取一个恢复检查点。
    /// Reads a recovery checkpoint.
    /// </summary>
    Task<RecoveryCheckpoint?> GetAsync(ReplayCheckpointKey key, CancellationToken cancellationToken);

    /// <summary>
    /// 删除一个恢复检查点。
    /// Removes a recovery checkpoint.
    /// </summary>
    Task RemoveAsync(ReplayCheckpointKey key, CancellationToken cancellationToken);
}
