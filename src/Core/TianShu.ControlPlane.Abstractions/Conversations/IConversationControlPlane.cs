using TianShu.Contracts.Conversations;
using ThreadViewProjection = TianShu.Contracts.Projections.ThreadProjection;

namespace TianShu.ControlPlane.Abstractions.Conversations;

public interface IConversationControlPlane
{
    Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken);

    Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken);

    Task InterruptTurnAsync(CancellationToken cancellationToken);

    Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken);

    Task<ThreadViewProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken);

    Task<ControlPlaneThreadSummary?> StartThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken);

    Task<bool> RenameThreadAsync(ControlPlaneRenameThreadCommand command, CancellationToken cancellationToken);

    Task<bool> ArchiveThreadAsync(ControlPlaneArchiveThreadCommand command, CancellationToken cancellationToken);

    Task<bool> DeleteThreadAsync(ControlPlaneDeleteThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(ControlPlaneClearThreadsCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken);

    IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeStreamAsync(ThreadId? threadId, CancellationToken cancellationToken);

    Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken);

    Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken);

    Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken);
}
