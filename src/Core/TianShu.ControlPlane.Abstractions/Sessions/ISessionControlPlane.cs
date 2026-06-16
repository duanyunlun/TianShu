using TianShu.Contracts.Sessions;

namespace TianShu.ControlPlane.Abstractions.Sessions;

public interface ISessionControlPlane
{
    Task<ControlPlaneSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);

    Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken);
}
