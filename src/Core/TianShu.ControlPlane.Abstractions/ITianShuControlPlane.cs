using TianShu.ControlPlane.Abstractions.Agents;
using TianShu.ControlPlane.Abstractions.Artifacts;
using TianShu.ControlPlane.Abstractions.Catalog;
using TianShu.ControlPlane.Abstractions.Collaboration;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.ControlPlane.Abstractions.Diagnostics;
using TianShu.ControlPlane.Abstractions.Governance;
using TianShu.ControlPlane.Abstractions.Identity;
using TianShu.ControlPlane.Abstractions.Memory;
using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.ControlPlane.Abstractions.Sessions;
using TianShu.ControlPlane.Abstractions.Subscriptions;
using TianShu.ControlPlane.Abstractions.Workflows;

namespace TianShu.ControlPlane.Abstractions;

public interface ITianShuControlPlane : IControlPlane
{
    Task<ControlOperationResult> IControlPlane.ProcessAsync(ControlOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(ControlOperationResult.Rejected(
            request,
            ControlOperationKind.Unspecified,
            "control.operation.not_implemented",
            "当前 Control Plane 实现尚未接入统一 operation 入口。"));
    }

    ICollaborationControlPlane Collaboration { get; }

    ISessionControlPlane Sessions { get; }

    IConversationControlPlane Conversations { get; }

    IWorkflowControlPlane Workflows { get; }

    IAgentControlPlane Agents { get; }

    IGovernanceControlPlane Governance { get; }

    ICatalogControlPlane Catalog { get; }

    IArtifactControlPlane Artifacts { get; }

    IDiagnosticsControlPlane Diagnostics { get; }

    IIdentityControlPlane Identity { get; }

    IMemoryControlPlane Memory { get; }

    IProjectionSubscriptions Subscriptions { get; }
}
