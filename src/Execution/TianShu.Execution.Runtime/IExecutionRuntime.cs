using TianShu.Contracts.Conversations;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntime :
    IAsyncDisposable,
    ICollaborationControlPlaneClient,
    ISessionControlPlaneClient,
    IConversationControlPlaneClient,
    IWorkflowControlPlaneClient,
    IAgentControlPlaneClient,
    IGovernanceControlPlaneClient,
    ICatalogControlPlaneClient,
    IDiagnosticsControlPlaneClient,
    IIdentityControlPlaneClient,
    IMemoryControlPlaneClient,
    IArtifactControlPlaneClient
{
    Task<ExecutionRunResult> ExecuteAsync(ExecutionPlan plan, ExecutionRuntimeContext context, CancellationToken cancellationToken);

    Task<RuntimeStepResult> ExecuteStepAsync(RuntimeStep step, ExecutionRuntimeContext context, CancellationToken cancellationToken);

    Task<ControlPlaneTurnSubmissionResult> RunUserShellCommandAsync(string command, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand request, CancellationToken cancellationToken);
}
