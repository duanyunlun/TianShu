using TianShu.Contracts.Projections;

namespace TianShu.ProjectionStores;

/// <summary>
/// 投影视图快照记录，表示某个作用域当前最后一次已物化的 delta 或 reset。
/// Projection snapshot record that represents the latest materialized delta or reset for a scope.
/// </summary>
public sealed record ProjectionSnapshotRecord
{
    /// <summary>
    /// 初始化投影视图快照记录。
    /// Initializes a projection snapshot record.
    /// </summary>
    public ProjectionSnapshotRecord(
        ProjectionSnapshotKey key,
        ProjectionDelta? delta = null,
        ProjectionReset? reset = null,
        long version = 1,
        DateTimeOffset? updatedAt = null)
    {
        if ((delta is null) == (reset is null))
        {
            throw new ArgumentException("Projection snapshot record must contain exactly one of delta or reset.");
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "版本号必须大于零。");
        }

        Key = key;
        Delta = delta;
        Reset = reset;
        Version = version;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 快照主键。
    /// Snapshot key.
    /// </summary>
    public ProjectionSnapshotKey Key { get; }

    /// <summary>
    /// 当前最新 delta；若最近一次事件是 reset，则为空。
    /// Latest delta; null when the most recent materialized event is a reset.
    /// </summary>
    public ProjectionDelta? Delta { get; }

    /// <summary>
    /// 当前最新 reset；若最近一次事件是 delta，则为空。
    /// Latest reset; null when the most recent materialized event is a delta.
    /// </summary>
    public ProjectionReset? Reset { get; }

    /// <summary>
    /// 快照版本号，每次写入或重置时递增。
    /// Snapshot version that increments on each write or reset.
    /// </summary>
    public long Version { get; }

    /// <summary>
    /// 最后更新时间。
    /// Last update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// 基于新 delta 生成下一版本快照。
    /// Creates the next snapshot version from a new delta.
    /// </summary>
    public ProjectionSnapshotRecord WithDelta(ProjectionDelta delta)
        => new(Key, delta: delta, version: Version + 1, updatedAt: DateTimeOffset.UtcNow);

    /// <summary>
    /// 基于新 reset 生成下一版本快照。
    /// Creates the next snapshot version from a new reset.
    /// </summary>
    public ProjectionSnapshotRecord WithReset(ProjectionReset reset)
        => new(Key, reset: reset, version: Version + 1, updatedAt: DateTimeOffset.UtcNow);
}
