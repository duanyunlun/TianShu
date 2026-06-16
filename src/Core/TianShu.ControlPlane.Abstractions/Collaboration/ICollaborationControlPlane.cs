using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Participants;
using CollaborationViewProjection = TianShu.Contracts.Projections.CollaborationSpaceProjection;
using ParticipantViewProjection = TianShu.Contracts.Projections.ParticipantProjection;

namespace TianShu.ControlPlane.Abstractions.Collaboration;

/// <summary>
/// 协作域控制平面，统一承接协作空间生命周期与 participant scope 成员关系入口。
/// Collaboration control-plane surface for collaboration-space lifecycle and participant-scope membership operations.
/// </summary>
public interface ICollaborationControlPlane
{
    Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken);

    Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken);

    Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken);

    Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken);

    Task<CollaborationViewProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken);

    Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken);

    Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken);

    Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken);

    Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken);

    Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken);

    Task<ParticipantViewProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken);

    Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken);
}
