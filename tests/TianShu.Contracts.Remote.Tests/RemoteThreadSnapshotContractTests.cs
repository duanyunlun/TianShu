using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Contracts.Remote.Tests;

public sealed class RemoteThreadSnapshotContractTests
{
    [Fact]
    public void RemoteThreadSnapshot_UsesEmptyCollectionsByDefault()
    {
        var snapshot = new RemoteThreadSnapshot(
            "snapshot-001",
            new ThreadId("thread-001"),
            new RemoteRunState(RemoteRunLifecycle.Idle));

        Assert.Equal("snapshot-001", snapshot.SnapshotId);
        Assert.Equal("thread-001", snapshot.ThreadId.Value);
        Assert.Empty(snapshot.ToolStates);
        Assert.Empty(snapshot.SubAgentStates);
        Assert.Empty(snapshot.PendingApprovals);
        Assert.Empty(snapshot.Artifacts);
        Assert.Empty(snapshot.Diagnostics.FailureCodes);
        Assert.Empty(snapshot.Evidence.AuditRefs);
        Assert.False(snapshot.Redaction.HasRedactedFields);
    }

    [Fact]
    public void RemoteThreadSnapshot_RejectsBlankSnapshotId()
    {
        Assert.Throws<ArgumentException>(() => new RemoteThreadSnapshot(
            " ",
            new ThreadId("thread-blank-snapshot"),
            new RemoteRunState(RemoteRunLifecycle.Running)));
    }

    [Fact]
    public void RemotePendingApproval_RequiresExplicitSideEffectAndHumanGate()
    {
        Assert.Throws<ArgumentException>(() => new RemotePendingApproval(
            new ApprovalId("approval-unspecified"),
            "workspace write",
            RemoteApprovalState.Pending,
            SideEffectLevel.Unspecified,
            requiresHumanGate: true));

        Assert.Throws<ArgumentException>(() => new RemotePendingApproval(
            new ApprovalId("approval-no-gate"),
            "workspace write",
            RemoteApprovalState.Pending,
            SideEffectLevel.WorkspaceWrite,
            requiresHumanGate: false));
    }

    [Fact]
    public void RemoteThreadSnapshot_PreservesThreadStatusSurface()
    {
        var snapshot = new RemoteThreadSnapshot(
            "snapshot-002",
            new ThreadId("thread-002"),
            new RemoteRunState(
                RemoteRunLifecycle.WaitingForApproval,
                activeRunRef: "active-run://thread-002/run-001",
                activeTurnId: new TurnId("turn-002"),
                activeExecutionId: new ExecutionId("execution-002")),
            currentStage: new RemoteStageState(
                "graph-002",
                "stage-tool",
                RemoteStageStatus.Blocked,
                stageKind: "tool",
                objective: "Await approval",
                diagnosticsRefs: ["diagnostics://stage-tool"]),
            toolStates:
            [
                new RemoteToolState(
                    "write",
                    "write",
                    RemoteInvocationStatus.ApprovalRequired,
                    new CallId("call-write-002"),
                    SideEffectLevel.WorkspaceWrite,
                    requiresHumanGate: true,
                    approvalRef: "approval://approval-002"),
            ],
            pendingApprovals:
            [
                new RemotePendingApproval(
                    new ApprovalId("approval-002"),
                    "Approve workspace write",
                    RemoteApprovalState.Pending,
                    SideEffectLevel.WorkspaceWrite,
                    requiresHumanGate: true,
                    decisionOptions: ["approve", "deny"],
                    riskSummary: "writes workspace file",
                    diffRef: "diff://approval-002"),
            ],
            diagnostics: new RemoteDiagnosticsSummary(
                runtimeTraceRefs: ["runtime-trace://execution-002"],
                failureCodes: ["approval_required"]),
            redaction: new RemoteSnapshotRedaction(
                hasRedactedFields: true,
                redactedKinds: ["absolute_path", "secret"],
                policyRefs: ["remote-redaction-policy://default"]));

        Assert.Equal(RemoteRunLifecycle.WaitingForApproval, snapshot.RunState.Lifecycle);
        Assert.Equal("graph-002", snapshot.CurrentStage?.GraphId);
        Assert.Equal(RemoteInvocationStatus.ApprovalRequired, Assert.Single(snapshot.ToolStates).Status);
        Assert.Equal("approve", Assert.Single(snapshot.PendingApprovals).DecisionOptions[0]);
        Assert.Equal("approval_required", Assert.Single(snapshot.Diagnostics.FailureCodes));
        Assert.Contains("secret", snapshot.Redaction.RedactedKinds);
    }

    [Fact]
    public void RemoteThreadSnapshot_SerializesIdentifierValues()
    {
        var snapshot = new RemoteThreadSnapshot(
            "snapshot-json-001",
            new ThreadId("thread-json-001"),
            new RemoteRunState(RemoteRunLifecycle.Completed),
            artifacts:
            [
                new RemoteArtifactRef(
                    new ArtifactId("artifact-json-001"),
                    "summary.md",
                    "document",
                    "promoted",
                    uriRef: "artifact://artifact-json-001"),
            ]);

        var json = JsonSerializer.Serialize(snapshot);

        Assert.Contains("snapshot-json-001", json, StringComparison.Ordinal);
        Assert.Contains("thread-json-001", json, StringComparison.Ordinal);
        Assert.Contains("artifact-json-001", json, StringComparison.Ordinal);
        Assert.DoesNotContain("D:\\", json, StringComparison.OrdinalIgnoreCase);
    }
}
