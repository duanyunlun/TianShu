using TianShu.Contracts.Diagnostics;

namespace TianShu.ControlPlane.Abstractions.Diagnostics;

/// <summary>
/// 诊断平面 northbound 抽象。
/// Northbound abstraction for the diagnostics plane.
/// </summary>
public interface IDiagnosticsControlPlane
{
    /// <summary>
    /// 读取执行追踪。
    /// Gets an execution trace.
    /// </summary>
    Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken);

    /// <summary>
    /// 读取执行尝试摘要。
    /// Lists attempt summaries for one execution chain.
    /// </summary>
    Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken);

    /// <summary>
    /// 上传用户反馈。
    /// Uploads end-user feedback.
    /// </summary>
    Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// 清理调试记忆状态。
    /// Clears debug memory state.
    /// </summary>
    Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken);
}
