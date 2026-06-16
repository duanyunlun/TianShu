using TianShu.Contracts.Diagnostics;
using TianShu.ControlPlane.Abstractions.Diagnostics;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetExecutionTraceAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListAttemptSummariesAsync(query, cancellationToken);
    }

    Task<ControlPlaneFeedbackUploadResult> IDiagnosticsControlPlane.UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ((IDiagnosticsControlPlaneClient)runtime).UploadFeedbackAsync(command, cancellationToken);
    }

    Task<ControlPlaneDebugClearMemoriesResult> IDiagnosticsControlPlane.ClearDebugMemoriesAsync(CancellationToken cancellationToken)
        => ((IDiagnosticsControlPlaneClient)runtime).ClearDebugMemoriesAsync(cancellationToken);
}
