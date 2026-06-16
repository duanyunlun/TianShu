using TianShu.Contracts.Governance;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Governance.Tests;

public sealed class GovernanceContractTests
{
    [Fact]
    public void Approval_RejectsBlankTitle()
    {
        var participant = new ServiceParticipant(
            new ParticipantId("participant-approval"),
            "Reviewer",
            "approver");

        Assert.Throws<ArgumentException>(() => new Approval(
            new ApprovalId("approval-001"),
            " ",
            "需要确认",
            ParticipantRef.From(participant)));
    }

    [Fact]
    public void GovernanceCheckpoint_PreservesDecisionAndRequestedParticipant()
    {
        var participant = new ServiceParticipant(
            new ParticipantId("participant-governance"),
            "Reviewer",
            "approver");
        var decision = new PolicyDecision(PolicyDecisionKind.Escalate, "sandbox-risk", "需要人工确认");
        var checkpoint = new GovernanceCheckpoint(
            new CallId("call-governance"),
            GovernanceCheckpointKind.Approval,
            "审批",
            ParticipantRef.From(participant),
            decision);

        Assert.Equal(decision, checkpoint.Decision);
        Assert.Equal("participant-governance", checkpoint.RequestedFromParticipant.Id.Value);
    }

    [Fact]
    public void ControlPlaneApprovalResolution_PreservesDecisionAndAmendments()
    {
        var command = new ControlPlaneApprovalResolution
        {
            Envelope = new InteractionEnvelope(
                new InteractionEnvelopeId("interaction-approval-001"),
                new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                [new StructuredInteractionItem("approval_response", StructuredValue.FromString("approve"))]),
            CallId = new CallId("call-approval-001"),
            Decision = ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
            Note = "allow git status",
            CommandPrefix = ["git", "status"],
            NetworkHost = "api.openai.com",
            NetworkAction = "allow",
        };

        Assert.Equal("call-approval-001", command.CallId.Value);
        Assert.Equal("interaction-approval-001", command.Envelope?.Id.Value);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment, command.Decision);
        Assert.Equal(["git", "status"], command.CommandPrefix);
        Assert.Equal("api.openai.com", command.NetworkHost);
        Assert.Equal("allow", command.NetworkAction);
    }

    [Fact]
    public void ControlPlanePermissionGrant_AndUserInputSubmission_PreserveStructuredPayloads()
    {
        var permission = new ControlPlanePermissionGrant
        {
            Envelope = new InteractionEnvelope(
                new InteractionEnvelopeId("interaction-permission-001"),
                new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                [new StructuredInteractionItem("permission_response", StructuredValue.FromString("session"))]),
            CallId = new CallId("call-permission-001"),
            Permissions = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["network.http"] = StructuredValue.FromBoolean(true),
            },
            Scope = ControlPlanePermissionScope.Session,
        };
        var userInput = new ControlPlaneUserInputSubmission
        {
            Envelope = new InteractionEnvelope(
                new InteractionEnvelopeId("interaction-input-001"),
                new InteractionSource(InteractionSourceKind.Host, "sidecar"),
                [new StructuredInteractionItem("user_input_submission", StructuredValue.FromString("A"))]),
            CallId = new CallId("call-input-001"),
            Answers = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["choice"] = StructuredValue.FromString("A"),
            },
        };

        Assert.Equal("call-permission-001", permission.CallId.Value);
        Assert.Equal("interaction-permission-001", permission.Envelope?.Id.Value);
        Assert.Equal(ControlPlanePermissionScope.Session, permission.Scope);
        Assert.True(permission.Permissions["network.http"].BooleanValue);
        Assert.Equal("call-input-001", userInput.CallId.Value);
        Assert.Equal("interaction-input-001", userInput.Envelope?.Id.Value);
        Assert.Equal("A", userInput.Answers["choice"].StringValue);
    }
}
