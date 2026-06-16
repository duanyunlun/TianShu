using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Environment;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Tools;
using TianShu.Contracts.Workflows;
using AgentRosterEntry = TianShu.Contracts.Projections.AgentRosterEntry;
using AgentRosterProjection = TianShu.Contracts.Projections.AgentRosterProjection;
using ArtifactCollectionProjection = TianShu.Contracts.Projections.ArtifactCollectionProjection;
using ArtifactProjection = TianShu.Contracts.Projections.ArtifactProjection;
using TaskBoardProjection = TianShu.Contracts.Projections.TaskBoardProjection;
using WorkflowBoardProjection = TianShu.Contracts.Projections.WorkflowBoardProjection;
using TianShu.Execution.Runtime;
using Task = System.Threading.Tasks.Task;

namespace TianShu.VSSDK.Sidecar.Tests;

internal sealed class CliConsumerFakeRuntime : IExecutionRuntimeDiagnostics
{
    public ControlPlaneInitializeRuntimeCommand? InitializedOptions { get; private set; }
    public List<ControlPlaneApprovalResolution> ApprovalResponses { get; } = [];
    public List<ControlPlanePermissionGrant> PermissionResponses { get; } = [];
    public List<ControlPlaneUserInputSubmission> UserInputResponses { get; } = [];
    public List<(int Limit, bool Archived, bool MatchCurrentCwd)> ThreadListCalls { get; } = [];
    public List<ControlPlaneThreadListQuery> ThreadListRequestCalls { get; } = [];
    public List<string> ResumeThreadCalls { get; } = [];
    public List<ControlPlaneResumeThreadCommand> ResumeThreadRequestCalls { get; } = [];
    public List<ControlPlaneLoadedThreadListQuery> ThreadLoadedListCalls { get; } = [];
    public List<ControlPlaneReadThreadQuery> ThreadReadCalls { get; } = [];
    public List<ControlPlaneCompactThreadCommand> ThreadCompactCalls { get; } = [];
    public List<ControlPlaneCleanBackgroundTerminalsCommand> ThreadCleanBackgroundTerminalCalls { get; } = [];
    public List<ControlPlaneUnsubscribeThreadCommand> ThreadUnsubscribeCalls { get; } = [];
    public List<ControlPlaneIncrementThreadElicitationCommand> ThreadIncrementElicitationCalls { get; } = [];
    public List<ControlPlaneDecrementThreadElicitationCommand> ThreadDecrementElicitationCalls { get; } = [];
    public List<string> DeleteThreadCalls { get; } = [];
    public int ClearThreadsCallCount { get; private set; }
    public List<ControlPlaneUpdateThreadMetadataCommand> ThreadMetadataUpdateCalls { get; } = [];
    public List<ControlPlaneFuzzyFileSearchQuery> FuzzyFileSearchCalls { get; } = [];
    public List<ControlPlaneStartFuzzyFileSearchSessionCommand> FuzzyFileSearchSessionStartCalls { get; } = [];
    public List<ControlPlaneUpdateFuzzyFileSearchSessionCommand> FuzzyFileSearchSessionUpdateCalls { get; } = [];
    public List<ControlPlaneStopFuzzyFileSearchSessionCommand> FuzzyFileSearchSessionStopCalls { get; } = [];
    public List<ControlPlaneWindowsSandboxSetupStartCommand> WindowsSandboxSetupCalls { get; } = [];
    public List<ControlPlaneMcpServerOauthLoginStartCommand> McpServerOauthLoginCalls { get; } = [];
    public List<ControlPlaneSkillCatalogQuery> SkillsListCalls { get; } = [];
    public List<ControlPlaneSkillConfigWriteCommand> SkillConfigWriteCalls { get; } = [];
    public List<ControlPlaneMcpServerStatusQuery> McpServerStatusCalls { get; } = [];
    public List<ControlPlaneConfigReadQuery> ConfigReadCalls { get; } = [];
    public List<ControlPlaneConfigRequirementsQuery> ConfigRequirementsReadCalls { get; } = [];
    public List<ControlPlaneConfigValueWriteCommand> ConfigValueWriteCalls { get; } = [];
    public List<ControlPlaneConfigBatchWriteCommand> ConfigBatchWriteCalls { get; } = [];
    public List<ControlPlaneReviewStartCommand> ReviewStartCalls { get; } = [];
    public List<ControlPlaneRegisterAgentThreadCommand> AgentThreadRegistrationCalls { get; } = [];
    public List<ControlPlaneCreateJobCommand> AgentJobCreateCalls { get; } = [];
    public List<ControlPlaneDispatchJobCommand> AgentJobDispatchCalls { get; } = [];
    public List<ControlPlaneReportJobItemCommand> AgentJobItemReportCalls { get; } = [];
    public List<ControlPlaneReadJobQuery> AgentJobReadCalls { get; } = [];
    public List<GetThreadProjection> ThreadProjectionCalls { get; } = [];
    public List<GetAgentRoster> AgentRosterProjectionCalls { get; } = [];
    public List<GetTeamProjection> TeamProjectionCalls { get; } = [];
    public List<GetWorkflowBoard> WorkflowBoardProjectionCalls { get; } = [];
    public List<GetTaskBoard> TaskBoardProjectionCalls { get; } = [];
    public List<GetPlanProjection> PlanProjectionCalls { get; } = [];
    public List<CreateWorkflow> WorkflowCreateCalls { get; } = [];
    public List<PublishPlan> WorkflowPublishPlanCalls { get; } = [];
    public List<CreateTask> WorkflowCreateTaskCalls { get; } = [];
    public List<UpdateTaskState> WorkflowUpdateTaskStateCalls { get; } = [];
    public List<GetSessionOverview> SessionOverviewCalls { get; } = [];
    public List<ListSessions> SessionListCalls { get; } = [];
    public List<GetExecutionTrace> ExecutionTraceCalls { get; } = [];
    public List<ListAttemptSummaries> AttemptSummaryListCalls { get; } = [];
    public List<GetAccountProfile> AccountProfileCalls { get; } = [];
    public List<ListBoundDevices> BoundDeviceListCalls { get; } = [];
    public List<ListMemoryProviders> MemoryProviderListCalls { get; } = [];
    public List<ListMemorySpaces> MemorySpaceListCalls { get; } = [];
    public List<ResolveMemoryOverlay> MemoryOverlayCalls { get; } = [];
    public List<FilterMemory> MemoryFilterCalls { get; } = [];
    public List<ListMemoryReviews> MemoryReviewListCalls { get; } = [];
    public List<AddMemory> MemoryAddCalls { get; } = [];
    public List<ExtractMemory> MemoryExtractCalls { get; } = [];
    public List<ImportMemory> MemoryImportCalls { get; } = [];
    public List<ExportMemory> MemoryExportCalls { get; } = [];
    public List<BindMemoryProvider> MemoryBindProviderCalls { get; } = [];
    public List<RunMemoryConsolidation> MemoryConsolidationCalls { get; } = [];
    public List<ForgetMemory> MemoryForgetCalls { get; } = [];
    public List<DeleteMemory> MemoryDeleteCalls { get; } = [];
    public List<SupersedeMemory> MemorySupersedeCalls { get; } = [];
    public List<ApproveMemoryReview> MemoryApproveReviewCalls { get; } = [];
    public List<DemoteMemoryReview> MemoryDemoteReviewCalls { get; } = [];
    public List<MergeMemoryReview> MemoryMergeReviewCalls { get; } = [];
    public List<RestoreMemoryReview> MemoryRestoreReviewCalls { get; } = [];
    public List<RecordMemoryFeedback> MemoryFeedbackCalls { get; } = [];
    public List<RecordMemoryCitation> MemoryCitationCalls { get; } = [];
    public List<GetCapabilityCatalog> ProviderCatalogCalls { get; } = [];
    public List<ResolveEngineBinding> EngineBindingCalls { get; } = [];
    public List<ControlPlaneAgentListQuery> AgentListCalls { get; } = [];
    public List<ListPendingApprovals> ApprovalQueueProjectionCalls { get; } = [];
    public List<ListUserInputRequests> UserInputListCalls { get; } = [];
    public List<CreateCollaborationSpace> CollaborationSpaceCreateCalls { get; } = [];
    public List<ConfigureCollaborationSpace> CollaborationSpaceConfigureCalls { get; } = [];
    public List<ArchiveCollaborationSpace> CollaborationSpaceArchiveCalls { get; } = [];
    public List<GetCollaborationSpaceOverview> CollaborationSpaceOverviewCalls { get; } = [];
    public List<GetCollaborationSpaceProjection> CollaborationSpaceProjectionCalls { get; } = [];
    public List<ListCollaborationSpaces> CollaborationSpaceListCalls { get; } = [];
    public List<BindParticipantToSession> ParticipantSessionBindCalls { get; } = [];
    public List<BindParticipantToWorkflow> ParticipantWorkflowBindCalls { get; } = [];
    public List<UpdateParticipantRole> ParticipantRoleUpdateCalls { get; } = [];
    public List<GetParticipantProjection> ParticipantProjectionCalls { get; } = [];
    public List<GetParticipantViewProjection> ParticipantViewProjectionCalls { get; } = [];
    public List<ListParticipantsInScope> ParticipantListCalls { get; } = [];
    public List<GetArtifactDetail> ArtifactProjectionCalls { get; } = [];
    public List<ListArtifacts> ArtifactCollectionProjectionCalls { get; } = [];
    public List<ControlPlaneRealtimeStartCommand> RealtimeStartCalls { get; } = [];
    public List<ControlPlaneRealtimeAppendTextCommand> RealtimeAppendTextCalls { get; } = [];
    public List<ControlPlaneRealtimeAppendAudioCommand> RealtimeAppendAudioCalls { get; } = [];
    public List<ControlPlaneRealtimeHandoffOutputCommand> RealtimeHandoffOutputCalls { get; } = [];
    public List<ControlPlaneRealtimeStopCommand> RealtimeStopCalls { get; } = [];
    public List<ControlPlaneFeedbackUploadCommand> FeedbackUploadCalls { get; } = [];
    public List<ControlPlaneFeedbackUploadCommand> GovernanceFeedbackUploadCalls { get; } = [];
    public List<ControlPlaneFeedbackUploadCommand> DiagnosticsFeedbackUploadCalls { get; } = [];
    public List<ControlPlaneCommandExecutionStartCommand> CommandExecutionStartCalls { get; } = [];
    public List<ControlPlaneCommandExecutionWriteCommand> CommandExecutionWriteCalls { get; } = [];
    public List<ControlPlaneCommandExecutionTerminateCommand> CommandExecutionTerminateCalls { get; } = [];
    public List<ControlPlaneCommandExecutionResizeCommand> CommandExecutionResizeCalls { get; } = [];
    public List<(string Method, StructuredValue? Parameters)> RpcCalls { get; } = [];
    public int CollaborationModeListCallCount { get; private set; }
    public List<ControlPlanePluginUninstallCommand> PluginUninstallCalls { get; } = [];

    public Func<ControlPlaneThreadSnapshot?, CancellationToken, Task<ControlPlaneThreadSnapshot?>>? ResumeThreadAsyncHandler { get; set; }
    public Func<ControlPlaneLoadedThreadListQuery, CancellationToken, Task<ControlPlaneLoadedThreadListResult>>? ListLoadedThreadsAsyncHandler { get; set; }
    public Func<ControlPlaneReadThreadQuery, CancellationToken, Task<ControlPlaneThreadOperationResult>>? ReadThreadAsyncHandler { get; set; }
    public Func<ControlPlaneCompactThreadCommand, CancellationToken, Task<ControlPlaneThreadCommandAcceptedResult>>? CompactThreadAsyncHandler { get; set; }
    public Func<ControlPlaneCleanBackgroundTerminalsCommand, CancellationToken, Task<ControlPlaneThreadCommandAcceptedResult>>? CleanBackgroundTerminalsAsyncHandler { get; set; }
    public Func<ControlPlaneUnsubscribeThreadCommand, CancellationToken, Task<ControlPlaneThreadUnsubscribeResult>>? UnsubscribeThreadAsyncHandler { get; set; }
    public Func<ControlPlaneIncrementThreadElicitationCommand, CancellationToken, Task<ControlPlaneThreadElicitationResult>>? IncrementThreadElicitationAsyncHandler { get; set; }
    public Func<ControlPlaneDecrementThreadElicitationCommand, CancellationToken, Task<ControlPlaneThreadElicitationResult>>? DecrementThreadElicitationAsyncHandler { get; set; }
    public Func<string, CancellationToken, Task<bool>>? DeleteThreadAsyncHandler { get; set; }
    public Func<CancellationToken, Task<ControlPlaneClearThreadsResult>>? ClearThreadsAsyncHandler { get; set; }
    public Func<ControlPlaneThreadListQuery, CancellationToken, Task<ControlPlaneThreadListResult>>? ListThreadsRequestAsyncHandler { get; set; }
    public Func<ControlPlaneUpdateThreadMetadataCommand, CancellationToken, Task<ControlPlaneThreadOperationResult>>? UpdateThreadMetadataAsyncHandler { get; set; }
    public Func<ControlPlaneFuzzyFileSearchQuery, CancellationToken, Task<ControlPlaneFuzzyFileSearchResult>>? SearchFuzzyFilesAsyncHandler { get; set; }
    public Func<ControlPlaneStartFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? StartFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<ControlPlaneUpdateFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? UpdateFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<ControlPlaneStopFuzzyFileSearchSessionCommand, CancellationToken, Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult>>? StopFuzzyFileSearchSessionAsyncHandler { get; set; }
    public Func<ControlPlaneSkillCatalogQuery, CancellationToken, Task<ControlPlaneSkillCatalogResult>>? ListSkillsAsyncHandler { get; set; }
    public Func<ControlPlaneSkillConfigWriteCommand, CancellationToken, Task<ControlPlaneSkillConfigWriteResult>>? WriteSkillsConfigAsyncHandler { get; set; }
    public Func<CancellationToken, Task<ControlPlaneCollaborationModeCatalogResult>>? ListCollaborationModesAsyncHandler { get; set; }
    public Func<ControlPlaneMcpServerOauthLoginStartCommand, CancellationToken, Task<ControlPlaneMcpServerOauthLoginStartResult>>? StartMcpServerOauthLoginAsyncHandler { get; set; }
    public Func<ControlPlaneConfigReadQuery, CancellationToken, Task<ControlPlaneConfigSnapshotResult>>? ReadConfigAsyncHandler { get; set; }
    public Func<ControlPlaneConfigRequirementsQuery, CancellationToken, Task<ControlPlaneConfigRequirementsResult>>? ReadConfigRequirementsAsyncHandler { get; set; }
    public Func<ControlPlaneConfigValueWriteCommand, CancellationToken, Task<ControlPlaneConfigWriteResult>>? WriteConfigValueAsyncHandler { get; set; }
    public Func<ControlPlaneConfigBatchWriteCommand, CancellationToken, Task<ControlPlaneConfigWriteResult>>? WriteConfigBatchAsyncHandler { get; set; }
    public Func<ControlPlaneWindowsSandboxSetupStartCommand, CancellationToken, Task<ControlPlaneWindowsSandboxSetupStartResult>>? StartWindowsSandboxSetupAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionStartCommand, CancellationToken, Task<ControlPlaneCommandExecutionResult>>? StartCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionWriteCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? WriteCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionTerminateCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? TerminateCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneCommandExecutionResizeCommand, CancellationToken, Task<ControlPlaneCommandExecutionCommandAcceptedResult>>? ResizeCommandExecutionAsyncHandler { get; set; }
    public Func<ControlPlaneRegisterAgentThreadCommand, CancellationToken, Task<ControlPlaneAgentThreadRegistrationResult>>? RegisterAgentThreadAsyncHandler { get; set; }
    public Func<ControlPlaneCreateJobCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? CreateAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneDispatchJobCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? DispatchAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneReportJobItemCommand, CancellationToken, Task<ControlPlaneJobOperationResult>>? ReportAgentJobItemAsyncHandler { get; set; }
    public Func<ControlPlaneReadJobQuery, CancellationToken, Task<ControlPlaneJobOperationResult>>? ReadAgentJobAsyncHandler { get; set; }
    public Func<ControlPlaneListJobsQuery, CancellationToken, Task<ControlPlaneJobListResult>>? ListAgentJobsAsyncHandler { get; set; }
    public Func<GetThreadProjection, CancellationToken, Task<TianShu.Contracts.Projections.ThreadProjection?>>? GetThreadProjectionAsyncHandler { get; set; }
    public Func<GetAgentRoster, CancellationToken, Task<AgentRosterProjection?>>? GetAgentRosterProjectionAsyncHandler { get; set; }
    public Func<GetTeamProjection, CancellationToken, Task<TeamProjection?>>? GetTeamProjectionAsyncHandler { get; set; }
    public Func<GetWorkflowBoard, CancellationToken, Task<WorkflowBoardProjection?>>? GetWorkflowBoardProjectionAsyncHandler { get; set; }
    public Func<GetTaskBoard, CancellationToken, Task<TaskBoardProjection?>>? GetTaskBoardProjectionAsyncHandler { get; set; }
    public Func<GetPlanProjection, CancellationToken, Task<PlanProjection?>>? GetPlanProjectionAsyncHandler { get; set; }
    public Func<CreateWorkflow, CancellationToken, Task<Workflow>>? CreateWorkflowAsyncHandler { get; set; }
    public Func<PublishPlan, CancellationToken, Task<PlanProjection>>? PublishPlanAsyncHandler { get; set; }
    public Func<CreateTask, CancellationToken, Task<TianShu.Contracts.Workflows.Task>>? CreateTaskAsyncHandler { get; set; }
    public Func<UpdateTaskState, CancellationToken, Task<TianShu.Contracts.Workflows.Task?>>? UpdateTaskStateAsyncHandler { get; set; }
    public Func<GetSessionOverview, CancellationToken, Task<SessionOverviewProjection?>>? GetSessionOverviewAsyncHandler { get; set; }
    public Func<ListSessions, CancellationToken, Task<IReadOnlyList<SessionOverviewProjection>>>? ListSessionsAsyncHandler { get; set; }
    public Func<GetExecutionTrace, CancellationToken, Task<ExecutionTrace?>>? GetExecutionTraceAsyncHandler { get; set; }
    public Func<ListAttemptSummaries, CancellationToken, Task<IReadOnlyList<AttemptSummary>>>? ListAttemptSummariesAsyncHandler { get; set; }
    public Func<GetAccountProfile, CancellationToken, Task<Account?>>? GetAccountProfileAsyncHandler { get; set; }
    public Func<ListBoundDevices, CancellationToken, Task<IReadOnlyList<DeviceBinding>>>? ListBoundDevicesAsyncHandler { get; set; }
    public Func<ListMemoryProviders, CancellationToken, Task<IReadOnlyList<MemoryProviderDescriptor>>>? ListMemoryProvidersAsyncHandler { get; set; }
    public Func<ListMemorySpaces, CancellationToken, Task<IReadOnlyList<MemorySpace>>>? ListMemorySpacesAsyncHandler { get; set; }
    public Func<ResolveMemoryOverlay, CancellationToken, Task<MemoryOverlay>>? ResolveMemoryOverlayAsyncHandler { get; set; }
    public Func<FilterMemory, CancellationToken, Task<MemoryQueryResult>>? FilterMemoryAsyncHandler { get; set; }
    public Func<ListMemoryReviews, CancellationToken, Task<MemoryReviewQueryResult>>? ListMemoryReviewsAsyncHandler { get; set; }
    public Func<AddMemory, CancellationToken, Task<MemoryMutationResult>>? AddMemoryAsyncHandler { get; set; }
    public Func<ExtractMemory, CancellationToken, Task<IReadOnlyList<MemoryCandidate>>>? ExtractMemoryAsyncHandler { get; set; }
    public Func<ImportMemory, CancellationToken, Task<MemoryMutationResult>>? ImportMemoryAsyncHandler { get; set; }
    public Func<ExportMemory, CancellationToken, Task<MemoryQueryResult>>? ExportMemoryAsyncHandler { get; set; }
    public Func<BindMemoryProvider, CancellationToken, Task<MemoryMutationResult>>? BindMemoryProviderAsyncHandler { get; set; }
    public Func<RunMemoryConsolidation, CancellationToken, Task<MemoryConsolidationRunResult>>? RunMemoryConsolidationAsyncHandler { get; set; }
    public Func<ForgetMemory, CancellationToken, Task<MemoryMutationResult>>? ForgetMemoryAsyncHandler { get; set; }
    public Func<DeleteMemory, CancellationToken, Task<MemoryMutationResult>>? DeleteMemoryAsyncHandler { get; set; }
    public Func<SupersedeMemory, CancellationToken, Task<MemoryMutationResult>>? SupersedeMemoryAsyncHandler { get; set; }
    public Func<ApproveMemoryReview, CancellationToken, Task<MemoryMutationResult>>? ApproveMemoryReviewAsyncHandler { get; set; }
    public Func<DemoteMemoryReview, CancellationToken, Task<MemoryMutationResult>>? DemoteMemoryReviewAsyncHandler { get; set; }
    public Func<MergeMemoryReview, CancellationToken, Task<MemoryMutationResult>>? MergeMemoryReviewAsyncHandler { get; set; }
    public Func<RestoreMemoryReview, CancellationToken, Task<MemoryMutationResult>>? RestoreMemoryReviewAsyncHandler { get; set; }
    public Func<RecordMemoryFeedback, CancellationToken, Task<MemoryMutationResult>>? RecordMemoryFeedbackAsyncHandler { get; set; }
    public Func<RecordMemoryCitation, CancellationToken, Task<MemoryMutationResult>>? RecordMemoryCitationAsyncHandler { get; set; }
    public Func<GetCapabilityCatalog, CancellationToken, Task<CapabilityCatalogSnapshot>>? GetCapabilityCatalogAsyncHandler { get; set; }
    public Func<ResolveEngineBinding, CancellationToken, Task<ResolvedEngineBinding>>? ResolveEngineBindingAsyncHandler { get; set; }
    public Func<ControlPlaneAgentListQuery, CancellationToken, Task<ControlPlaneAgentRosterResult>>? ListAgentsAsyncHandler { get; set; }
    public Func<ListPendingApprovals, CancellationToken, Task<TianShu.Contracts.Projections.ApprovalQueueProjection?>>? GetApprovalQueueProjectionAsyncHandler { get; set; }
    public Func<ListUserInputRequests, CancellationToken, Task<IReadOnlyList<UserInputRequest>>>? ListUserInputRequestsAsyncHandler { get; set; }
    public Func<GetCollaborationSpaceOverview, CancellationToken, Task<CollaborationSpaceOverviewProjection?>>? GetCollaborationSpaceOverviewAsyncHandler { get; set; }
    public Func<GetCollaborationSpaceProjection, CancellationToken, Task<TianShu.Contracts.Projections.CollaborationSpaceProjection?>>? GetCollaborationSpaceProjectionAsyncHandler { get; set; }
    public Func<ListCollaborationSpaces, CancellationToken, Task<IReadOnlyList<CollaborationSpaceOverviewProjection>>>? ListCollaborationSpacesAsyncHandler { get; set; }
    public Func<CreateCollaborationSpace, CancellationToken, Task<CollaborationSpace>>? CreateCollaborationSpaceAsyncHandler { get; set; }
    public Func<ConfigureCollaborationSpace, CancellationToken, Task<CollaborationSpace>>? ConfigureCollaborationSpaceAsyncHandler { get; set; }
    public Func<ArchiveCollaborationSpace, CancellationToken, Task<bool>>? ArchiveCollaborationSpaceAsyncHandler { get; set; }
    public Func<BindParticipantToSession, CancellationToken, Task<bool>>? BindParticipantToSessionAsyncHandler { get; set; }
    public Func<BindParticipantToWorkflow, CancellationToken, Task<bool>>? BindParticipantToWorkflowAsyncHandler { get; set; }
    public Func<UpdateParticipantRole, CancellationToken, Task<bool>>? UpdateParticipantRoleAsyncHandler { get; set; }
    public Func<GetParticipantProjection, CancellationToken, Task<ParticipantProjection?>>? GetParticipantProjectionAsyncHandler { get; set; }
    public Func<GetParticipantViewProjection, CancellationToken, Task<TianShu.Contracts.Projections.ParticipantProjection?>>? GetParticipantViewProjectionAsyncHandler { get; set; }
    public Func<ListParticipantsInScope, CancellationToken, Task<IReadOnlyList<ParticipantProjection>>>? ListParticipantsInScopeAsyncHandler { get; set; }
    public Func<GetArtifactDetail, CancellationToken, Task<ArtifactProjection?>>? GetArtifactProjectionAsyncHandler { get; set; }
    public Func<ListArtifacts, CancellationToken, Task<ArtifactCollectionProjection?>>? GetArtifactCollectionProjectionAsyncHandler { get; set; }
    public Func<ControlPlanePluginUninstallCommand, CancellationToken, Task<ControlPlanePluginUninstallResult>>? UninstallPluginAsyncHandler { get; set; }
    public Func<ControlPlaneApprovalResolution, CancellationToken, Task<bool>>? RespondToApprovalAsyncHandler { get; set; }
    public Func<ControlPlanePermissionGrant, CancellationToken, Task<bool>>? RespondToPermissionRequestAsyncHandler { get; set; }
    public Func<ControlPlaneUserInputSubmission, CancellationToken, Task<bool>>? RespondToUserInputAsyncHandler { get; set; }
    public Func<string, StructuredValue?, CancellationToken, Task<StructuredValue>>? InvokeDiagnosticRpcAsyncHandler { get; set; }

    public string RuntimeName => "CliConsumerFakeRuntime";

    public string? ActiveThreadId { get; set; }

    public bool HasActiveTurn { get; set; }

    public event EventHandler<ControlPlaneConversationStreamEventArgs>? StreamEventReceived;

    public void Emit(ControlPlaneConversationStreamEvent streamEvent)
        => StreamEventReceived?.Invoke(this, new ControlPlaneConversationStreamEventArgs(streamEvent));

    public Task InitializeAsync(
        ControlPlaneInitializeRuntimeCommand command,
        Func<ToolInvocationRequest, CancellationToken, Task<ToolInvocationResult>>? dynamicToolCallHandler,
        CancellationToken cancellationToken)
    {
        InitializedOptions = command;
        return Task.CompletedTask;
    }

    public Task<ExecutionRunResult> ExecuteAsync(ExecutionPlan plan, ExecutionRuntimeContext context, CancellationToken cancellationToken)
        => Task.FromException<ExecutionRunResult>(new NotSupportedException("Sidecar fake runtime 不执行 RuntimeStep。"));

    public Task<RuntimeStepResult> ExecuteStepAsync(RuntimeStep step, ExecutionRuntimeContext context, CancellationToken cancellationToken)
        => Task.FromException<RuntimeStepResult>(new NotSupportedException("Sidecar fake runtime 不执行 RuntimeStep。"));

    public Task<ControlPlaneTurnSubmissionResult> SendAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneTurnSubmissionResult { Accepted = true });

    public Task<ControlPlaneTurnSubmissionResult> SendFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneTurnSubmissionResult { Accepted = true, RequestedMode = command.Mode });

    public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePendingFollowUpMutationResult());

    public Task InterruptTurnAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
    {
        ThreadListRequestCalls.Add(query);
        return ListThreadsRequestAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadListResult());
    }

    public Task<TianShu.Contracts.Projections.ThreadProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
    {
        ThreadProjectionCalls.Add(query);
        return GetThreadProjectionAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<TianShu.Contracts.Projections.ThreadProjection?>(null);
    }

    public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection request, CancellationToken cancellationToken)
    {
        PlanProjectionCalls.Add(request);
        return GetPlanProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<PlanProjection?>(null);
    }

    public Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection request, CancellationToken cancellationToken)
    {
        TeamProjectionCalls.Add(request);
        return GetTeamProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<TeamProjection?>(null);
    }

    public Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster request, CancellationToken cancellationToken)
    {
        AgentRosterProjectionCalls.Add(request);
        return GetAgentRosterProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<AgentRosterProjection?>(
                   request.WorkflowId is null
                       ? new AgentRosterProjection(
                       [
                           new AgentRosterEntry(
                               new AgentId("agent-fake"),
                               new ParticipantRef(new ParticipantId("agent-fake"), ParticipantKind.Agent, "Fake Agent"),
                               "member",
                               0,
                               false),
                       ])
                       : null);
    }

    public Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery request, CancellationToken cancellationToken)
    {
        AgentListCalls.Add(request);
        return ListAgentsAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneAgentRosterResult
               {
                   Agents =
                   [
                       new ControlPlaneAgentDescriptor
                       {
                           ThreadId = new ThreadId("agent-fake"),
                           AgentNickname = "Fake Agent",
                           AgentRole = "member",
                           UpdatedAt = DateTimeOffset.UtcNow,
                       },
                   ],
               });
    }

    public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard request, CancellationToken cancellationToken)
    {
        WorkflowBoardProjectionCalls.Add(request);
        return GetWorkflowBoardProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<WorkflowBoardProjection?>(null);
    }

    public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard request, CancellationToken cancellationToken)
    {
        TaskBoardProjectionCalls.Add(request);
        return GetTaskBoardProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<TaskBoardProjection?>(null);
    }

    public async Task<IReadOnlyList<ControlPlaneThreadSummary>> ListThreadsAsync(int limit, bool archived, bool matchCurrentCwd, CancellationToken cancellationToken)
    {
        ThreadListCalls.Add((limit, archived, matchCurrentCwd));
        var result = await ListThreadsAsync(
            new ControlPlaneThreadListQuery
            {
                Limit = limit,
                Archived = archived,
                WorkingDirectory = matchCurrentCwd ? "D:/Work/TianShu" : null,
            },
            cancellationToken).ConfigureAwait(false);
        return result.Threads;
    }

    public Task<ControlPlaneThreadSummary?> StartNewThreadAsync(CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneThreadSummary?>(null);

    public Task<ControlPlaneThreadSummary?> StartNewThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneThreadSummary?>(null);

    public Task<ControlPlaneThreadSummary?> ForkThreadAsync(string threadId, CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneThreadSummary?>(null);

    public Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneThreadSummary?>(null);

    public Task<bool> ArchiveThreadAsync(string threadId, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public Task<bool> DeleteThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        DeleteThreadCalls.Add(threadId);
        return DeleteThreadAsyncHandler?.Invoke(threadId, cancellationToken) ?? Task.FromResult(false);
    }

    public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(CancellationToken cancellationToken)
    {
        ClearThreadsCallCount++;
        return ClearThreadsAsyncHandler?.Invoke(cancellationToken) ?? Task.FromResult(new ControlPlaneClearThreadsResult());
    }

    public Task<bool> RenameThreadAsync(string threadId, string name, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        ResumeThreadCalls.Add(threadId);
        var placeholder = new ControlPlaneThreadSnapshot
        {
            Thread = new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId(threadId),
            },
        };
        return ResumeThreadAsyncHandler is null
            ? placeholder
            : await ResumeThreadAsyncHandler(placeholder, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
    {
        ResumeThreadRequestCalls.Add(command);
        var snapshot = await ResumeThreadAsync(command.ThreadId.Value, cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    public Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
    {
        ThreadLoadedListCalls.Add(query);
        return ListLoadedThreadsAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneLoadedThreadListResult());
    }

    public Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
    {
        ThreadReadCalls.Add(query);
        return ReadThreadAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
    {
        ThreadCompactCalls.Add(command);
        return CompactThreadAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
    }

    public Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
    {
        ThreadCleanBackgroundTerminalCalls.Add(command);
        return CleanBackgroundTerminalsAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
    }

    public Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
    {
        ThreadUnsubscribeCalls.Add(command);
        return UnsubscribeThreadAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadUnsubscribeResult());
    }

    public Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        ThreadIncrementElicitationCalls.Add(command);
        return IncrementThreadElicitationAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadElicitationResult());
    }

    public Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
    {
        ThreadDecrementElicitationCalls.Add(command);
        return DecrementThreadElicitationAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadElicitationResult());
    }

    public Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneThreadOperationResult());

    public Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
    {
        ThreadMetadataUpdateCalls.Add(command);
        return UpdateThreadMetadataAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneThreadOperationResult());
    }

    public Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneThreadOperationResult());

    public Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken)
    {
        FuzzyFileSearchCalls.Add(query);
        return SearchFuzzyFilesAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneFuzzyFileSearchResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        FuzzyFileSearchSessionStartCalls.Add(command);
        return StartFuzzyFileSearchSessionAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        FuzzyFileSearchSessionUpdateCalls.Add(command);
        return UpdateFuzzyFileSearchSessionAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
    {
        FuzzyFileSearchSessionStopCalls.Add(command);
        return StopFuzzyFileSearchSessionAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
    {
        RealtimeStartCalls.Add(command);
        return Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
    {
        RealtimeAppendTextCalls.Add(command);
        return Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
    {
        RealtimeAppendAudioCalls.Add(command);
        return Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
    {
        RealtimeHandoffOutputCalls.Add(command);
        return Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
    {
        RealtimeStopCalls.Add(command);
        return Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    public Task<bool> RespondToApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
    {
        ApprovalResponses.Add(command);
        return RespondToApprovalAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<bool> RespondToPermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
    {
        PermissionResponses.Add(command);
        return RespondToPermissionRequestAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<bool> RespondToUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
    {
        UserInputResponses.Add(command);
        return RespondToUserInputAsyncHandler?.Invoke(command, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<TianShu.Contracts.Projections.ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
    {
        ApprovalQueueProjectionCalls.Add(query);
        return GetApprovalQueueProjectionAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<TianShu.Contracts.Projections.ApprovalQueueProjection?>(null);
    }

    public Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
    {
        UserInputListCalls.Add(query);
        return ListUserInputRequestsAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<UserInputRequest>>([]);
    }

    public Task<Artifact> PublishArtifactAsync(PublishArtifact request, CancellationToken cancellationToken)
        => Task.FromResult(request.Artifact);

    public Task<Artifact> PromoteArtifactAsync(PromoteArtifact request, CancellationToken cancellationToken)
        => Task.FromResult(new Artifact(
            request.ArtifactId,
            new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
            "fake-artifact.md",
            ArtifactKind.Document,
            state: ArtifactLifecycleState.Promoted));

    public Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask request, CancellationToken cancellationToken)
        => Task.FromResult(new Artifact(
            request.ArtifactId,
            new CollaborationSpaceRef(new CollaborationSpaceId("space-fake"), "space-fake", "Fake Space"),
            "fake-artifact.md",
            ArtifactKind.Document,
            state: ArtifactLifecycleState.Published));

    public Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail request, CancellationToken cancellationToken)
    {
        ArtifactProjectionCalls.Add(request);
        return GetArtifactProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<ArtifactProjection?>(null);
    }

    public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts request, CancellationToken cancellationToken)
    {
        ArtifactCollectionProjectionCalls.Add(request);
        return GetArtifactCollectionProjectionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<ArtifactCollectionProjection?>(null);
    }

    Task<ControlPlaneFeedbackUploadResult> IGovernanceControlPlaneClient.UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
    {
        FeedbackUploadCalls.Add(request);
        GovernanceFeedbackUploadCalls.Add(request);
        return Task.FromResult(new ControlPlaneFeedbackUploadResult());
    }

    Task<ControlPlaneFeedbackUploadResult> IDiagnosticsControlPlaneClient.UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand request, CancellationToken cancellationToken)
    {
        FeedbackUploadCalls.Add(request);
        DiagnosticsFeedbackUploadCalls.Add(request);
        return Task.FromResult(new ControlPlaneFeedbackUploadResult());
    }

    public Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneModelCatalogResult());

    public Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(GetCapabilityCatalog request, CancellationToken cancellationToken)
    {
        ProviderCatalogCalls.Add(request);
        return GetCapabilityCatalogAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new CapabilityCatalogSnapshot());
    }

    public Task<ResolvedEngineBinding> ResolveEngineBindingAsync(ResolveEngineBinding request, CancellationToken cancellationToken)
    {
        EngineBindingCalls.Add(request);
        return ResolveEngineBindingAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ResolvedEngineBinding(null));
    }

    public Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery request, CancellationToken cancellationToken)
    {
        SkillsListCalls.Add(request);
        return ListSkillsAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneSkillCatalogResult());
    }

    public Task<ControlPlaneSkillConfigWriteResult> WriteSkillsConfigAsync(ControlPlaneSkillConfigWriteCommand request, CancellationToken cancellationToken)
    {
        SkillConfigWriteCalls.Add(request);
        return WriteSkillsConfigAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneSkillConfigWriteResult());
    }

    public Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneRemoteSkillCatalogResult());

    public Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneRemoteSkillExportResult());

    public Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginCatalogResult());

    public Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginReadResult());

    public Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlanePluginInstallResult());

    public Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand request, CancellationToken cancellationToken)
    {
        PluginUninstallCalls.Add(request);
        return UninstallPluginAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlanePluginUninstallResult());
    }

    public Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneAppCatalogResult());

    public Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneExperimentalFeatureCatalogResult());

    public Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery request, CancellationToken cancellationToken)
    {
        McpServerStatusCalls.Add(request);
        return Task.FromResult(new ControlPlaneMcpServerCatalogResult());
    }

    public Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(ControlPlaneMcpServerReloadCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneMcpServerReloadResult());

    public Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(ControlPlaneProviderPackageReloadCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneProviderPackageReloadResult());

    public Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand request, CancellationToken cancellationToken)
    {
        McpServerOauthLoginCalls.Add(request);
        return StartMcpServerOauthLoginAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneMcpServerOauthLoginStartResult());
    }

    public Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery request, CancellationToken cancellationToken)
        => Task.FromResult<ControlPlaneConversationArtifact?>(null);

    public Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneGitDiffArtifact());

    public Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand request, CancellationToken cancellationToken)
    {
        ReviewStartCalls.Add(request);
        return Task.FromResult(new ControlPlaneReviewStartResult());
    }

    public Task<Workflow> CreateWorkflowAsync(CreateWorkflow request, CancellationToken cancellationToken)
    {
        WorkflowCreateCalls.Add(request);
        return CreateWorkflowAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(
                   new Workflow(
                       request.WorkflowId,
                       new CollaborationSpaceRef(request.CollaborationSpaceId, request.CollaborationSpaceId.Value, request.CollaborationSpaceId.Value),
                       request.DisplayName,
                       WorkflowState.Draft,
                       request.OwnerParticipant,
                       request.ThreadId));
    }

    public Task<PlanProjection> PublishPlanAsync(PublishPlan request, CancellationToken cancellationToken)
    {
        WorkflowPublishPlanCalls.Add(request);
        return PublishPlanAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new PlanProjection(request.WorkflowId, request.Plan));
    }

    public Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask request, CancellationToken cancellationToken)
    {
        WorkflowCreateTaskCalls.Add(request);
        return CreateTaskAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(request.Task);
    }

    public Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState request, CancellationToken cancellationToken)
    {
        WorkflowUpdateTaskStateCalls.Add(request);
        return UpdateTaskStateAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult<TianShu.Contracts.Workflows.Task?>(null);
    }

    public Task<ControlPlaneJobOperationResult> CreateAgentJobAsync(ControlPlaneCreateJobCommand request, CancellationToken cancellationToken)
    {
        AgentJobCreateCalls.Add(request);
        return CreateAgentJobAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> DispatchAgentJobAsync(ControlPlaneDispatchJobCommand request, CancellationToken cancellationToken)
    {
        AgentJobDispatchCalls.Add(request);
        return DispatchAgentJobAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> ReportAgentJobItemAsync(ControlPlaneReportJobItemCommand request, CancellationToken cancellationToken)
    {
        AgentJobItemReportCalls.Add(request);
        return ReportAgentJobItemAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobOperationResult> ReadAgentJobAsync(ControlPlaneReadJobQuery request, CancellationToken cancellationToken)
    {
        AgentJobReadCalls.Add(request);
        return ReadAgentJobAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneJobOperationResult());
    }

    public Task<ControlPlaneJobListResult> ListAgentJobsAsync(ControlPlaneListJobsQuery request, CancellationToken cancellationToken)
        => ListAgentJobsAsyncHandler?.Invoke(request, cancellationToken)
           ?? Task.FromResult(new ControlPlaneJobListResult());

    public Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand request, CancellationToken cancellationToken)
    {
        AgentThreadRegistrationCalls.Add(request);
        return RegisterAgentThreadAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneAgentThreadRegistrationResult());
    }

    public Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
    {
        CollaborationModeListCallCount++;
        return ListCollaborationModesAsyncHandler?.Invoke(cancellationToken)
               ?? Task.FromResult(new ControlPlaneCollaborationModeCatalogResult());
    }

    public Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken)
    {
        ConfigReadCalls.Add(query);
        return ReadConfigAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneConfigSnapshotResult());
    }

    public Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken)
    {
        ConfigRequirementsReadCalls.Add(query);
        return ReadConfigRequirementsAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new ControlPlaneConfigRequirementsResult());
    }

    public Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken)
    {
        ConfigValueWriteCalls.Add(command);
        return WriteConfigValueAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneConfigWriteResult());
    }

    public Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken)
    {
        ConfigBatchWriteCalls.Add(command);
        return WriteConfigBatchAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new ControlPlaneConfigWriteResult());
    }

    public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
    {
        CollaborationSpaceCreateCalls.Add(command);
        return CreateCollaborationSpaceAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef));
    }

    public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
    {
        CollaborationSpaceConfigureCalls.Add(command);
        return ConfigureCollaborationSpaceAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new CollaborationSpace(
                   command.SpaceId,
                   command.SpaceId.Value,
                   command.DisplayName ?? command.SpaceId.Value,
                   command.Profile ?? new CollaborationSpaceProfile("fake collaboration"),
                   command.Defaults ?? CollaborationDefaultSet.Empty,
                   command.PolicyRef));
    }

    public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
    {
        CollaborationSpaceArchiveCalls.Add(command);
        return ArchiveCollaborationSpaceAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(true);
    }

    public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
    {
        CollaborationSpaceOverviewCalls.Add(query);
        return GetCollaborationSpaceOverviewAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<CollaborationSpaceOverviewProjection?>(null);
    }

    public Task<TianShu.Contracts.Projections.CollaborationSpaceProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
    {
        CollaborationSpaceProjectionCalls.Add(query);
        return GetCollaborationSpaceProjectionAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<TianShu.Contracts.Projections.CollaborationSpaceProjection?>(
                   new TianShu.Contracts.Projections.CollaborationSpaceProjection(
                       new CollaborationSpaceRef(query.SpaceId, query.SpaceId.Value, query.SpaceId.Value),
                       ActiveSessionCount: 0,
                       ActiveThreadCount: 0,
                       IsArchived: false));
    }

    public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
    {
        CollaborationSpaceListCalls.Add(query);
        return ListCollaborationSpacesAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<CollaborationSpaceOverviewProjection>>([]);
    }

    public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
    {
        ParticipantSessionBindCalls.Add(command);
        return BindParticipantToSessionAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(true);
    }

    public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
    {
        ParticipantWorkflowBindCalls.Add(command);
        return BindParticipantToWorkflowAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(true);
    }

    public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
    {
        ParticipantRoleUpdateCalls.Add(command);
        return UpdateParticipantRoleAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(true);
    }

    public Task<ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
    {
        ParticipantProjectionCalls.Add(query);
        return GetParticipantProjectionAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<ParticipantProjection?>(new ParticipantProjection(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value, "member"));
    }

    public Task<TianShu.Contracts.Projections.ParticipantProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
    {
        ParticipantViewProjectionCalls.Add(query);
        return GetParticipantViewProjectionAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<TianShu.Contracts.Projections.ParticipantProjection?>(
                   new TianShu.Contracts.Projections.ParticipantProjection(
                       new ParticipantRef(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value),
                       ScopeKind: "participant",
                       ScopeKey: query.ParticipantId.Value,
                       Role: "member",
                       IsActive: true));
    }

    public Task<IReadOnlyList<ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
    {
        ParticipantListCalls.Add(query);
        return ListParticipantsInScopeAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<ParticipantProjection>>([]);
    }

    public Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
    {
        SessionOverviewCalls.Add(query);
        return GetSessionOverviewAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<SessionOverviewProjection?>(null);
    }

    public Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
    {
        SessionListCalls.Add(query);
        return ListSessionsAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<SessionOverviewProjection>>([]);
    }

    public Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
    {
        ExecutionTraceCalls.Add(query);
        return GetExecutionTraceAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<ExecutionTrace?>(null);
    }

    public Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
    {
        AttemptSummaryListCalls.Add(query);
        return ListAttemptSummariesAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<AttemptSummary>>([]);
    }

    public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
    {
        AccountProfileCalls.Add(query);
        return GetAccountProfileAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<Account?>(null);
    }

    public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
    {
        BoundDeviceListCalls.Add(query);
        return ListBoundDevicesAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<DeviceBinding>>([]);
    }

    public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
    {
        MemorySpaceListCalls.Add(query);
        return ListMemorySpacesAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<MemorySpace>>([]);
    }

    public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
    {
        MemoryOverlayCalls.Add(query);
        return ResolveMemoryOverlayAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new MemoryOverlay());
    }

    public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
    {
        MemoryProviderListCalls.Add(query);
        return ListMemoryProvidersAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>([]);
    }

    public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
    {
        MemoryFilterCalls.Add(query);
        return FilterMemoryAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new MemoryQueryResult());
    }

    public Task<MemoryReviewQueryResult> ListMemoryReviewsAsync(ListMemoryReviews query, CancellationToken cancellationToken)
    {
        MemoryReviewListCalls.Add(query);
        return ListMemoryReviewsAsyncHandler?.Invoke(query, cancellationToken)
               ?? Task.FromResult(new MemoryReviewQueryResult());
    }

    public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
    {
        MemoryAddCalls.Add(command);
        return AddMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
    {
        MemoryExtractCalls.Add(command);
        return ExtractMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult<IReadOnlyList<MemoryCandidate>>([]);
    }

    public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
    {
        MemoryImportCalls.Add(command);
        return ImportMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
    {
        MemoryExportCalls.Add(command);
        return ExportMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryQueryResult());
    }

    public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
    {
        MemoryBindProviderCalls.Add(command);
        return BindMemoryProviderAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
    {
        MemoryConsolidationCalls.Add(command);
        return RunMemoryConsolidationAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryConsolidationRunResult(0, 0));
    }

    public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
    {
        MemoryForgetCalls.Add(command);
        return ForgetMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
    {
        MemoryDeleteCalls.Add(command);
        return DeleteMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
    {
        MemorySupersedeCalls.Add(command);
        return SupersedeMemoryAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryApproveReviewCalls.Add(command);
        return ApproveMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Active));
    }

    public Task<MemoryMutationResult> DemoteMemoryReviewAsync(DemoteMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryDemoteReviewCalls.Add(command);
        return DemoteMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.Archived));
    }

    public Task<MemoryMutationResult> MergeMemoryReviewAsync(MergeMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryMergeReviewCalls.Add(command);
        return MergeMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.TargetRecordId, MemoryLifecycleStatus.Active));
    }

    public Task<MemoryMutationResult> RestoreMemoryReviewAsync(RestoreMemoryReview command, CancellationToken cancellationToken)
    {
        MemoryRestoreReviewCalls.Add(command);
        return RestoreMemoryReviewAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true, command.MemoryRecordId, MemoryLifecycleStatus.PendingReview));
    }

    public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
    {
        MemoryFeedbackCalls.Add(command);
        return RecordMemoryFeedbackAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
    {
        MemoryCitationCalls.Add(command);
        return RecordMemoryCitationAsyncHandler?.Invoke(command, cancellationToken)
               ?? Task.FromResult(new MemoryMutationResult(true));
    }

    public Task<ControlPlaneTurnSubmissionResult> RunUserShellCommandAsync(string command, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneTurnSubmissionResult { Accepted = true });

    public Task<ControlPlaneCommandExecutionResult> StartCommandExecutionAsync(ControlPlaneCommandExecutionStartCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionStartCalls.Add(request);
        return StartCommandExecutionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneCommandExecutionResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> WriteCommandExecutionAsync(ControlPlaneCommandExecutionWriteCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionWriteCalls.Add(request);
        return WriteCommandExecutionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> TerminateCommandExecutionAsync(ControlPlaneCommandExecutionTerminateCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionTerminateCalls.Add(request);
        return TerminateCommandExecutionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCommandExecutionCommandAcceptedResult> ResizeCommandExecutionAsync(ControlPlaneCommandExecutionResizeCommand request, CancellationToken cancellationToken)
    {
        CommandExecutionResizeCalls.Add(request);
        return ResizeCommandExecutionAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneCommandExecutionCommandAcceptedResult());
    }

    public Task<ControlPlaneCodeModeResult> ExecuteCodeModeAsync(ControlPlaneCodeModeExecCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneCodeModeResult());

    public Task<ControlPlaneCodeModeResult> WaitCodeModeAsync(ControlPlaneCodeModeWaitCommand request, CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneCodeModeResult());

    public Task<ControlPlaneWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(ControlPlaneWindowsSandboxSetupStartCommand request, CancellationToken cancellationToken)
    {
        WindowsSandboxSetupCalls.Add(request);
        return StartWindowsSandboxSetupAsyncHandler?.Invoke(request, cancellationToken)
               ?? Task.FromResult(new ControlPlaneWindowsSandboxSetupStartResult());
    }

    public Task<StructuredValue> InvokeDiagnosticRpcAsync(string method, StructuredValue? parameters, CancellationToken cancellationToken)
    {
        RpcCalls.Add((method, parameters));
        return InvokeDiagnosticRpcAsyncHandler?.Invoke(method, parameters, cancellationToken)
               ?? Task.FromResult(StructuredValue.FromJsonElement(ReflectionTestHelper.ParseJsonElement("{}")));
    }

    public Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ControlPlaneDebugClearMemoriesResult
        {
            StateDbPath = "D:/TianShu/state/state.db",
            MemoryRootPath = "D:/TianShu/memories",
            RemovedMemoryRoot = true,
        });

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
