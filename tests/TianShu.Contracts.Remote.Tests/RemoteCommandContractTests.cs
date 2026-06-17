using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Contracts.Remote.Tests;

public sealed class RemoteCommandContractTests
{
    [Fact]
    public void RemoteCommandScope_RejectsUnspecifiedSideEffect()
    {
        Assert.Throws<ArgumentException>(() => new RemoteCommandScope(
            [RemoteCommandKind.SubmitMessage],
            SideEffectLevel.Unspecified));
    }

    [Fact]
    public void RemoteCommandEnvelope_RejectsCommandOutsideScope()
    {
        var scope = new RemoteCommandScope(
            [RemoteCommandKind.SubmitMessage],
            SideEffectLevel.HostMutation);

        Assert.Throws<ArgumentException>(() => new RemoteCommandEnvelope<RemoteInterruptPayload>(
            "command-scope-denied",
            new ThreadId("thread-command-001"),
            new DeviceId("device-command-001"),
            new SessionId("session-command-001"),
            new RemoteInterruptPayload("stop"),
            scope,
            new RemoteCommandIdempotencyKey("idem-command-001"),
            new RemoteAuditContext("pairing://device-command-001", "remote-user://alice")));
    }

    [Fact]
    public void RemoteCommandEnvelope_PreservesSubmitMessagePayloadAndAudit()
    {
        var scope = new RemoteCommandScope(
            [RemoteCommandKind.SubmitMessage, RemoteCommandKind.ApprovalDecision],
            SideEffectLevel.HostMutation,
            threadRefs: ["thread-command-002"],
            scopeRefs: ["remote.thread.submit"]);
        var envelope = new RemoteCommandEnvelope<RemoteSubmitMessagePayload>(
            "command-submit-002",
            new ThreadId("thread-command-002"),
            new DeviceId("device-command-002"),
            new SessionId("session-command-002"),
            new RemoteSubmitMessagePayload("继续执行", ["artifact://context-002"]),
            scope,
            new RemoteCommandIdempotencyKey("idem-command-002"),
            new RemoteAuditContext(
                "pairing://device-command-002",
                "remote-user://alice",
                networkRef: "network://loopback",
                auditRefs: ["audit://command-submit-002"]));

        Assert.Equal(RemoteCommandKind.SubmitMessage, envelope.Kind);
        Assert.Equal("继续执行", envelope.Payload.MessageText);
        Assert.Equal("artifact://context-002", Assert.Single(envelope.Payload.AttachmentRefs));
        Assert.Equal("audit://command-submit-002", Assert.Single(envelope.Audit.AuditRefs));
        Assert.True(envelope.Scope.Allows(RemoteCommandKind.ApprovalDecision));
    }

    [Fact]
    public void RemoteCommandScope_RequiresSideEffectCeilingForHighRiskCommands()
    {
        var readOnlyApprovalScope = new RemoteCommandScope(
            [RemoteCommandKind.ApprovalDecision],
            SideEffectLevel.ReadOnly);
        var hostMutationApprovalScope = new RemoteCommandScope(
            [RemoteCommandKind.ApprovalDecision],
            SideEffectLevel.HostMutation);
        var readOnlySubmitScope = new RemoteCommandScope(
            [RemoteCommandKind.SubmitMessage],
            SideEffectLevel.ReadOnly);

        Assert.False(readOnlyApprovalScope.AllowsSideEffectFor(RemoteCommandKind.ApprovalDecision));
        Assert.True(hostMutationApprovalScope.AllowsSideEffectFor(RemoteCommandKind.ApprovalDecision));
        Assert.True(readOnlySubmitScope.AllowsSideEffectFor(RemoteCommandKind.SubmitMessage));
        Assert.Equal(SideEffectLevel.HostMutation, RemoteCommandScope.GetRequiredSideEffectLevel(RemoteCommandKind.Resume));
    }

    [Fact]
    public void RemoteApprovalDecisionPayload_RejectsUnspecifiedDecision()
    {
        Assert.Throws<ArgumentException>(() => new RemoteApprovalDecisionPayload(
            new ApprovalId("approval-command-001"),
            RemoteApprovalDecisionKind.Unspecified));
    }

    [Fact]
    public void RemoteCommandPayloads_RejectBlankRequiredFields()
    {
        Assert.Throws<ArgumentException>(() => new RemoteSubmitMessagePayload(" "));
        Assert.Throws<ArgumentException>(() => new RemoteSteerPayload(" "));
        Assert.Throws<ArgumentException>(() => new RemoteInterruptPayload(" "));
        Assert.Throws<ArgumentException>(() => new RemoteResumePayload(" "));
        Assert.Throws<ArgumentException>(() => new RemoteCancelPendingOperationPayload(" "));
    }

    [Fact]
    public void RemoteCommandResult_PreservesIdempotencyAndFailure()
    {
        var result = new RemoteCommandResult(
            "command-result-001",
            RemoteCommandKind.CancelPendingOperation,
            RemoteCommandAdmissionStatus.ScopeDenied,
            new RemoteCommandIdempotencyKey("idem-result-001"),
            failureCode: "remote_scope_denied",
            diagnosticsRef: "diagnostics://remote-command-result-001");

        Assert.Equal(RemoteCommandAdmissionStatus.ScopeDenied, result.Status);
        Assert.Equal("idem-result-001", result.IdempotencyKey.Value);
        Assert.Equal("remote_scope_denied", result.FailureCode);
    }

    [Fact]
    public void RemoteCommandIdempotencyKey_SerializesAsIdentifierValue()
    {
        var json = JsonSerializer.Serialize(new RemoteCommandIdempotencyKey("idem-json-001"));

        Assert.Contains("idem-json-001", json, StringComparison.Ordinal);
    }
}
