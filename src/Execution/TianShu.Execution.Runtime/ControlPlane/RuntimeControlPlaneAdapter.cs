using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Governance;
using ControlPlaneCreateJobCommand = TianShu.Contracts.Workflows.ControlPlaneCreateJobCommand;
using ControlPlaneDispatchJobCommand = TianShu.Contracts.Workflows.ControlPlaneDispatchJobCommand;
using ControlPlaneJobListResult = TianShu.Contracts.Workflows.ControlPlaneJobListResult;
using ControlPlaneJobOperationResult = TianShu.Contracts.Workflows.ControlPlaneJobOperationResult;
using ControlPlaneListJobsQuery = TianShu.Contracts.Workflows.ControlPlaneListJobsQuery;
using CreateTask = TianShu.Contracts.Workflows.CreateTask;
using CreateWorkflow = TianShu.Contracts.Workflows.CreateWorkflow;
using PlanProjection = TianShu.Contracts.Workflows.PlanProjection;
using PublishPlan = TianShu.Contracts.Workflows.PublishPlan;
using ControlPlaneReportJobItemCommand = TianShu.Contracts.Workflows.ControlPlaneReportJobItemCommand;
using ControlPlaneReadJobQuery = TianShu.Contracts.Workflows.ControlPlaneReadJobQuery;
using ControlPlaneReviewStartCommand = TianShu.Contracts.Workflows.ControlPlaneReviewStartCommand;
using ControlPlaneReviewStartResult = TianShu.Contracts.Workflows.ControlPlaneReviewStartResult;
using GetPlanProjection = TianShu.Contracts.Workflows.GetPlanProjection;
using GetTaskBoard = TianShu.Contracts.Workflows.GetTaskBoard;
using GetWorkflowBoard = TianShu.Contracts.Workflows.GetWorkflowBoard;
using UpdateTaskState = TianShu.Contracts.Workflows.UpdateTaskState;
using Workflow = TianShu.Contracts.Workflows.Workflow;
using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Agents;
using TianShu.ControlPlane.Abstractions.Artifacts;
using TianShu.ControlPlane.Abstractions.Catalog;
using TianShu.ControlPlane.Abstractions.Collaboration;
using TianShu.ControlPlane.Abstractions.Conversations;
using TianShu.ControlPlane.Abstractions.Diagnostics;
using TianShu.ControlPlane.Abstractions.Governance;
using TianShu.ControlPlane.Abstractions.Identity;
using TianShu.ControlPlane.Abstractions.Memory;
using TianShu.ControlPlane.Abstractions.Sessions;
using TianShu.ControlPlane.Abstractions.Subscriptions;
using TianShu.ControlPlane.Abstractions.Workflows;
using ThreadViewProjection = TianShu.Contracts.Projections.ThreadProjection;

namespace TianShu.Execution.Runtime.ControlPlane;

public sealed partial class RuntimeControlPlaneAdapter :
    ITianShuControlPlane,
    ICollaborationControlPlane,
    ISessionControlPlane,
    IConversationControlPlane,
    IWorkflowControlPlane,
    IAgentControlPlane,
    IGovernanceControlPlane,
    ICatalogControlPlane,
    IArtifactControlPlane,
    IDiagnosticsControlPlane,
    IIdentityControlPlane,
    IMemoryControlPlane,
    IProjectionSubscriptions
{
    private readonly IExecutionRuntime runtime;

    public RuntimeControlPlaneAdapter(IExecutionRuntime runtime)
    {
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public ICollaborationControlPlane Collaboration => this;

    public ISessionControlPlane Sessions => this;

    public IConversationControlPlane Conversations => this;

    public IWorkflowControlPlane Workflows => this;

    public IAgentControlPlane Agents => this;

    public IGovernanceControlPlane Governance => this;

    public ICatalogControlPlane Catalog => this;

    public IArtifactControlPlane Artifacts => this;

    public IDiagnosticsControlPlane Diagnostics => this;

    public IIdentityControlPlane Identity => this;

    public IMemoryControlPlane Memory => this;

    public IProjectionSubscriptions Subscriptions => this;

    public Task<ControlPlaneSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ControlPlaneSessionSnapshot
        {
            RuntimeName = runtime.RuntimeName,
            ActiveThreadId = ToOptionalThreadId(runtime.ActiveThreadId),
            HasActiveTurn = runtime.HasActiveTurn,
        });
    }

    public Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetSessionOverviewAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListSessionsAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.SendAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.SendFollowUpAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.MutatePendingFollowUpAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public Task InterruptTurnAsync(CancellationToken cancellationToken)
        => runtime.InterruptTurnAsync(cancellationToken);

    public async Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.ListThreadsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public Task<ThreadViewProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetThreadProjectionAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneThreadSummary?> StartThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StartNewThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.ForkThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.ResumeThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.ListLoadedThreadsAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.ReadThreadAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RenameThreadAsync(ControlPlaneRenameThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.RenameThreadAsync(command.ThreadId.Value, command.Name, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ArchiveThreadAsync(ControlPlaneArchiveThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.ArchiveThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteThreadAsync(ControlPlaneDeleteThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.DeleteThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(ControlPlaneClearThreadsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.ClearThreadsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.CompactThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.CleanBackgroundTerminalsAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.UnsubscribeThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.IncrementThreadElicitationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.DecrementThreadElicitationAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.UnarchiveThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.UpdateThreadMetadataAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.RollbackThreadAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.SearchFuzzyFilesAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StartFuzzyFileSearchSessionAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.UpdateFuzzyFileSearchSessionAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StopFuzzyFileSearchSessionAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StartRealtimeAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.AppendRealtimeTextAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.AppendRealtimeAudioAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.HandoffRealtimeOutputAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StopRealtimeAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ResolveApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RespondToApprovalAsync(command, cancellationToken);
    }

    public Task<ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetApprovalQueueProjectionAsync(query, cancellationToken);
    }

    public Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.ListUserInputRequestsAsync(query, cancellationToken);
    }

    public Task<bool> ResolvePermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RespondToPermissionRequestAsync(command, cancellationToken);
    }

    public Task<bool> SubmitUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.RespondToUserInputAsync(command, cancellationToken);
    }

    public Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ((IGovernanceControlPlaneClient)runtime).UploadFeedbackAsync(command, cancellationToken);
    }

    public async Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.StartReviewAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public Task<Workflow> CreateWorkflowAsync(CreateWorkflow command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.CreateWorkflowAsync(command, cancellationToken);
    }

    public Task<PlanProjection> PublishPlanAsync(PublishPlan command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.PublishPlanAsync(command, cancellationToken);
    }

    public Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.CreateTaskAsync(command, cancellationToken);
    }

    public Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return runtime.UpdateTaskStateAsync(command, cancellationToken);
    }

    public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetWorkflowBoardProjectionAsync(query, cancellationToken);
    }

    public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetTaskBoardProjectionAsync(query, cancellationToken);
    }

    public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return runtime.GetPlanProjectionAsync(query, cancellationToken);
    }

    public async Task<ControlPlaneJobOperationResult> CreateJobAsync(ControlPlaneCreateJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.CreateAgentJobAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneJobOperationResult> DispatchJobAsync(ControlPlaneDispatchJobCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.DispatchAgentJobAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneJobOperationResult> ReportJobItemAsync(ControlPlaneReportJobItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await runtime.ReportAgentJobItemAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneJobOperationResult> ReadJobAsync(ControlPlaneReadJobQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.ReadAgentJobAsync(query, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneJobListResult> ListJobsAsync(ControlPlaneListJobsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await runtime.ListAgentJobsAsync(query, cancellationToken).ConfigureAwait(false);
    }
}
