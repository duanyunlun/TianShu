using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Agents;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Diagnostics;
using System.Runtime.CompilerServices;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Host;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using GetPlanProjection = TianShu.Contracts.Workflows.GetPlanProjection;
using GetTaskBoard = TianShu.Contracts.Workflows.GetTaskBoard;
using GetWorkflowBoard = TianShu.Contracts.Workflows.GetWorkflowBoard;
using TianShu.ControlPlane.Abstractions;
using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.ProjectionStores;

namespace TianShu.HostGateway;

/// <summary>
/// TianShu 宿主网关一期实现。
/// Phase-one TianShu host-gateway implementation.
/// </summary>
public sealed class TianShuHostGateway : ITianShuHostGateway
{
    private const string FallbackNotificationCode = "host_projection";

    private readonly ITianShuControlPlane controlPlane;
    private readonly IProjectionSnapshotStore? projectionSnapshotStore;

    /// <summary>
    /// 初始化宿主网关。
    /// Initializes the host gateway.
    /// </summary>
    public TianShuHostGateway(ITianShuControlPlane controlPlane)
        : this(controlPlane, projectionSnapshotStore: null)
    {
    }

    /// <summary>
    /// 初始化宿主网关，并按需接入真实投影视图快照存储。
    /// Initializes the host gateway and optionally wires in the real projection snapshot store.
    /// </summary>
    public TianShuHostGateway(
        ITianShuControlPlane controlPlane,
        IProjectionSnapshotStore? projectionSnapshotStore)
    {
        this.controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
        this.projectionSnapshotStore = projectionSnapshotStore;
    }

    /// <inheritdoc />
    public async ValueTask<HostOperationResult> InvokeAsync(HostOperationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var controlResult = await controlPlane.ProcessAsync(
            ToControlOperationRequest(request),
            cancellationToken).ConfigureAwait(false);

        return ToHostOperationResult(request, controlResult);
    }

    /// <inheritdoc />
    async IAsyncEnumerable<HostViewUpdate> IHostGateway.SubscribeAsync(
        HostSubscriptionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var output in SubscribeAsync(request, cancellationToken).ConfigureAwait(false))
        {
            if (output.ViewUpdate is { } viewUpdate)
            {
                yield return viewUpdate;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<HostSnapshot> SnapshotAsync(HostSnapshotRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = new ProjectionSnapshotKey(request.ScopeKind, request.ScopeId);
        HostViewUpdate? viewUpdate = null;
        if (projectionSnapshotStore is not null)
        {
            var snapshot = await projectionSnapshotStore.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                viewUpdate = ToHostViewUpdate(snapshot);
            }
        }

        if (viewUpdate is null)
        {
            viewUpdate = await TryCreateInitialViewUpdateAsync(
                new ProjectionSubscription(
                    new SubscriptionToken($"snapshot:{request.HostId}:{request.ScopeKind}:{request.ScopeId}"),
                    request.ScopeKind,
                    request.ScopeId),
                cancellationToken).ConfigureAwait(false);
        }

        return new HostSnapshot(
            $"snapshot:{request.HostId}:{request.ScopeKind}:{request.ScopeId}",
            request.ScopeKind,
            request.ScopeId,
            projections: viewUpdate?.Delta is { } delta ? [delta.Payload] : Array.Empty<ProjectionPayload>());
    }

    /// <inheritdoc />
    public async Task<HostTurnSubmissionResult> SubmitTurnAsync(HostSubmitTurn command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interactionEnvelope = MapInteractionEnvelope(command.Envelope);
        ThreadId? threadId = command.Envelope.Target?.ThreadId;
        if (threadId is { } resumeThreadId)
        {
            await controlPlane.Conversations.ResumeThreadAsync(
                new ControlPlaneResumeThreadCommand
                {
                    ThreadId = resumeThreadId,
                },
                cancellationToken).ConfigureAwait(false);
        }

        var result = await controlPlane.Conversations.SubmitTurnAsync(
            new ControlPlaneSubmitTurnCommand
            {
                Envelope = interactionEnvelope,
                Inputs = interactionEnvelope.Items.Select(MapInput).ToArray(),
                History = command.History,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostTurnSubmissionResult
        {
            Accepted = result.Accepted,
            Message = result.Message,
            TurnId = result.TurnId,
            TurnStatus = result.TurnStatus,
            CorrelationId = result.CorrelationId,
            RequestedMode = MapFollowUpMode(result.RequestedMode),
            EffectiveMode = MapFollowUpMode(result.EffectiveMode),
            ThreadId = threadId,
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> ResolveApprovalAsync(HostResolveApproval command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Governance.ResolveApprovalAsync(
            new ControlPlaneApprovalResolution
            {
                Envelope = MapGovernanceEnvelope(
                    command.Context,
                    $"host-approval-{command.CallId.Value}",
                    "approval_response",
                    BuildApprovalPayload(command),
                    command.ResolvedByParticipantId),
                CallId = command.CallId,
                Decision = MapApprovalDecision(command.Decision),
                Note = command.Note,
                CommandPrefix = command.CommandPrefix,
                NetworkHost = command.NetworkHost,
                NetworkAction = command.NetworkAction,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "审批已提交。" : "审批未被接受。",
            CallId = command.CallId,
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> GrantPermissionAsync(HostGrantPermission command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Governance.ResolvePermissionRequestAsync(
            new ControlPlanePermissionGrant
            {
                Envelope = MapGovernanceEnvelope(
                    command.Context,
                    $"host-permission-{command.CallId.Value}",
                    "permission_response",
                    BuildPermissionPayload(command),
                    command.GrantedByParticipantId),
                CallId = command.CallId,
                Permissions = command.Permissions,
                Scope = command.Scope switch
                {
                    HostPermissionScope.Session => ControlPlanePermissionScope.Session,
                    _ => ControlPlanePermissionScope.Turn,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "权限授予已提交。" : "权限授予未被接受。",
            CallId = command.CallId,
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> SubmitUserInputAsync(HostSubmitUserInput command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Governance.SubmitUserInputAsync(
            new ControlPlaneUserInputSubmission
            {
                Envelope = MapGovernanceEnvelope(
                    command.Context,
                    $"host-userinput-{command.CallId.Value}",
                    "user_input_submission",
                    BuildUserInputPayload(command),
                    command.SubmittedByParticipantId),
                CallId = command.CallId,
                Answers = command.Answers,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "补录输入已提交。" : "补录输入未被接受。",
            CallId = command.CallId,
        };
    }

    /// <inheritdoc />
    public async Task<HostTurnSubmissionResult> SubmitFollowUpAsync(HostSubmitFollowUp command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var interactionEnvelope = MapInteractionEnvelope(command.Envelope);
        var result = await controlPlane.Conversations.SubmitFollowUpAsync(
            new ControlPlaneSubmitFollowUpCommand
            {
                Envelope = interactionEnvelope,
                Inputs = interactionEnvelope.Items.Select(MapInput).ToArray(),
                Mode = command.Mode switch
                {
                    HostFollowUpMode.Steer => ControlPlaneFollowUpMode.Steer,
                    HostFollowUpMode.Interrupt => ControlPlaneFollowUpMode.Interrupt,
                    _ => ControlPlaneFollowUpMode.Queue,
                },
                CorrelationId = command.CorrelationId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostTurnSubmissionResult
        {
            Accepted = result.Accepted,
            Message = result.Message,
            TurnId = result.TurnId,
            TurnStatus = result.TurnStatus,
            CorrelationId = result.CorrelationId,
            RequestedMode = MapFollowUpMode(result.RequestedMode),
            EffectiveMode = MapFollowUpMode(result.EffectiveMode),
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadListResult> ListThreadsAsync(HostListThreadsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await controlPlane.Conversations.ListThreadsAsync(
            new ControlPlaneThreadListQuery
            {
                Limit = query.Limit,
                Cursor = query.Cursor,
                Archived = query.Archived,
                WorkingDirectory = query.WorkingDirectory,
                SortKey = query.SortKey,
                ModelProviders = query.ModelProviders,
                SourceKinds = query.SourceKinds,
                SearchTerm = query.SearchTerm,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadListResult
        {
            Threads = result.Threads,
            NextCursor = result.NextCursor,
        };
    }

    /// <inheritdoc />
    public async Task<HostLoadedThreadListResult> ListLoadedThreadsAsync(HostListLoadedThreadsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await controlPlane.Conversations.ListLoadedThreadsAsync(
            new ControlPlaneLoadedThreadListQuery
            {
                Limit = query.Limit,
                Cursor = query.Cursor,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostLoadedThreadListResult
        {
            ThreadIds = result.ThreadIds,
            NextCursor = result.NextCursor,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadSummaryResult> StartThreadAsync(HostStartThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.StartThreadAsync(
            new ControlPlaneStartThreadCommand
            {
                Model = command.Model,
                ModelProvider = command.ModelProvider,
                ServiceTier = command.ServiceTier,
                WorkingDirectory = command.WorkingDirectory,
                ApprovalPolicy = command.ApprovalPolicy,
                SandboxMode = command.SandboxMode,
                Configuration = command.Configuration,
                ServiceName = command.ServiceName,
                BaseInstructions = command.BaseInstructions,
                DeveloperInstructions = command.DeveloperInstructions,
                Personality = command.Personality,
                Ephemeral = command.Ephemeral,
                DynamicTools = command.DynamicTools,
                MockExperimentalField = command.MockExperimentalField,
                PersistExtendedHistory = command.PersistExtendedHistory,
                ExperimentalRawEvents = command.ExperimentalRawEvents,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadSummaryResult
        {
            Thread = result,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadSnapshotResult> ResumeThreadAsync(HostResumeThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.ResumeThreadAsync(
            new ControlPlaneResumeThreadCommand
            {
                ThreadId = command.ThreadId,
                History = command.History,
                Path = command.Path,
                Model = command.Model,
                ModelProvider = command.ModelProvider,
                ServiceTier = command.ServiceTier,
                WorkingDirectory = command.WorkingDirectory,
                ApprovalPolicy = command.ApprovalPolicy,
                SandboxMode = command.SandboxMode,
                Configuration = command.Configuration,
                BaseInstructions = command.BaseInstructions,
                DeveloperInstructions = command.DeveloperInstructions,
                Personality = command.Personality,
                PersistExtendedHistory = command.PersistExtendedHistory,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadSnapshotResult
        {
            Snapshot = result,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadSummaryResult> ForkThreadAsync(HostForkThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.ForkThreadAsync(
            new ControlPlaneForkThreadCommand
            {
                ThreadId = command.ThreadId,
                Path = command.Path,
                Model = command.Model,
                ModelProvider = command.ModelProvider,
                ServiceTier = command.ServiceTier,
                WorkingDirectory = command.WorkingDirectory,
                ApprovalPolicy = command.ApprovalPolicy,
                SandboxMode = command.SandboxMode,
                Configuration = command.Configuration,
                BaseInstructions = command.BaseInstructions,
                DeveloperInstructions = command.DeveloperInstructions,
                Ephemeral = command.Ephemeral,
                PersistExtendedHistory = command.PersistExtendedHistory,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadSummaryResult
        {
            Thread = result,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadOperationResult> ReadThreadAsync(HostReadThreadQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await controlPlane.Conversations.ReadThreadAsync(
            new ControlPlaneReadThreadQuery
            {
                ThreadId = query.ThreadId,
                IncludeTurns = query.IncludeTurns,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadOperationResult
        {
            Thread = result.Thread,
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> RenameThreadAsync(HostRenameThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Conversations.RenameThreadAsync(
            new ControlPlaneRenameThreadCommand
            {
                ThreadId = command.ThreadId,
                Name = command.Name,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "线程已重命名。" : "线程重命名未被接受。",
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> ArchiveThreadAsync(HostArchiveThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Conversations.ArchiveThreadAsync(
            new ControlPlaneArchiveThreadCommand
            {
                ThreadId = command.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "线程已归档。" : "线程归档未被接受。",
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> DeleteThreadAsync(HostDeleteThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var accepted = await controlPlane.Conversations.DeleteThreadAsync(
            new ControlPlaneDeleteThreadCommand
            {
                ThreadId = command.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = accepted,
            Message = accepted ? "线程已删除。" : "线程删除未被接受。",
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> InterruptTurnAsync(HostInterruptTurn command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await controlPlane.Conversations.InterruptTurnAsync(cancellationToken).ConfigureAwait(false);
        return new HostCommandResult
        {
            Accepted = true,
            Message = "中断请求已提交。",
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadOperationResult> UpdateThreadMetadataAsync(HostUpdateThreadMetadata command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.UpdateThreadMetadataAsync(
            new ControlPlaneUpdateThreadMetadataCommand
            {
                ThreadId = command.ThreadId,
                HasGitSha = command.HasGitSha,
                GitSha = command.GitSha,
                HasGitBranch = command.HasGitBranch,
                GitBranch = command.GitBranch,
                HasGitOriginUrl = command.HasGitOriginUrl,
                GitOriginUrl = command.GitOriginUrl,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadOperationResult
        {
            Thread = result.Thread,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadOperationResult> UnarchiveThreadAsync(HostUnarchiveThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.UnarchiveThreadAsync(
            new ControlPlaneUnarchiveThreadCommand
            {
                ThreadId = command.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadOperationResult
        {
            Thread = result.Thread,
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadOperationResult> RollbackThreadAsync(HostRollbackThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.RollbackThreadAsync(
            new ControlPlaneRollbackThreadCommand
            {
                ThreadId = command.ThreadId,
                NumTurns = command.NumTurns,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadOperationResult
        {
            Thread = result.Thread,
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> CompactThreadAsync(HostCompactThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await controlPlane.Conversations.CompactThreadAsync(
            new ControlPlaneCompactThreadCommand
            {
                ThreadId = command.ThreadId,
                KeepRecentTurns = command.KeepRecentTurns,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = true,
            Message = "线程压缩请求已提交。",
        };
    }

    /// <inheritdoc />
    public async Task<HostCommandResult> CleanBackgroundTerminalsAsync(HostCleanBackgroundTerminals command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await controlPlane.Conversations.CleanBackgroundTerminalsAsync(
            new ControlPlaneCleanBackgroundTerminalsCommand
            {
                ThreadId = command.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostCommandResult
        {
            Accepted = true,
            Message = "线程后台终端清理请求已提交。",
        };
    }

    /// <inheritdoc />
    public async Task<HostThreadUnsubscribeResult> UnsubscribeThreadAsync(HostUnsubscribeThread command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Conversations.UnsubscribeThreadAsync(
            new ControlPlaneUnsubscribeThreadCommand
            {
                ThreadId = command.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostThreadUnsubscribeResult
        {
            Status = result.Status,
        };
    }

    /// <inheritdoc />
    public async Task<HostConversationArtifactResult> GetConversationSummaryAsync(HostReadConversationSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var artifact = await controlPlane.Artifacts.GetConversationSummaryAsync(
            new ControlPlaneConversationArtifactQuery
            {
                ThreadId = query.ThreadId,
                RolloutPath = query.RolloutPath,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostConversationArtifactResult
        {
            Artifact = artifact,
        };
    }

    /// <inheritdoc />
    public async Task<HostGitDiffArtifactResult> GetGitDiffToRemoteAsync(HostReadGitDiffToRemoteQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var artifact = await controlPlane.Artifacts.GetGitDiffToRemoteAsync(
            new ControlPlaneGitDiffArtifactQuery
            {
                ThreadId = query.ThreadId,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostGitDiffArtifactResult
        {
            Artifact = artifact,
        };
    }

    /// <inheritdoc />
    public async Task<HostCapabilityCatalogResult> GetCapabilityCatalogAsync(HostGetCapabilityCatalogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var catalog = await controlPlane.Catalog.GetCapabilityCatalogAsync(
            new GetCapabilityCatalog(
                query.WorkspacePath,
                query.IncludeHiddenModels,
                query.ModelLimit),
            cancellationToken).ConfigureAwait(false);

        return new HostCapabilityCatalogResult
        {
            Catalog = catalog,
        };
    }

    /// <inheritdoc />
    public async Task<HostResolvedEngineBindingResult> ResolveEngineBindingAsync(HostResolveEngineBindingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolution = await controlPlane.Catalog.ResolveEngineBindingAsync(
            new ResolveEngineBinding(
                query.WorkspacePath,
                query.PreferredProviderKey,
                query.PreferredModelKey,
                query.ReasoningEffort,
                query.ReasoningSummary,
                query.Verbosity,
                query.PreferWebsocketTransport),
            cancellationToken).ConfigureAwait(false);

        return new HostResolvedEngineBindingResult
        {
            Resolution = resolution,
        };
    }

    /// <inheritdoc />
    public async Task<HostAgentListResult> ListAgentsAsync(HostListAgentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var result = await controlPlane.Agents.ListAgentsAsync(
            new ControlPlaneAgentListQuery
            {
                Limit = query.Limit,
                Cursor = query.Cursor,
                IncludePrimaryThreads = query.IncludePrimaryThreads,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostAgentListResult
        {
            Agents = result.Agents,
            NextCursor = result.NextCursor,
        };
    }

    /// <inheritdoc />
    public async Task<HostExecutionTraceResult> GetExecutionTraceAsync(HostReadExecutionTraceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trace = await controlPlane.Diagnostics.GetExecutionTraceAsync(
            new GetExecutionTrace(query.TraceId),
            cancellationToken).ConfigureAwait(false);

        return new HostExecutionTraceResult
        {
            Trace = trace,
        };
    }

    /// <inheritdoc />
    public async Task<HostAttemptSummaryListResult> ListAttemptSummariesAsync(HostListAttemptSummariesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var attempts = await controlPlane.Diagnostics.ListAttemptSummariesAsync(
            new ListAttemptSummaries(query.ExecutionId),
            cancellationToken).ConfigureAwait(false);

        return new HostAttemptSummaryListResult
        {
            Attempts = attempts,
        };
    }

    /// <inheritdoc />
    public async Task<HostFeedbackUploadResult> UploadFeedbackAsync(HostUploadFeedback command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var result = await controlPlane.Diagnostics.UploadFeedbackAsync(
            new ControlPlaneFeedbackUploadCommand
            {
                Classification = command.Classification,
                IncludeLogs = command.IncludeLogs,
                ThreadId = command.ThreadId,
                Reason = command.Reason,
                ExtraLogFiles = command.ExtraLogFiles,
            },
            cancellationToken).ConfigureAwait(false);

        return new HostFeedbackUploadResult
        {
            ThreadId = result.ThreadId,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<HostOutputEvent> SubscribeAsync(
        HostSubscriptionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscription = request.Subscription;
        var snapshotKey = TryCreateSnapshotKey(subscription);
        if (projectionSnapshotStore is not null
            && snapshotKey is { } key
            && await projectionSnapshotStore.GetAsync(key, cancellationToken).ConfigureAwait(false) is { } snapshot)
        {
            yield return new HostOutputEvent(viewUpdate: ToHostViewUpdate(snapshot));
        }
        else if (await TryCreateInitialViewUpdateAsync(subscription, cancellationToken).ConfigureAwait(false) is { } initialViewUpdate)
        {
            await PersistViewUpdateAsync(subscription, initialViewUpdate, cancellationToken).ConfigureAwait(false);
            yield return new HostOutputEvent(viewUpdate: initialViewUpdate);
        }

        var source = subscription.ScopeKind switch
        {
            ProjectionScopeKind.ApprovalQueue => controlPlane.Subscriptions.SubscribeGovernanceAsync(
                new ControlPlaneGovernanceSubscription
                {
                    ThreadId = TryParseThreadId(subscription.ScopeKey),
                },
                cancellationToken),
            ProjectionScopeKind.Thread => controlPlane.Subscriptions.SubscribeThreadAsync(
                new ControlPlaneThreadSubscription
                {
                    ThreadId = TryParseThreadId(subscription.ScopeKey),
                },
                cancellationToken),
            ProjectionScopeKind.WorkflowBoard or ProjectionScopeKind.TaskBoard or ProjectionScopeKind.Plan => controlPlane.Subscriptions.SubscribeWorkflowAsync(
                new ControlPlaneWorkflowSubscription(),
                cancellationToken),
            ProjectionScopeKind.AgentRoster or ProjectionScopeKind.Participant or ProjectionScopeKind.Team => controlPlane.Subscriptions.SubscribeAgentAsync(
                new ControlPlaneAgentSubscription(),
                cancellationToken),
            ProjectionScopeKind.CollaborationSpace
                or ProjectionScopeKind.Artifact
                or ProjectionScopeKind.ArtifactCollection
                => EmptyProjectionStreamAsync(cancellationToken),
            _ => EmptyProjectionStreamAsync(cancellationToken),
        };

        await foreach (var projectionEvent in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (await TryProjectViewUpdateAsync(subscription, projectionEvent, cancellationToken).ConfigureAwait(false) is { } viewUpdate)
            {
                yield return new HostOutputEvent(viewUpdate: viewUpdate);
                continue;
            }

            if (ShouldIgnoreTypedProjectionEvent(subscription, projectionEvent))
            {
                continue;
            }

            yield return new HostOutputEvent(notification: ProjectNotification(projectionEvent));
        }
    }

    private async Task<HostViewUpdate?> TryCreateInitialViewUpdateAsync(
        ProjectionSubscription subscription,
        CancellationToken cancellationToken)
    {
        return subscription.ScopeKind switch
        {
            ProjectionScopeKind.Thread
                => await TryCreateThreadViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.ApprovalQueue
                => await TryCreateApprovalQueueViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.CollaborationSpace
                => await TryCreateCollaborationSpaceViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.Participant
                => await TryCreateParticipantViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.AgentRoster
                => await TryCreateAgentRosterViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.Team
                => await TryCreateTeamViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.WorkflowBoard
                => await TryCreateWorkflowBoardViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.TaskBoard
                => await TryCreateTaskBoardViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.Plan
                => await TryCreatePlanViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.Artifact
                => await TryCreateArtifactViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            ProjectionScopeKind.ArtifactCollection
                => await TryCreateArtifactCollectionViewUpdateAsync(subscription.ScopeKey, cancellationToken).ConfigureAwait(false),
            _ => null,
        };
    }

    private async Task<HostViewUpdate?> TryCreateThreadViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Conversations.GetThreadProjectionAsync(
            new GetThreadProjection(new ThreadId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.Thread, scopeKey, "thread_projection_unavailable")
            : new HostViewUpdate(delta: new ProjectionDelta(new ThreadProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateApprovalQueueViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Governance.GetApprovalQueueProjectionAsync(
            BuildApprovalQueueQuery(scopeKey),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.ApprovalQueue, scopeKey, "approval_queue_uninitialized")
            : new HostViewUpdate(delta: new ProjectionDelta(new ApprovalQueueProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateCollaborationSpaceViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Collaboration.GetSpaceProjectionAsync(
            new GetCollaborationSpaceProjection(new CollaborationSpaceId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        if (projection is null)
        {
            return CreateResetViewUpdate(ProjectionScopeKind.CollaborationSpace, scopeKey, "collaboration_space_not_found");
        }

        return new HostViewUpdate(
            delta: new ProjectionDelta(
                new CollaborationSpaceProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateParticipantViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Collaboration.GetParticipantViewProjectionAsync(
            new GetParticipantViewProjection(new ParticipantId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        if (projection is null)
        {
            return CreateResetViewUpdate(ProjectionScopeKind.Participant, scopeKey, "participant_not_found");
        }

        return new HostViewUpdate(
            delta: new ProjectionDelta(
                new ParticipantProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateAgentRosterViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var query = string.Equals(scopeKey, "agents", StringComparison.OrdinalIgnoreCase)
            ? new GetAgentRoster()
            : new GetAgentRoster(new WorkflowId(scopeKey));
        var roster = await controlPlane.Agents.GetAgentRosterProjectionAsync(query, cancellationToken).ConfigureAwait(false);

        if (roster is null)
        {
            return CreateResetViewUpdate(ProjectionScopeKind.AgentRoster, scopeKey, "agent_roster_uninitialized");
        }

        return new HostViewUpdate(
            delta: new ProjectionDelta(
                new AgentRosterProjectionPayload(roster)));
    }

    private async Task<HostViewUpdate?> TryCreateTeamViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var team = await controlPlane.Agents.GetTeamProjectionAsync(
            new GetTeamProjection(new TeamId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        if (team is null)
        {
            return CreateResetViewUpdate(ProjectionScopeKind.Team, scopeKey, "team_projection_unavailable");
        }

        return new HostViewUpdate(
            delta: new ProjectionDelta(
                new TeamProjectionPayload(team)));
    }

    private async Task<HostViewUpdate?> TryCreateWorkflowBoardViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Workflows.GetWorkflowBoardProjectionAsync(
            new GetWorkflowBoard(new WorkflowId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.WorkflowBoard, scopeKey, "workflow_board_uninitialized")
            : new HostViewUpdate(delta: new ProjectionDelta(new WorkflowBoardProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateTaskBoardViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Workflows.GetTaskBoardProjectionAsync(
            new GetTaskBoard(new WorkflowId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.TaskBoard, scopeKey, "task_board_uninitialized")
            : new HostViewUpdate(delta: new ProjectionDelta(new TaskBoardProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreatePlanViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Workflows.GetPlanProjectionAsync(
            new GetPlanProjection(new WorkflowId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.Plan, scopeKey, "plan_projection_uninitialized")
            : new HostViewUpdate(delta: new ProjectionDelta(new PlanProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateArtifactViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Artifacts.GetArtifactProjectionAsync(
            new GetArtifactDetail(new ArtifactId(scopeKey)),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.Artifact, scopeKey, "artifact_snapshot_unavailable")
            : new HostViewUpdate(delta: new ProjectionDelta(new ArtifactProjectionPayload(projection)));
    }

    private async Task<HostViewUpdate?> TryCreateArtifactCollectionViewUpdateAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var projection = await controlPlane.Artifacts.GetArtifactCollectionProjectionAsync(
            new ListArtifacts(new CollaborationSpaceId(scopeKey), null),
            cancellationToken).ConfigureAwait(false);

        return projection is null
            ? CreateResetViewUpdate(ProjectionScopeKind.ArtifactCollection, scopeKey, "artifact_collection_snapshot_unavailable")
            : new HostViewUpdate(delta: new ProjectionDelta(new ArtifactCollectionProjectionPayload(projection)));
    }

    private async Task PersistViewUpdateAsync(
        ProjectionSubscription subscription,
        HostViewUpdate viewUpdate,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null || TryCreateSnapshotKey(subscription) is not { } snapshotKey)
        {
            return;
        }

        if (viewUpdate.Reset is { } reset)
        {
            await projectionSnapshotStore.ResetAsync(reset, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (viewUpdate.Delta is { } delta)
        {
            await projectionSnapshotStore.UpsertAsync(snapshotKey, delta, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeConversationStreamAsync(
        HostConversationStreamSubscription request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return controlPlane.Conversations.SubscribeStreamAsync(request.ThreadId, cancellationToken);
    }

    private static async IAsyncEnumerable<ControlPlaneProjectionEvent> SingleNotificationAsync(
        HostNotification notification,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ControlPlaneProjectionEvent
        {
            Kind = notification.Code,
            Timestamp = DateTimeOffset.UtcNow,
            CallId = notification.RelatedCallId,
            Message = notification.Message ?? notification.Title,
            Payload = notification.Payload,
        };
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<ControlPlaneProjectionEvent> EmptyProjectionStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private static HostViewUpdate ToHostViewUpdate(ProjectionSnapshotRecord snapshot)
        => snapshot.Reset is { } reset
            ? new HostViewUpdate(reset: reset)
            : new HostViewUpdate(delta: snapshot.Delta);

    private static HostViewUpdate CreateResetViewUpdate(ProjectionScopeKind scopeKind, string scopeKey, string reason)
        => new(
            reset: new ProjectionReset(
                scopeKind,
                scopeKey,
                reason));

    private static ControlOperationRequest ToControlOperationRequest(HostOperationRequest request)
        => new(
            request.OperationId,
            ReadString(request.Payload, "operation_name", "operationName") ?? GetDefaultControlOperationName(request.OperationKind),
            TryReadControlOperationSubject(request.Payload),
            TryReadGovernanceRequest(request.Payload),
            request.Payload,
            request.Metadata);

    private static HostOperationResult ToHostOperationResult(HostOperationRequest request, ControlOperationResult result)
        => new(
            request.OperationId,
            result.Status switch
            {
                ControlOperationStatus.Accepted => HostOperationStatus.Accepted,
                ControlOperationStatus.Completed => HostOperationStatus.Completed,
                ControlOperationStatus.Rejected => HostOperationStatus.Rejected,
                _ => HostOperationStatus.Failed,
            },
            projection: result.TypedResult.Kind == StructuredValueKind.Null ? null : result.TypedResult,
            diagnostics: result.Issues
                .Select(static issue => new HostDiagnosticRef(issue.Code, "error", issue.Message))
                .ToArray(),
            message: result.Issues.Count > 0
                ? result.Issues[0].Message
                : result.CoreIntent is not null
                    ? $"Control Plane accepted core intent {result.CoreIntent.IntentId.Value}."
                    : null);

    private static string GetDefaultControlOperationName(HostOperationKind operationKind)
        => operationKind switch
        {
            HostOperationKind.Query => "query.host",
            HostOperationKind.Control => "control.host",
            HostOperationKind.State => "state.host",
            HostOperationKind.Governance => "governance.host",
            HostOperationKind.CoreIntent => "turn.submit",
            _ => "host.unspecified",
        };

    private static ControlOperationSubject? TryReadControlOperationSubject(StructuredValue payload)
    {
        var sessionId = ReadString(payload, "session_id", "sessionId");
        var threadId = ReadString(payload, "thread_id", "threadId");
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }

        var workflowId = ReadString(payload, "workflow_id", "workflowId");
        var turnId = ReadString(payload, "turn_id", "turnId");
        return new ControlOperationSubject(
            new SessionId(sessionId),
            new ThreadId(threadId),
            string.IsNullOrWhiteSpace(workflowId) ? null : new WorkflowId(workflowId),
            string.IsNullOrWhiteSpace(turnId) ? null : new TurnId(turnId));
    }

    private static ControlOperationGovernanceRequest? TryReadGovernanceRequest(StructuredValue payload)
    {
        var envelopeId = ReadString(payload, "governance_envelope_id", "governanceEnvelopeId", "envelope_id", "envelopeId");
        if (string.IsNullOrWhiteSpace(envelopeId))
        {
            return null;
        }

        return new ControlOperationGovernanceRequest(
            envelopeId,
            policyIds: ReadStringList(payload, "policy_ids", "policyIds"),
            allowedToolIds: ReadStringList(payload, "allowed_tool_ids", "allowedToolIds"),
            allowedModuleIds: ReadStringList(payload, "allowed_module_ids", "allowedModuleIds"),
            maxSideEffectLevel: ReadSideEffectLevel(payload),
            requiresHumanGate: ReadBoolean(payload, defaultValue: true, "requires_human_gate", "requiresHumanGate"));
    }

    private static string? ReadString(StructuredValue payload, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (payload.TryGetProperty(propertyName, out var value)
                && value is not null
                && value.Kind == StructuredValueKind.String
                && !string.IsNullOrWhiteSpace(value.StringValue))
            {
                return value.StringValue.Trim();
            }
        }

        return null;
    }

    private static SideEffectLevel ReadSideEffectLevel(StructuredValue payload)
    {
        var value = ReadString(payload, "max_side_effect_level", "maxSideEffectLevel");
        return Enum.TryParse<SideEffectLevel>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SideEffectLevel.Unspecified;
    }

    private static IReadOnlyList<string> ReadStringList(StructuredValue payload, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!payload.TryGetProperty(propertyName, out var value) || value is null)
            {
                continue;
            }

            if (value.Kind == StructuredValueKind.Array)
            {
                return value.Items
                    .Where(static item => item.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(item.StringValue))
                    .Select(static item => item.StringValue!.Trim())
                    .ToArray();
            }

            if (value.Kind == StructuredValueKind.String && !string.IsNullOrWhiteSpace(value.StringValue))
            {
                return value.StringValue
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return Array.Empty<string>();
    }

    private static bool ReadBoolean(StructuredValue payload, bool defaultValue, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (payload.TryGetProperty(propertyName, out var value)
                && value is not null
                && value.Kind == StructuredValueKind.Boolean
                && value.BooleanValue.HasValue)
            {
                return value.BooleanValue.Value;
            }
        }

        return defaultValue;
    }

    private static HostNotification ProjectNotification(ControlPlaneProjectionEvent projectionEvent)
    {
        ArgumentNullException.ThrowIfNull(projectionEvent);

        var code = string.IsNullOrWhiteSpace(projectionEvent.Kind)
            ? FallbackNotificationCode
            : projectionEvent.Kind;

        return new HostNotification(
            code,
            MapNotificationKind(projectionEvent),
            BuildNotificationTitle(projectionEvent),
            projectionEvent.Message ?? projectionEvent.Text,
            projectionEvent.CallId,
            payloadKind: projectionEvent.Kind,
            payload: projectionEvent.Payload);
    }

    private static HostNotificationKind MapNotificationKind(ControlPlaneProjectionEvent projectionEvent)
    {
        var normalizedKind = NormalizeToken(projectionEvent.Kind);
        return normalizedKind switch
        {
            "approvalrequested" => HostNotificationKind.ApprovalRequested,
            "permissionrequested" => HostNotificationKind.PermissionRequested,
            "userinputrequested" => HostNotificationKind.UserInputRequested,
            "turncompleted" => HostNotificationKind.TurnCompleted,
            _ when ContainsToken(projectionEvent.Kind, "error") || ContainsToken(projectionEvent.Status, "error")
                => HostNotificationKind.Error,
            _ when ContainsToken(projectionEvent.Kind, "warning") || ContainsToken(projectionEvent.Status, "warning")
                => HostNotificationKind.Warning,
            _ => HostNotificationKind.Info,
        };
    }

    private static HostFollowUpMode? MapFollowUpMode(ControlPlaneFollowUpMode? mode)
        => mode switch
        {
            ControlPlaneFollowUpMode.Steer => HostFollowUpMode.Steer,
            ControlPlaneFollowUpMode.Interrupt => HostFollowUpMode.Interrupt,
            ControlPlaneFollowUpMode.Queue => HostFollowUpMode.Queue,
            _ => null,
        };

    private static ListPendingApprovals BuildApprovalQueueQuery(string scopeKey)
        => string.Equals(scopeKey, "approvals", StringComparison.OrdinalIgnoreCase)
            ? new ListPendingApprovals()
            : new ListPendingApprovals(new ParticipantId(scopeKey));

    private static string BuildNotificationTitle(ControlPlaneProjectionEvent projectionEvent)
    {
        if (projectionEvent.Payload?.TryGetProperty("title", out var titleValue) == true)
        {
            var title = titleValue?.GetString();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectionEvent.Message))
        {
            return projectionEvent.Message;
        }

        if (!string.IsNullOrWhiteSpace(projectionEvent.Text))
        {
            return projectionEvent.Text;
        }

        if (!string.IsNullOrWhiteSpace(projectionEvent.Kind))
        {
            return projectionEvent.Kind;
        }

        return "Host notification";
    }

    private static ControlPlaneApprovalDecision MapApprovalDecision(string decision)
    {
        var normalized = NormalizeToken(decision);
        return normalized switch
        {
            "accept" or "approve" => ControlPlaneApprovalDecision.Approve,
            "session" or "acceptforsession" or "approvesession"
                => ControlPlaneApprovalDecision.ApproveForSession,
            "always" or "acceptandremember" or "approvealways"
                => ControlPlaneApprovalDecision.ApproveAndRemember,
            "acceptwithexecpolicyamendment" or "acceptwithexecpolicy" or "approvewithexecpolicy" or "approvewithexecutionpolicyamendment"
                => ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment,
            "applynetworkpolicyamendment" or "approvenetworkrule" or "applynetworkrule"
                => ControlPlaneApprovalDecision.ApplyNetworkPolicyAmendment,
            "decline" or "reject"
                => ControlPlaneApprovalDecision.Decline,
            "cancel"
                => ControlPlaneApprovalDecision.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "不支持的宿主审批决策。"),
        };
    }

    private static ControlPlaneInputItem MapInput(InteractionItem input)
        => input switch
        {
            TextInteractionItem text => new ControlPlaneTextInput(
                text.Text,
                text.Elements.Select(static element => new ControlPlaneTextElement(
                    new ControlPlaneByteRange(element.ByteRange.Start, element.ByteRange.End),
                    element.Placeholder)).ToArray()),
            ImageInteractionItem image => new ControlPlaneImageInput(image.Url),
            LocalImageInteractionItem localImage => new ControlPlaneLocalImageInput(localImage.Path),
            SkillInteractionItem skill => new ControlPlaneSkillInput(skill.Name, skill.Path),
            MentionInteractionItem mention => new ControlPlaneMentionInput(mention.Name, mention.Path),
            _ => throw new NotSupportedException($"不支持的宿主输入类型：{input.GetType().Name}"),
        };

    private static InteractionEnvelope MapInteractionEnvelope(HostInteractionEnvelope envelope)
        => new(
            envelope.InteractionId,
            new InteractionSource(InteractionSourceKind.Host, envelope.Context.SurfaceId),
            envelope.Items.ToArray(),
            envelope.Target,
            MapRoutingHint(envelope),
            envelope.InitiatedByParticipantId,
            envelope.CreatedAt);

    private static InteractionRoutingHint? MapRoutingHint(HostInteractionEnvelope envelope)
    {
        var hint = envelope.RoutingHint;
        var surface = string.IsNullOrWhiteSpace(hint?.Surface)
            ? envelope.Context.SurfaceId
            : hint!.Surface;

        if (hint is null)
        {
            return new InteractionRoutingHint(Surface: surface);
        }

        return new InteractionRoutingHint(hint.Intent, surface, hint.PreferForeground);
    }

    private static InteractionEnvelope? MapGovernanceEnvelope(
        HostContext? context,
        string interactionId,
        string semanticKind,
        StructuredValue payload,
        ParticipantId? initiatedByParticipantId)
    {
        if (context is null)
        {
            return null;
        }

        return new InteractionEnvelope(
            new InteractionEnvelopeId(interactionId),
            new InteractionSource(InteractionSourceKind.Host, context.SurfaceId),
            [new StructuredInteractionItem(semanticKind, payload)],
            routingHint: new InteractionRoutingHint(Intent: semanticKind, Surface: context.SurfaceId),
            initiatedByParticipantId: initiatedByParticipantId);
    }

    private static StructuredValue BuildApprovalPayload(HostResolveApproval command)
    {
        var payload = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["decision"] = StructuredValue.FromString(command.Decision),
        };

        if (command.ApprovalId is { } approvalId)
        {
            payload["approvalId"] = StructuredValue.FromString(approvalId.Value);
        }

        if (!string.IsNullOrWhiteSpace(command.Note))
        {
            payload["note"] = StructuredValue.FromString(command.Note);
        }

        if (command.CommandPrefix.Count > 0)
        {
            payload["commandPrefix"] = StructuredValue.FromArray(command.CommandPrefix.Select(StructuredValue.FromString).ToArray());
        }

        if (!string.IsNullOrWhiteSpace(command.NetworkHost))
        {
            payload["networkHost"] = StructuredValue.FromString(command.NetworkHost);
        }

        if (!string.IsNullOrWhiteSpace(command.NetworkAction))
        {
            payload["networkAction"] = StructuredValue.FromString(command.NetworkAction);
        }

        return StructuredValue.FromObject(payload);
    }

    private static StructuredValue BuildPermissionPayload(HostGrantPermission command)
    {
        var payload = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["scope"] = StructuredValue.FromString(command.Scope == HostPermissionScope.Session ? "session" : "turn"),
            ["permissions"] = StructuredValue.FromObject(command.Permissions.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
        };

        return StructuredValue.FromObject(payload);
    }

    private static StructuredValue BuildUserInputPayload(HostSubmitUserInput command)
    {
        var payload = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["callId"] = StructuredValue.FromString(command.CallId.Value),
            ["answers"] = StructuredValue.FromObject(command.Answers.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)),
        };

        if (command.RequestId is { } requestId)
        {
            payload["requestId"] = StructuredValue.FromString(requestId.Value);
        }

        return StructuredValue.FromObject(payload);
    }

    private async Task<HostViewUpdate?> TryProjectViewUpdateAsync(
        ProjectionSubscription subscription,
        ControlPlaneProjectionEvent projectionEvent,
        CancellationToken cancellationToken)
    {
        if (projectionSnapshotStore is null)
        {
            return null;
        }

        if (TryCreateSnapshotKey(subscription) is not { } snapshotKey)
        {
            return null;
        }

        if (projectionEvent.Reset is { } reset && MatchesScope(subscription, reset))
        {
            await projectionSnapshotStore.ResetAsync(reset, cancellationToken).ConfigureAwait(false);
            return new HostViewUpdate(reset: reset);
        }

        if (projectionEvent.Delta is { } delta && MatchesScope(subscription, delta))
        {
            await projectionSnapshotStore.UpsertAsync(snapshotKey, delta, cancellationToken).ConfigureAwait(false);
            return new HostViewUpdate(delta: delta);
        }

        return null;
    }

    private static bool MatchesScope(ProjectionSubscription subscription, ProjectionReset reset)
        => subscription.ScopeKind == reset.ScopeKind
           && string.Equals(subscription.ScopeKey, reset.ScopeKey, StringComparison.Ordinal);

    private static bool MatchesScope(ProjectionSubscription subscription, ProjectionDelta delta)
        => TryResolveScope(delta.Payload) is { } resolved
           && subscription.ScopeKind == resolved.ScopeKind
           && (resolved.ScopeKey is null
               || string.Equals(subscription.ScopeKey, resolved.ScopeKey, StringComparison.Ordinal));

    private static bool ShouldIgnoreTypedProjectionEvent(ProjectionSubscription subscription, ControlPlaneProjectionEvent projectionEvent)
        => (projectionEvent.Reset is { } reset && !MatchesScope(subscription, reset))
           || (projectionEvent.Delta is { } delta && !MatchesScope(subscription, delta));

    private static (ProjectionScopeKind ScopeKind, string? ScopeKey)? TryResolveScope(ProjectionPayload payload)
        => payload switch
        {
            CollaborationSpaceProjectionPayload collaboration => (ProjectionScopeKind.CollaborationSpace, collaboration.Projection.CollaborationSpace.Id.Value),
            ThreadProjectionPayload thread => (ProjectionScopeKind.Thread, thread.Projection.ThreadId.Value),
            WorkflowBoardProjectionPayload workflowBoard => (ProjectionScopeKind.WorkflowBoard, workflowBoard.Projection.WorkflowId.Value),
            TaskBoardProjectionPayload taskBoard => (ProjectionScopeKind.TaskBoard, taskBoard.Projection.WorkflowId.Value),
            PlanProjectionPayload plan => (ProjectionScopeKind.Plan, plan.Projection.WorkflowId.Value),
            ParticipantProjectionPayload participant => (ProjectionScopeKind.Participant, participant.Projection.Participant.Id.Value),
            AgentRosterProjectionPayload => (ProjectionScopeKind.AgentRoster, null),
            TeamProjectionPayload team => (ProjectionScopeKind.Team, team.Projection.Team.Id.Value),
            ApprovalQueueProjectionPayload => (ProjectionScopeKind.ApprovalQueue, null),
            ArtifactProjectionPayload artifact => (ProjectionScopeKind.Artifact, artifact.Projection.ArtifactId.Value),
            ArtifactCollectionProjectionPayload collection => (ProjectionScopeKind.ArtifactCollection, collection.Projection.CollaborationSpace.Id.Value),
            _ => null,
        };

    private static ProjectionSnapshotKey? TryCreateSnapshotKey(ProjectionSubscription subscription)
        => new ProjectionSnapshotKey(subscription.ScopeKind, subscription.ScopeKey);

    private static ThreadId? TryParseThreadId(string scopeKey)
    {
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return null;
        }

        var normalized = scopeKey.Trim();
        if (string.Equals(normalized, "approvals", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ThreadId(normalized);
    }

    private static bool ContainsToken(string? value, string token)
        => value is not null
            && value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
