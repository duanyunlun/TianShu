using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Participants.Tests;

public sealed class ParticipantContractTests
{
    [Fact]
    public void HumanParticipant_RejectsBlankDisplayName()
    {
        Assert.Throws<ArgumentException>(() => new HumanParticipant(
            new ParticipantId("participant-001"),
            " ",
            "owner"));
    }

    [Fact]
    public void ParticipantRef_FromParticipant_CopiesIdentityFields()
    {
        var participant = new ServiceParticipant(
            new ParticipantId("participant-002"),
            "Control Plane",
            "projection-service");

        var reference = ParticipantRef.From(participant);

        Assert.Equal(participant.Id, reference.Id);
        Assert.Equal(participant.Kind, reference.Kind);
        Assert.Equal(participant.DisplayName, reference.DisplayName);
    }

    [Fact]
    public void AgentParticipant_PreservesLineage()
    {
        var lineage = new AgentLineage(new ParticipantId("participant-root"), 1);
        var participant = new AgentParticipant(
            new ParticipantId("participant-003"),
            "Contracts Worker",
            new AgentId("agent-003"),
            "implementer",
            lineage);

        Assert.Equal(new AgentId("agent-003"), participant.AgentId);
        Assert.Equal("implementer", participant.Role);
        Assert.Equal(lineage, participant.Lineage);
    }
}
