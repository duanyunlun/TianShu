using TianShu.Contracts.Primitives;

namespace TianShu.Kernel.Tests;

public sealed class SessionObservedStateFactoryTests
{
    [Fact]
    public void Create_WhenWorkspacePolicyArtifactsAndWarningsExist_ProjectsObservedSegments()
    {
        var observedState = SessionObservedStateFactory.Create(
            workspaceCwd: " C:/repo ",
            workspaceSandboxMode: " workspace-write ",
            workspaceWebSearchMode: null,
            workspaceWindowsSandboxLevel: "Unrestricted",
            allowLoginShell: true,
            artifactRefs:
            [
                new ArtifactRef(new ArtifactId("artifact-1"), "Draft", "markdown"),
                new ArtifactRef(new ArtifactId("artifact-1"), "Duplicate", "markdown"),
            ],
            stageRegistryIssues:
            [
                new RuntimeStageRegistryIssue("warn-stage", "Stage is degraded", "review", RuntimeStageRegistryIssueSeverity.Warning),
                new RuntimeStageRegistryIssue("error-stage", "Stage is invalid"),
            ],
            memoryMode: " scoped ",
            approvalPolicy: "Never",
            policySandboxMode: "workspace-write",
            policyWebSearchMode: "enabled",
            defaultModeRequestUserInputEnabled: false);

        var workspaceSegment = Assert.Single(observedState.WorkspaceStateSegments);
        Assert.Equal("workspace_state", workspaceSegment.Kind);
        Assert.Contains("cwd=C:/repo", workspaceSegment.Content, StringComparison.Ordinal);
        Assert.Contains("allow_login_shell=True", workspaceSegment.Content, StringComparison.Ordinal);

        var artifactSegment = Assert.Single(observedState.ArtifactStateSegments);
        Assert.Equal("artifact_state", artifactSegment.Kind);
        Assert.Contains("id=artifact-1, name=Draft, kind=markdown", artifactSegment.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate", artifactSegment.Content, StringComparison.Ordinal);

        var diagnosticSegment = Assert.Single(observedState.DiagnosticStateSegments);
        Assert.Equal("diagnostic_state", diagnosticSegment.Kind);
        Assert.Contains("code=warn-stage", diagnosticSegment.Content, StringComparison.Ordinal);

        var memorySegment = Assert.Single(observedState.MemoryStateSegments);
        Assert.Equal("memory_mode=scoped", memorySegment.Content);

        var policySegment = Assert.Single(observedState.PolicyStateSegments);
        Assert.Contains("approval_policy=Never", policySegment.Content, StringComparison.Ordinal);
        Assert.Contains("default_mode_request_user_input=False", policySegment.Content, StringComparison.Ordinal);
        Assert.Equal(["runtime-policy-context"], observedState.PolicyHits);
    }

    [Fact]
    public void Create_WhenOnlyDefaultPolicyFlagExists_ProjectsPolicyWithoutWorkspace()
    {
        var observedState = SessionObservedStateFactory.Create(
            workspaceCwd: null,
            workspaceSandboxMode: null,
            workspaceWebSearchMode: null,
            workspaceWindowsSandboxLevel: "Unrestricted",
            allowLoginShell: false,
            artifactRefs: null,
            stageRegistryIssues: null,
            memoryMode: null,
            approvalPolicy: null,
            policySandboxMode: null,
            policyWebSearchMode: null,
            defaultModeRequestUserInputEnabled: true);

        Assert.Empty(observedState.WorkspaceStateSegments);
        var policySegment = Assert.Single(observedState.PolicyStateSegments);
        Assert.Equal("default_mode_request_user_input=True", policySegment.Content);
        Assert.Equal(["runtime-policy-context"], observedState.PolicyHits);
    }
}
