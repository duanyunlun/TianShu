using TianShu.Execution.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class ApprovalResponseTests
{
    [Fact]
    public void NetworkPolicyAmendmentDecision_WhenPayloadMissing_ShouldDegradeToDecline()
    {
        var response = new ApprovalResponse(ApprovalResponseDecision.ApplyNetworkPolicyAmendment, "missing payload");

        Assert.False(response.IsApproved);
        Assert.Equal("decline", response.ToProtocolDecisionToken());
        Assert.Equal("decline", response.ToProtocolDecisionPayload());
    }

    [Fact]
    public void ExecPolicyAmendmentDecision_WhenPayloadMissing_ShouldDegradeToDecline()
    {
        var response = new ApprovalResponse(ApprovalResponseDecision.AcceptWithExecPolicyAmendment, "missing payload");

        Assert.False(response.IsApproved);
        Assert.Equal("decline", response.ToProtocolDecisionToken());
        Assert.Equal("decline", response.ToProtocolDecisionPayload());
    }

    [Fact]
    public void NetworkPolicyAmendmentDecision_WhenActionInvalid_ShouldDegradeToDecline()
    {
        var response = ApprovalResponse.ApplyNetworkPolicyAmendment(
            new NetworkPolicyAmendmentPayload("example.com", "foobar"),
            "invalid action");

        Assert.False(response.IsApproved);
        Assert.Equal("decline", response.ToProtocolDecisionToken());
        Assert.Equal("decline", response.ToProtocolDecisionPayload());
    }
}
