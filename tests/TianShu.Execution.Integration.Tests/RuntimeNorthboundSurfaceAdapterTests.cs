using Moq;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class RuntimeNorthboundSurfaceAdapterTests
{
    [Fact]
    public async Task ExecutionSurface_MapsCommandExecutionOperations()
    {
        var runtime = new Mock<IExecutionRuntime>(MockBehavior.Strict);
        runtime.Setup(static item => item.DisposeAsync()).Returns(ValueTask.CompletedTask);
        runtime
            .Setup(static item => item.StartCommandExecutionAsync(It.IsAny<ControlPlaneCommandExecutionStartCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCommandExecutionResult());
        runtime
            .Setup(static item => item.WriteCommandExecutionAsync(It.IsAny<ControlPlaneCommandExecutionWriteCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCommandExecutionCommandAcceptedResult());
        runtime
            .Setup(static item => item.TerminateCommandExecutionAsync(It.IsAny<ControlPlaneCommandExecutionTerminateCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCommandExecutionCommandAcceptedResult());
        runtime
            .Setup(static item => item.ResizeCommandExecutionAsync(It.IsAny<ControlPlaneCommandExecutionResizeCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCommandExecutionCommandAcceptedResult());

        var sut = runtime.Object.AsNorthboundSurface();

        var started = await sut.Execution.StartCommandExecutionAsync(new ControlPlaneCommandExecutionStartCommand(), CancellationToken.None);
        var wrote = await sut.Execution.WriteCommandExecutionAsync(new ControlPlaneCommandExecutionWriteCommand(), CancellationToken.None);
        var terminated = await sut.Execution.TerminateCommandExecutionAsync(new ControlPlaneCommandExecutionTerminateCommand(), CancellationToken.None);
        var resized = await sut.Execution.ResizeCommandExecutionAsync(new ControlPlaneCommandExecutionResizeCommand(), CancellationToken.None);

        Assert.NotNull(started);
        Assert.NotNull(wrote);
        Assert.NotNull(terminated);
        Assert.NotNull(resized);
    }

    [Fact]
    public async Task ExecutionAndEnvironmentSurface_MapsCodeModeAndWindowsSandboxOperations()
    {
        var runtime = new Mock<IExecutionRuntime>(MockBehavior.Strict);
        runtime.Setup(static item => item.DisposeAsync()).Returns(ValueTask.CompletedTask);
        runtime
            .Setup(static item => item.ExecuteCodeModeAsync(It.IsAny<ControlPlaneCodeModeExecCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCodeModeResult());
        runtime
            .Setup(static item => item.WaitCodeModeAsync(It.IsAny<ControlPlaneCodeModeWaitCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneCodeModeResult());
        runtime
            .Setup(static item => item.StartWindowsSandboxSetupAsync(It.IsAny<ControlPlaneWindowsSandboxSetupStartCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ControlPlaneWindowsSandboxSetupStartResult());

        var sut = runtime.Object.AsNorthboundSurface();

        var executed = await sut.Execution.ExecuteCodeModeAsync(new ControlPlaneCodeModeExecCommand(), CancellationToken.None);
        var waited = await sut.Execution.WaitCodeModeAsync(new ControlPlaneCodeModeWaitCommand(), CancellationToken.None);
        var sandbox = await sut.Environment.StartWindowsSandboxSetupAsync(new ControlPlaneWindowsSandboxSetupStartCommand(), CancellationToken.None);

        Assert.NotNull(executed);
        Assert.NotNull(waited);
        Assert.NotNull(sandbox);
    }
}
