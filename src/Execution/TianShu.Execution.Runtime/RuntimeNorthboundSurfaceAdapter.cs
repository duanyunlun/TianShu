using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;

namespace TianShu.Execution.Runtime;

public sealed class RuntimeNorthboundSurfaceAdapter :
    IRuntimeNorthboundSurface,
    IExecutionRuntimeSurface,
    IEnvironmentRuntimeSurface
{
    private readonly IExecutionRuntime runtime;

    public RuntimeNorthboundSurfaceAdapter(IExecutionRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public IExecutionRuntimeSurface Execution => this;

    public IEnvironmentRuntimeSurface Environment => this;

    public Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(
        ControlPlaneCommandExecutionStartCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.StartCommandExecutionAsync(command, cancellationToken);
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(
        ControlPlaneCommandExecutionWriteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.WriteCommandExecutionAsync(command, cancellationToken);
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(
        ControlPlaneCommandExecutionTerminateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.TerminateCommandExecutionAsync(command, cancellationToken);
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(
        ControlPlaneCommandExecutionResizeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ResizeCommandExecutionAsync(command, cancellationToken);
    }

    public Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(
        ControlPlaneCodeModeExecCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ExecuteCodeModeAsync(command, cancellationToken);
    }

    public Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(
        ControlPlaneCodeModeWaitCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.WaitCodeModeAsync(command, cancellationToken);
    }

    public Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(
        ControlPlaneWindowsSandboxSetupStartCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.StartWindowsSandboxSetupAsync(command, cancellationToken);
    }
}
