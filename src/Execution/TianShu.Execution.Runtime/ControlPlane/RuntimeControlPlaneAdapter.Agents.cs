using TianShu.Contracts.Agents;
using TianShu.Contracts.Projections;
using TianShu.ControlPlane.Abstractions.Agents;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetAgentRosterProjectionAsync(query, cancellationToken);
    }

    public Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetTeamProjectionAsync(query, cancellationToken);
    }

    public Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListAgentsAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(
        ControlPlaneRegisterAgentThreadCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.RegisterAgentThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
