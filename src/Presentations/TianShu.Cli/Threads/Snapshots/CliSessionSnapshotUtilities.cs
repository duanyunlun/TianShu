using TianShu.Contracts.Sessions;
using TianShu.ControlPlane;
using TianShu.Execution.Runtime;

namespace TianShu.Cli;

/// <summary>
/// 统一读取 CLI 展示层所需的 session snapshot，避免直接依赖 runtime 状态指示器。
/// Centralized CLI helper for consuming session snapshots instead of runtime state indicators.
/// </summary>
internal static class CliSessionSnapshotUtilities
{
    public static Task<ControlPlaneSessionSnapshot> GetSnapshotAsync(
        IExecutionRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return TianShuControlPlaneClientFactory.Create(runtime).Sessions.GetSnapshotAsync(cancellationToken);
    }
}
