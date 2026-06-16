using TianShu.Contracts.Projections;

namespace TianShu.ProjectionStores;

/// <summary>
/// 投影视图快照只读来源，只允许消费层读取当前物化视图。
/// Read-only projection snapshot source that only allows consumers to read current materialized views.
/// </summary>
public interface IProjectionSnapshotSource
{
    /// <summary>
    /// 读取某个作用域当前的快照。
    /// Reads the current snapshot for a scope.
    /// </summary>
    Task<ProjectionSnapshotRecord?> GetAsync(ProjectionSnapshotKey key, CancellationToken cancellationToken);
}

/// <summary>
/// 投影视图快照存储，负责维护宿主和控制平面可读的当前物化视图。
/// Projection snapshot store responsible for keeping the current materialized view readable to hosts and the control plane.
/// </summary>
public interface IProjectionSnapshotStore : IProjectionSnapshotSource
{
    /// <summary>
    /// 写入或替换某个作用域的最新投影 delta。
    /// Writes or replaces the latest projection delta for a scope.
    /// </summary>
    Task<ProjectionSnapshotRecord> UpsertAsync(ProjectionSnapshotKey key, ProjectionDelta delta, CancellationToken cancellationToken);

    /// <summary>
    /// 记录某个作用域的 reset，并清空此前缓存的 delta。
    /// Records a reset for a scope and clears the previously cached delta.
    /// </summary>
    Task<ProjectionSnapshotRecord> ResetAsync(ProjectionReset reset, CancellationToken cancellationToken);

}
