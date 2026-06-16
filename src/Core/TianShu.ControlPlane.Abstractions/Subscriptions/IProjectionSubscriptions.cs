using TianShu.Contracts.Projections;

namespace TianShu.ControlPlane.Abstractions.Subscriptions;

public interface IProjectionSubscriptions
{
    IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeThreadAsync(ControlPlaneThreadSubscription request, CancellationToken cancellationToken);

    IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeWorkflowAsync(ControlPlaneWorkflowSubscription request, CancellationToken cancellationToken);

    IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeAgentAsync(ControlPlaneAgentSubscription request, CancellationToken cancellationToken);

    IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeGovernanceAsync(ControlPlaneGovernanceSubscription request, CancellationToken cancellationToken);
}
