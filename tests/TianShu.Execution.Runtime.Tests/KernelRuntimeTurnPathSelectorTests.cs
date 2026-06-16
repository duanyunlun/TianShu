using TianShu.RuntimeComposition;

namespace TianShu.Execution.Runtime.Tests;

public sealed class KernelRuntimeTurnPathSelectorTests
{
    [Fact]
    public void Decide_WhenCliSendExplicitlyRequestsKernelRuntimeLoop_RoutesToNewLoop()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: true));

        Assert.Equal(KernelRuntimeTurnPathKind.KernelRuntimeLoop, decision.PathKind);
        Assert.Equal("kernel-runtime-loop", decision.ExecutionPath);
        Assert.True(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Null(decision.FallbackReason);
        Assert.Null(decision.FailureCode);
    }

    [Fact]
    public void Decide_WhenDefaultPathHasNotCutOver_FailsClosedInsteadOfFallingBackToAppHost()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: false));

        Assert.Equal(KernelRuntimeTurnPathKind.FailClosed, decision.PathKind);
        Assert.Equal("fail-closed", decision.ExecutionPath);
        Assert.False(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Null(decision.FallbackReason);
        Assert.Equal("kernel_runtime_default_disabled", decision.FailureCode);
        Assert.Empty(decision.LegacyFallbackCapabilities);
    }

    [Fact]
    public void Decide_WhenDefaultKernelRuntimeLoopEnabledAndOnlyBasicTurn_RoutesToNewLoop()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: false,
                defaultKernelRuntimeLoopEnabled: true));

        Assert.Equal(KernelRuntimeTurnPathKind.KernelRuntimeLoop, decision.PathKind);
        Assert.Equal("kernel-runtime-loop", decision.ExecutionPath);
        Assert.True(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Null(decision.FallbackReason);
        Assert.Null(decision.FailureCode);
        Assert.Equal([KernelRuntimeTurnCapability.BasicTurn], decision.RequiredCapabilities);
    }

    [Fact]
    public void Decide_WhenUnsupportedCapabilityWasPreviouslyWhitelisted_FailsClosed()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            new KernelRuntimeTurnPathRequest(
                ExplicitKernelRuntimeLoopRequested: false,
                DefaultKernelRuntimeLoopEnabled: true,
                RequiredCapabilities:
                [
                    KernelRuntimeTurnCapability.BasicTurn,
                    KernelRuntimeTurnCapability.FullProductProjection,
                ]));

        Assert.Equal(KernelRuntimeTurnPathKind.FailClosed, decision.PathKind);
        Assert.Equal("fail-closed", decision.ExecutionPath);
        Assert.Null(decision.FallbackReason);
        Assert.Equal("kernel_runtime_capability_unsupported", decision.FailureCode);
    }

    [Fact]
    public void Decide_WhenAppHostControlPlaneExplicitlyRequested_FailsClosedAsRemovedPath()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: false,
                defaultKernelRuntimeLoopEnabled: true,
                explicitAppHostControlPlaneRequested: true));

        Assert.Equal(KernelRuntimeTurnPathKind.FailClosed, decision.PathKind);
        Assert.Equal("fail-closed", decision.ExecutionPath);
        Assert.False(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Null(decision.FallbackReason);
        Assert.Equal("kernel_runtime_legacy_apphost_removed", decision.FailureCode);
    }

    [Fact]
    public void Decide_WhenBothKernelAndRemovedAppHostPathsExplicitlyRequested_FailsClosed()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            KernelRuntimeTurnPathRequest.ForCliSend(
                explicitKernelRuntimeLoopRequested: true,
                defaultKernelRuntimeLoopEnabled: true,
                explicitAppHostControlPlaneRequested: true));

        Assert.Equal(KernelRuntimeTurnPathKind.FailClosed, decision.PathKind);
        Assert.Equal("fail-closed", decision.ExecutionPath);
        Assert.False(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Equal("kernel_runtime_legacy_apphost_removed", decision.FailureCode);
    }

    [Fact]
    public void Decide_WhenUnsupportedCapabilityIsNotWhitelisted_FailsClosed()
    {
        var decision = KernelRuntimeTurnPathSelector.Decide(
            new KernelRuntimeTurnPathRequest(
                ExplicitKernelRuntimeLoopRequested: false,
                DefaultKernelRuntimeLoopEnabled: true,
                RequiredCapabilities:
                [
                    KernelRuntimeTurnCapability.BasicTurn,
                    KernelRuntimeTurnCapability.SubagentJob,
                ]));

        Assert.Equal(KernelRuntimeTurnPathKind.FailClosed, decision.PathKind);
        Assert.Equal("fail-closed", decision.ExecutionPath);
        Assert.False(decision.UseKernelRuntimeLoop);
        Assert.False(decision.UseAppHostControlPlane);
        Assert.Null(decision.FallbackReason);
        Assert.Equal("kernel_runtime_capability_unsupported", decision.FailureCode);
    }
}
