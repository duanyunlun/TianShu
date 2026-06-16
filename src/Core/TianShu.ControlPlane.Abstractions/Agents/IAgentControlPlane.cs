using TianShu.Contracts.Agents;
using TianShu.Contracts.Projections;

namespace TianShu.ControlPlane.Abstractions.Agents;

public interface IAgentControlPlane
{
    Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster query, CancellationToken cancellationToken);

    Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection query, CancellationToken cancellationToken);

    Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand command, CancellationToken cancellationToken);
}
