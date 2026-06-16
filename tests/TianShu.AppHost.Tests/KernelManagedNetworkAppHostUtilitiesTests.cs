using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelManagedNetworkAppHostUtilitiesTests
{
    [Fact]
    public void ExtractApprovalDecision_ShouldReadNestedDecisionType()
    {
        var response = JsonSerializer.SerializeToElement(new
        {
            decision = new
            {
                type = "acceptWithExecpolicyAmendment",
            },
        });

        var decision = KernelManagedNetworkAppHostUtilities.ExtractApprovalDecision(response);

        Assert.Equal("acceptWithExecpolicyAmendment", decision);
    }

    [Fact]
    public void TryReadNetworkPolicyAmendment_ShouldParseDecisionNestedPayload()
    {
        var response = JsonSerializer.SerializeToElement(new
        {
            decision = new
            {
                applyNetworkPolicyAmendment = new
                {
                    network_policy_amendment = new
                    {
                        host = "example.com",
                        action = "deny",
                    },
                },
            },
        });

        var parsed = KernelManagedNetworkAppHostUtilities.TryReadNetworkPolicyAmendment(response, out var amendment);

        Assert.True(parsed);
        Assert.NotNull(amendment);
        Assert.Equal("example.com", amendment!.Host);
        Assert.Equal(KernelManagedNetworkRuleAction.Deny, amendment.Action);
    }

    [Fact]
    public void IsSandboxPolicyNetworkEnabled_ShouldAcceptStringEnabled()
    {
        var policy = JsonSerializer.SerializeToElement(new
        {
            networkAccess = "enabled",
        });

        Assert.True(KernelManagedNetworkAppHostUtilities.IsSandboxPolicyNetworkEnabled(policy));
    }
}
