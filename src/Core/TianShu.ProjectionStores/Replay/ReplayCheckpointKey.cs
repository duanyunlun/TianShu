using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.ProjectionStores;

/// <summary>
/// 回放检查点键，用于唯一定位某个执行阶段的当前恢复点。
/// Replay checkpoint key that uniquely identifies the current recovery point for an execution stage.
/// </summary>
public readonly record struct ReplayCheckpointKey
{
    /// <summary>
    /// 初始化回放检查点键。
    /// Initializes a replay checkpoint key.
    /// </summary>
    public ReplayCheckpointKey(ExecutionId executionId, string stage)
    {
        ExecutionId = executionId;
        Stage = IdentifierGuard.AgainstNullOrWhiteSpace(stage, nameof(stage));
    }

    /// <summary>
    /// 执行标识。
    /// Execution identifier.
    /// </summary>
    public ExecutionId ExecutionId { get; }

    /// <summary>
    /// 阶段名。
    /// Stage name.
    /// </summary>
    public string Stage { get; }

    /// <summary>
    /// 从恢复检查点生成对应键。
    /// Creates the corresponding key from a recovery checkpoint.
    /// </summary>
    public static ReplayCheckpointKey From(RecoveryCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return new ReplayCheckpointKey(checkpoint.ExecutionId, checkpoint.Stage);
    }
}
