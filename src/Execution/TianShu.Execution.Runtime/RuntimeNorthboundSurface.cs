using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;

namespace TianShu.Execution.Runtime;

public interface IExecutionRuntimeSurface
{
    Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand command, CancellationToken cancellationToken);
}

public interface IEnvironmentRuntimeSurface
{
    Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand command, CancellationToken cancellationToken);
}

public interface IRuntimeNorthboundSurface
{
    IExecutionRuntimeSurface Execution { get; }

    IEnvironmentRuntimeSurface Environment { get; }
}
