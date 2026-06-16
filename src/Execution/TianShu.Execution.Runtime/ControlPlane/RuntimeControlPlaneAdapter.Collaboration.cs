using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using CollaborationViewProjection = TianShu.Contracts.Projections.CollaborationSpaceProjection;
using ParticipantViewProjection = TianShu.Contracts.Projections.ParticipantProjection;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter
{
    public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.CreateSpaceAsync(command, cancellationToken);
    }

    public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ConfigureSpaceAsync(command, cancellationToken);
    }

    public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.ArchiveSpaceAsync(command, cancellationToken);
    }

    public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetSpaceOverviewAsync(query, cancellationToken);
    }

    public Task<CollaborationViewProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetSpaceProjectionAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListSpacesAsync(query, cancellationToken);
    }

    public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.BindParticipantToSessionAsync(command, cancellationToken);
    }

    public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.BindParticipantToWorkflowAsync(command, cancellationToken);
    }

    public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.UpdateParticipantRoleAsync(command, cancellationToken);
    }

    public Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetParticipantProjectionAsync(query, cancellationToken);
    }

    public Task<ParticipantViewProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetParticipantViewProjectionAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListParticipantsInScopeAsync(query, cancellationToken);
    }
}
