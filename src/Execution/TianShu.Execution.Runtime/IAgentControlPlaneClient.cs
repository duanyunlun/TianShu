using TianShu.Contracts.Agents;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Projections;

namespace TianShu.Execution.Runtime;

public interface IAgentControlPlaneClient
{
    Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster request, CancellationToken cancellationToken);

    Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection request, CancellationToken cancellationToken);

    Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery request, CancellationToken cancellationToken);

    Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand request, CancellationToken cancellationToken);

    Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken);
}
