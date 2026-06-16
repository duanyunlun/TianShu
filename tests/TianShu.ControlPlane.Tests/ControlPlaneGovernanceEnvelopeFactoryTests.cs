using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using Xunit;

namespace TianShu.ControlPlane.Tests;

public sealed class ControlPlaneGovernanceEnvelopeFactoryTests
{
    [Fact]
    public void Create_ShouldNormalizeGovernanceEnvelopeAtControlPlaneBoundary()
    {
        var envelope = ControlPlaneGovernanceEnvelopeFactory.Create(new ControlPlaneGovernanceEnvelopeRequest(
            "governance-control-001",
            policyIds: [" policy.runtime ", "policy.runtime"],
            allowedToolIds: ["tool.read", " tool.read ", "tool.write"],
            allowedModuleIds: ["provider.openai", "provider.openai"],
            maxSideEffectLevel: SideEffectLevel.ExternalNetwork,
            requiresHumanGate: true,
            approvalIds: [new ApprovalId("approval-control-001")]));

        Assert.Equal("governance-control-001", envelope.EnvelopeId);
        Assert.Equal(["policy.runtime"], envelope.PolicyIds);
        Assert.Equal(["tool.read", "tool.write"], envelope.AllowedToolIds);
        Assert.Equal(["provider.openai"], envelope.AllowedModuleIds);
        Assert.Equal(SideEffectLevel.ExternalNetwork, envelope.MaxSideEffectLevel);
        Assert.True(envelope.RequiresHumanGate);
        Assert.Equal(new ApprovalId("approval-control-001"), Assert.Single(envelope.ApprovalIds));
    }
}
