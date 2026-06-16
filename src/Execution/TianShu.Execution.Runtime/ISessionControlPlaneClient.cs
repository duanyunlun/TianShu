using TianShu.Contracts.Catalog;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;

namespace TianShu.Execution.Runtime;

public interface ISessionControlPlaneClient
{
    string RuntimeName { get; }

    string? ActiveThreadId { get; }

    bool HasActiveTurn { get; }

    Task InitializeAsync(
        ControlPlaneInitializeRuntimeCommand command,
        Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler,
        CancellationToken cancellationToken);

    Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken);

    Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken);

    Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken);
}
