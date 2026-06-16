using TianShu.Contracts.Environment;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Environment.Tests;

public sealed class EnvironmentContractTests
{
    [Fact]
    public void HostEnvironmentProfile_RejectsBlankEnvironmentKey()
    {
        Assert.Throws<ArgumentException>(() => new HostEnvironmentProfile(
            " ",
            EnvironmentHostKind.Cli,
            "windows",
            ".NET 10"));
    }

    [Fact]
    public void CapabilitySnapshot_RequiresReasonsWhenUnavailable()
    {
        Assert.Throws<ArgumentException>(() => new CapabilitySnapshot(
            new InputCapabilities(true, false, false, true, true),
            new OutputCapabilities(true, true, false, true, true),
            new ExecutionCapabilities(true, true, true, false, true),
            new AutomationCapabilities(false, false, false, false),
            new UiCapabilities(false, false, true, false, false),
            CapabilityAvailability.Denied));
    }

    [Fact]
    public void EnvironmentBinding_PreservesMetadataAndSnapshot()
    {
        var snapshot = new CapabilitySnapshot(
            new InputCapabilities(true, true, false, true, true),
            new OutputCapabilities(true, true, true, true, true),
            new ExecutionCapabilities(true, true, true, true, true),
            new AutomationCapabilities(true, false, false, false),
            new UiCapabilities(true, true, true, true, true));
        var binding = new EnvironmentBinding(
            "env-default",
            new HostEnvironmentProfile("host-cli", EnvironmentHostKind.Cli, "windows", ".NET 10"),
            snapshot,
            "safe");

        Assert.Equal("env-default", binding.BindingKey);
        Assert.Equal(snapshot, binding.Snapshot);
        Assert.Equal("safe", binding.DefaultExecutionProfile);
    }

    [Fact]
    public void WindowsSandboxSetupCommand_PreservesTypedModeAndWorkingDirectory()
    {
        var command = new ControlPlaneWindowsSandboxSetupStartCommand
        {
            Mode = WindowsSandboxSetupMode.Elevated,
            WorkingDirectory = "D:/Work/TianShu",
        };

        Assert.Equal(WindowsSandboxSetupMode.Elevated, command.Mode);
        Assert.Equal("D:/Work/TianShu", command.WorkingDirectory);
    }

    [Fact]
    public void WindowsSandboxSetupStartResult_PreservesStartedFlag()
    {
        var result = new ControlPlaneWindowsSandboxSetupStartResult
        {
            Started = true,
        };

        Assert.True(result.Started);
    }

    [Fact]
    public void WorkspaceFact_RequiresSourceAndProjectsToContextCandidate()
    {
        var source = new WorkspaceFactSource(
            "workspace-resolver:builtin:default",
            "workspace_resolver_manifest",
            packageId: "builtin",
            resolverId: "default");
        var fact = new WorkspaceFact(
            "workspace.root",
            WorkspaceFactKind.WorkspaceRoot,
            "D:/Work/TianShu",
            source);

        var candidate = fact.ToContextSourceCandidate();

        Assert.Equal(source, fact.Source);
        Assert.Equal(ContextSourceKind.WorkspaceFact, candidate.SourceKind);
        Assert.Equal("workspace-fact://workspace.root", candidate.EvidenceRef);
        Assert.Equal("D:/Work/TianShu", candidate.Content);
    }

    [Fact]
    public void WorkspaceResolutionResult_RejectsUnspecifiedStatusAndExportsContextCandidates()
    {
        var fact = new WorkspaceFact(
            "workspace.root",
            WorkspaceFactKind.WorkspaceRoot,
            "D:/Work/TianShu",
            new WorkspaceFactSource("workspace-resolver:configured", "configured_policy"));

        var result = new WorkspaceResolutionResult(
            WorkspaceResolutionStatus.Resolved,
            facts: [fact],
            sources: [fact.Source],
            diagnosticsRefs: ["diagnostics://workspace/root"]);

        Assert.Throws<ArgumentException>(() => new WorkspaceResolutionResult(WorkspaceResolutionStatus.Unspecified));
        Assert.Equal(WorkspaceResolutionStatus.Resolved, result.Status);
        Assert.Equal("diagnostics://workspace/root", Assert.Single(result.DiagnosticsRefs));
        Assert.Equal(ContextSourceKind.WorkspaceFact, Assert.Single(result.ToContextSourceCandidates()).SourceKind);
    }

    [Fact]
    public void WorkspaceModuleInvocationContext_PreservesRuntimeSourceAndReadOnlyPolicy()
    {
        var permission = new PermissionEnvelope(["module.workspace.environment"], requiresHumanGate: false);
        var sideEffect = new SideEffectProfile(SideEffectLevel.ReadOnly);
        var context = new WorkspaceModuleInvocationContext(
            "step-workspace",
            "intent-workspace",
            "graph-workspace",
            "stage-workspace",
            "operation-workspace",
            permission,
            sideEffect);

        Assert.Equal("step-workspace", context.RuntimeStepId);
        Assert.Equal("operation-workspace", context.SourceKernelOperationId);
        Assert.Equal(permission, context.Permission);
        Assert.Equal(sideEffect, context.SideEffect);
    }
}
