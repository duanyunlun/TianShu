using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Projections;

namespace TianShu.Execution.Runtime;

public interface IGovernanceControlPlaneClient
{
    Task<ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken);

    Task<bool> RespondToApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken);

    Task<bool> RespondToPermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken);

    Task<bool> RespondToUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken);

    Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken);
}
