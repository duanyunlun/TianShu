using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Collaboration.Tests;

public sealed class CollaborationContractTests
{
    [Fact]
    public void CollaborationSpace_RejectsBlankDisplayName()
    {
        Assert.Throws<ArgumentException>(() => new CollaborationSpace(
            new CollaborationSpaceId("space-001"),
            "design-space",
            " ",
            new CollaborationSpaceProfile("架构讨论"),
            CollaborationDefaultSet.Empty));
    }

    [Fact]
    public void CollaborationSpaceRef_FromSpace_CopiesIdentityFields()
    {
        var space = new CollaborationSpace(
            new CollaborationSpaceId("space-002"),
            "contracts-first",
            "Contracts First",
            new CollaborationSpaceProfile("独立 Contracts 体系"),
            new CollaborationDefaultSet("workspace-default", "safe"),
            new CollaborationPolicyRef("policy-default"));

        var reference = CollaborationSpaceRef.From(space);

        Assert.Equal(space.Id, reference.Id);
        Assert.Equal(space.Key, reference.Key);
        Assert.Equal(space.DisplayName, reference.DisplayName);
    }
}
