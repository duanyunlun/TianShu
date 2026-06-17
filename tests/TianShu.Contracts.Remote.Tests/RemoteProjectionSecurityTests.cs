using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Contracts.Remote.Tests;

public sealed class RemoteProjectionSecurityTests
{
    [Fact]
    public void ProjectSnapshot_RedactsLocalPathsSecretsAndKeepsHighRiskHumanGate()
    {
        var snapshot = new RemoteThreadSnapshot(
            "snapshot-security-001",
            new ThreadId("thread-security-001"),
            new RemoteRunState(
                RemoteRunLifecycle.WaitingForApproval,
                activeRunRef: @"C:\Users\SEMI\repo\.tianshu\runtime\run.json"),
            toolStates:
            [
                new RemoteToolState(
                    "write",
                    "write",
                    RemoteInvocationStatus.ApprovalRequired,
                    sideEffectLevel: SideEffectLevel.WorkspaceWrite,
                    requiresHumanGate: false,
                    resultRef: @"D:\GitRepos\Personal\TianShu\src\secret.txt"),
            ],
            pendingApprovals:
            [
                new RemotePendingApproval(
                    new ApprovalId("approval-security-001"),
                    "Approve workspace write",
                    RemoteApprovalState.Pending,
                    SideEffectLevel.WorkspaceWrite,
                    requiresHumanGate: true,
                    riskSummary: "authorization: Bearer test-token"),
            ],
            artifacts:
            [
                new RemoteArtifactRef(
                    new ArtifactId("artifact-security-001"),
                    "summary.md",
                    "document",
                    "available",
                    uriRef: "file:///D:/GitRepos/Personal/TianShu/summary.md",
                    summary: "api_key=should-not-leak"),
            ]);

        var projected = RemoteProjectionSecurityProjector.ProjectSnapshot(snapshot);

        Assert.Equal("[redacted:absolute_path]", projected.RunState.ActiveRunRef);
        Assert.True(Assert.Single(projected.ToolStates).RequiresHumanGate);
        Assert.Equal("[redacted:absolute_path]", Assert.Single(projected.ToolStates).ResultRef);
        Assert.Equal("[redacted:secret]", Assert.Single(projected.PendingApprovals).RiskSummary);
        Assert.Equal("[redacted:absolute_path]", Assert.Single(projected.Artifacts).UriRef);
        Assert.Equal("[redacted:secret]", Assert.Single(projected.Artifacts).Summary);
        Assert.True(projected.Redaction.HasRedactedFields);
        Assert.Contains("absolute_path", projected.Redaction.RedactedKinds);
        Assert.Contains("secret", projected.Redaction.RedactedKinds);
        Assert.Contains("remote-redaction-policy://default", projected.Redaction.PolicyRefs);
    }

    [Fact]
    public void ProjectEvent_RedactsStructuredPayloadWorkspaceContentSecretAndPath()
    {
        var @event = new RemoteContinuityEvent(
            "event-security-001",
            new ThreadId("thread-security-001"),
            new RemoteEventCursor("cursor-security-001"),
            RemoteContinuityEventKind.ArtifactChanged,
            new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero),
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["file_content"] = StructuredValue.FromString("workspace file body"),
                ["apiKey"] = StructuredValue.FromString("sk-test-value"),
                ["path"] = StructuredValue.FromString(@"C:\Users\SEMI\repo\file.txt"),
                ["safe"] = StructuredValue.FromString("artifact://safe-ref"),
            }),
            new RemoteEventVisibility(visibleScopes: ["remote.thread.read"]));

        var projected = RemoteProjectionSecurityProjector.ProjectEvent(@event);

        Assert.NotNull(projected.Payload);
        Assert.Equal("[redacted:workspace_file_content]", projected.Payload!.GetProperty("file_content").GetString());
        Assert.Equal("[redacted:secret]", projected.Payload.GetProperty("apiKey").GetString());
        Assert.Equal("[redacted:absolute_path]", projected.Payload.GetProperty("path").GetString());
        Assert.Equal("artifact://safe-ref", projected.Payload.GetProperty("safe").GetString());
        Assert.True(projected.Visibility.Redacted);
        Assert.Contains("workspace_file_content", projected.Visibility.RedactedKinds);
        Assert.Contains("secret", projected.Visibility.RedactedKinds);
        Assert.Contains("absolute_path", projected.Visibility.RedactedKinds);
        Assert.Equal("remote-redaction-policy://default", projected.Visibility.PolicyRef);
    }
}
