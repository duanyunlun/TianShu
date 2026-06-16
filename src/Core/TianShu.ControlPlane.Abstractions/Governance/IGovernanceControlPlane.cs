using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Projections;

namespace TianShu.ControlPlane.Abstractions.Governance;

public interface IGovernanceControlPlane
{
    Task<ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken);

    Task<bool> ResolveApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken);

    Task<bool> ResolvePermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken);

    Task<bool> SubmitUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken);

    Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken);
}
