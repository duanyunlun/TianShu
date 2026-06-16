using TianShu.Contracts.Agents;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Governance;
using TianShu.Contracts.Host;
using TianShu.Contracts.Identity;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.HostGateway;
using TianShu.ProjectionStores;
using TianShu.Contracts.Sessions;
using TianShu.Contracts.Workflows;
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
using TianShu.ControlPlane.Abstractions.Operations;
using TianShu.ControlPlane.Abstractions.Sessions;
using TianShu.ControlPlane.Abstractions.Subscriptions;
using TianShu.ControlPlane.Abstractions.Workflows;
using Task = System.Threading.Tasks.Task;

namespace TianShu.HostGateway.Tests;

public sealed class HostGatewayTests
{
    [Fact]
    public async Task InvokeAsync_MapsHostOperationIntoUnifiedControlOperation()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.UnifiedOperationResult = new ControlOperationResult(
            "host-op-001",
            ControlOperationKind.Query,
            ControlOperationStatus.Completed,
            StructuredValue.FromString("catalog-ok"));
        IHostGateway gateway = new TianShuHostGateway(controlPlane);

        var result = await gateway.InvokeAsync(
            new HostOperationRequest(
                "host-op-001",
                "cli",
                HostOperationKind.Query,
                StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["operation_name"] = StructuredValue.FromString("catalog.list"),
                })),
            CancellationToken.None);

        Assert.Equal(HostOperationStatus.Completed, result.Status);
        Assert.Equal("catalog-ok", result.Projection?.GetString());
        Assert.Equal("catalog.list", controlPlane.LastUnifiedOperationRequest?.OperationName);
        Assert.Equal("host-op-001", controlPlane.LastUnifiedOperationRequest?.OperationId);
    }

    [Fact]
    public async Task SnapshotAsync_ReadsHostProjectionSnapshotWithoutKernelInternals()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        await snapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-snapshot-001"),
            CreateThreadDelta("thread-snapshot-001", "快照线程"),
            CancellationToken.None);
        IHostGateway gateway = new TianShuHostGateway(controlPlane, snapshotStore);

        var snapshot = await gateway.SnapshotAsync(
            new HostSnapshotRequest("cli", ProjectionScopeKind.Thread, "thread-snapshot-001"),
            CancellationToken.None);

        Assert.Equal("snapshot:cli:Thread:thread-snapshot-001", snapshot.SnapshotId);
        Assert.Equal(ProjectionScopeKind.Thread, snapshot.ScopeKind);
        var payload = Assert.IsType<ThreadProjectionPayload>(Assert.Single(snapshot.Projections));
        Assert.Equal("快照线程", payload.Projection.Title);
        Assert.Empty(snapshot.KernelProjections);
    }

    [Fact]
    public async Task SnapshotAsync_ExposesRuntimeThreadStatusUsageAndDiagnosticsAsTypedProjection()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        await snapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-runtime-projection-001"),
            CreateRuntimeThreadDelta("thread-runtime-projection-001", "运行态线程"),
            CancellationToken.None);
        IHostGateway gateway = new TianShuHostGateway(controlPlane, snapshotStore);

        var snapshot = await gateway.SnapshotAsync(
            new HostSnapshotRequest("cli", ProjectionScopeKind.Thread, "thread-runtime-projection-001"),
            CancellationToken.None);

        var payload = Assert.IsType<ThreadProjectionPayload>(Assert.Single(snapshot.Projections));
        Assert.Equal("running", payload.Projection.RuntimeStatus?.Lifecycle);
        Assert.Equal("turn-runtime-projection-001", payload.Projection.ActiveTurnId?.Value);
        Assert.True(payload.Projection.HasActiveTurn);
        Assert.Equal(18, payload.Projection.TokenUsage?.Total.TotalTokens);
        Assert.False(payload.Projection.TokenUsage?.Estimated);
        var diagnostics = Assert.IsType<ThreadDiagnosticsProjection>(payload.Projection.Diagnostics);
        Assert.Contains("trace://runtime/thread-runtime-projection-001", diagnostics.RuntimeTraceRefs);
        Assert.Contains("diagnostics://runtime/thread-runtime-projection-001", diagnostics.DiagnosticsRefs);
        var contextSegment = Assert.Single(diagnostics.ContextSlicing!.Segments);
        Assert.Equal("history-1", contextSegment.SegmentId);
        Assert.Equal("dropped", contextSegment.Disposition);
        Assert.Equal("BudgetExceeded", contextSegment.DroppedReason);
        Assert.Equal("turn-log://thread-runtime-projection-001/turn-runtime-projection-001", payload.Projection.Evidence?.TurnLogRef);
        Assert.Empty(snapshot.KernelProjections);
    }

    [Fact]
    public async Task IHostGatewaySubscribeAsync_ProjectsOnlyHostViewUpdates()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        await snapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-subscribe-001"),
            CreateThreadDelta("thread-subscribe-001", "订阅线程"),
            CancellationToken.None);
        IHostGateway gateway = new TianShuHostGateway(controlPlane, snapshotStore);

        var outputs = new List<HostViewUpdate>();
        await foreach (var item in gateway.SubscribeAsync(
                           new HostSubscriptionRequest(
                               new ProjectionSubscription(
                                   new SubscriptionToken("sub-formal-001"),
                                   ProjectionScopeKind.Thread,
                                   "thread-subscribe-001")),
                           CancellationToken.None))
        {
            outputs.Add(item);
        }

        var update = Assert.Single(outputs);
        var payload = Assert.IsType<ThreadProjectionPayload>(Assert.IsType<ProjectionDelta>(update.Delta).Payload);
        Assert.Equal("订阅线程", payload.Projection.Title);
    }

    [Fact]
    public async Task SubmitTurnAsync_WhenThreadTargetProvided_ResumesThreadAndSubmitsMappedInputs()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.SubmitTurnResult = new ControlPlaneTurnSubmissionResult
        {
            Accepted = true,
            Message = "accepted",
            TurnId = new TurnId("turn-001"),
            TurnStatus = "running",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var command = new HostSubmitTurn(
            new HostInteractionEnvelope(
                new InteractionEnvelopeId("interaction-001"),
                new HostContext(HostSurfaceKind.Cli, "cli"),
                new InteractionItem[]
                {
                    new TextInteractionItem("请分析当前仓库"),
                    new MentionInteractionItem("repo", "app://workspace"),
                },
                new InteractionTarget(
                    ThreadId: new ThreadId("thread-001"))),
            [
                new ControlPlaneConversationMessage
                {
                    Role = ControlPlaneConversationRole.User,
                    Content = "历史消息",
                },
            ]);

        var result = await gateway.SubmitTurnAsync(command, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("turn-001", result.TurnId?.Value);
        Assert.Equal("running", result.TurnStatus);
        Assert.Equal("thread-001", controlPlane.Conversations.ResumeThreadCommand?.ThreadId.Value);
        Assert.Equal("历史消息", Assert.Single(controlPlane.Conversations.SubmitTurnCommand!.History).Content);
        Assert.Equal("interaction-001", controlPlane.Conversations.SubmitTurnCommand.Envelope?.Id.Value);
        Assert.Equal(InteractionSourceKind.Host, controlPlane.Conversations.SubmitTurnCommand.Envelope?.Source.Kind);
        Assert.Equal("cli", controlPlane.Conversations.SubmitTurnCommand.Envelope?.Source.Surface);
        Assert.Equal("thread-001", controlPlane.Conversations.SubmitTurnCommand.Envelope?.Target?.ThreadId?.Value);
        Assert.Collection(
            controlPlane.Conversations.SubmitTurnCommand!.Inputs,
            input => Assert.IsType<ControlPlaneTextInput>(input),
            input => Assert.IsType<ControlPlaneMentionInput>(input));
    }

    [Fact]
    public async Task SubmitFollowUpAsync_MapsInputsModeAndCorrelation()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.SubmitFollowUpResult = new ControlPlaneTurnSubmissionResult
        {
            Accepted = true,
            Message = "queued",
            TurnId = new TurnId("turn-followup-001"),
            TurnStatus = "queued",
            CorrelationId = "corr-1",
            RequestedMode = ControlPlaneFollowUpMode.Steer,
            EffectiveMode = ControlPlaneFollowUpMode.Steer,
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var result = await gateway.SubmitFollowUpAsync(
            new HostSubmitFollowUp(
                new HostInteractionEnvelope(
                    new InteractionEnvelopeId("interaction-followup-001"),
                    new HostContext(HostSurfaceKind.Vsix, "sidecar"),
                    [new TextInteractionItem("继续")]),
                HostFollowUpMode.Steer,
                "corr-1"),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("turn-followup-001", result.TurnId?.Value);
        Assert.Equal("corr-1", result.CorrelationId);
        Assert.Equal(HostFollowUpMode.Steer, result.RequestedMode);
        Assert.Equal(HostFollowUpMode.Steer, result.EffectiveMode);
        Assert.Equal("interaction-followup-001", controlPlane.Conversations.SubmitFollowUpCommand?.Envelope?.Id.Value);
        Assert.Equal("sidecar", controlPlane.Conversations.SubmitFollowUpCommand?.Envelope?.Source.Surface);
        Assert.Equal(ControlPlaneFollowUpMode.Steer, controlPlane.Conversations.SubmitFollowUpCommand?.Mode);
        Assert.Equal("corr-1", controlPlane.Conversations.SubmitFollowUpCommand?.CorrelationId);
        Assert.IsType<ControlPlaneTextInput>(Assert.Single(controlPlane.Conversations.SubmitFollowUpCommand!.Inputs));
    }

    [Fact]
    public async Task ResolveApprovalAsync_MapsDecisionIntoControlPlaneResolution()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Governance.ResolveApprovalResult = true;
        var gateway = new TianShuHostGateway(controlPlane);

        var result = await gateway.ResolveApprovalAsync(
            new HostResolveApproval(
                new CallId("call-approval-001"),
                "approve_with_execution_policy_amendment",
                new ApprovalId("approval-001"),
                commandPrefix: new[] { "git", "status" },
                note: "允许本次执行",
                context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("call-approval-001", controlPlane.Governance.ApprovalResolution?.CallId.Value);
        Assert.Equal("host-approval-call-approval-001", controlPlane.Governance.ApprovalResolution?.Envelope?.Id.Value);
        Assert.Equal("sidecar", controlPlane.Governance.ApprovalResolution?.Envelope?.Source.Surface);
        var approvalPayload = Assert.IsType<StructuredInteractionItem>(Assert.Single(controlPlane.Governance.ApprovalResolution!.Envelope!.Items));
        Assert.Equal("approval_response", approvalPayload.SemanticKind);
        Assert.Equal(ControlPlaneApprovalDecision.ApproveWithExecutionPolicyAmendment, controlPlane.Governance.ApprovalResolution?.Decision);
        Assert.Equal("git", controlPlane.Governance.ApprovalResolution?.CommandPrefix[0]);
    }

    [Fact]
    public async Task GrantPermissionAsync_MapsEnvelopeAndStructuredPayloadIntoGovernanceCommand()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Governance.ResolvePermissionResult = true;
        var gateway = new TianShuHostGateway(controlPlane);

        var result = await gateway.GrantPermissionAsync(
            new HostGrantPermission(
                new CallId("call-permission-001"),
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["network.http"] = StructuredValue.FromBoolean(true),
                },
                HostPermissionScope.Session,
                context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("call-permission-001", controlPlane.Governance.PermissionGrant?.CallId.Value);
        Assert.Equal("host-permission-call-permission-001", controlPlane.Governance.PermissionGrant?.Envelope?.Id.Value);
        Assert.Equal("sidecar", controlPlane.Governance.PermissionGrant?.Envelope?.Source.Surface);
        var permissionPayload = Assert.IsType<StructuredInteractionItem>(Assert.Single(controlPlane.Governance.PermissionGrant!.Envelope!.Items));
        Assert.Equal("permission_response", permissionPayload.SemanticKind);
        Assert.Equal(ControlPlanePermissionScope.Session, controlPlane.Governance.PermissionGrant?.Scope);
    }

    [Fact]
    public async Task SubmitUserInputAsync_MapsStructuredAnswersIntoGovernanceCommand()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Governance.SubmitUserInputResult = true;
        var gateway = new TianShuHostGateway(controlPlane);

        var result = await gateway.SubmitUserInputAsync(
            new HostSubmitUserInput(
                new CallId("call-input-001"),
                new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["summary"] = StructuredValue.FromString("继续执行"),
                },
                new UserInputRequestId("request-001"),
                context: new HostContext(HostSurfaceKind.Vsix, "sidecar")),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("call-input-001", controlPlane.Governance.UserInputSubmission?.CallId.Value);
        Assert.Equal("host-userinput-call-input-001", controlPlane.Governance.UserInputSubmission?.Envelope?.Id.Value);
        Assert.Equal("sidecar", controlPlane.Governance.UserInputSubmission?.Envelope?.Source.Surface);
        var inputPayload = Assert.IsType<StructuredInteractionItem>(Assert.Single(controlPlane.Governance.UserInputSubmission!.Envelope!.Items));
        Assert.Equal("user_input_submission", inputPayload.SemanticKind);
        Assert.Equal("继续执行", controlPlane.Governance.UserInputSubmission?.Answers["summary"].GetString());
    }

    [Fact]
    public async Task ListThreadsAsync_MapsQueryIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.ListThreadsResult = new ControlPlaneThreadListResult
        {
            Threads =
            [
                new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-list-001"),
                    Preview = "线程列表",
                    UpdatedAt = new DateTimeOffset(2026, 4, 28, 1, 0, 0, TimeSpan.Zero),
                },
            ],
            NextCursor = "cursor-2",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var result = await gateway.ListThreadsAsync(
            new HostListThreadsQuery
            {
                Limit = 5,
                Cursor = "cursor-1",
                Archived = true,
                WorkingDirectory = @"D:\repo",
                SortKey = "updated_at",
                ModelProviders = ["openai"],
                SourceKinds = [ControlPlaneThreadSourceKind.Cli],
                SearchTerm = "thread",
            },
            CancellationToken.None);

        Assert.Equal(5, controlPlane.Conversations.ListThreadsQuery?.Limit);
        Assert.Equal("cursor-1", controlPlane.Conversations.ListThreadsQuery?.Cursor);
        Assert.True(controlPlane.Conversations.ListThreadsQuery?.Archived);
        Assert.Equal("thread-list-001", Assert.Single(result.Threads).ThreadId.Value);
        Assert.Equal("cursor-2", result.NextCursor);
    }

    [Fact]
    public async Task ListLoadedThreadsAsync_MapsTypedQueryIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.ListLoadedThreadsResult = new ControlPlaneLoadedThreadListResult
        {
            ThreadIds = [new ThreadId("thread-loaded-001"), new ThreadId("thread-loaded-002")],
            NextCursor = "loaded-cursor-2",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var result = await gateway.ListLoadedThreadsAsync(
            new HostListLoadedThreadsQuery
            {
                Limit = 2,
                Cursor = "loaded-cursor-1",
            },
            CancellationToken.None);

        Assert.Equal(2, controlPlane.Conversations.ListLoadedThreadsQuery?.Limit);
        Assert.Equal("loaded-cursor-1", controlPlane.Conversations.ListLoadedThreadsQuery?.Cursor);
        Assert.Equal(["thread-loaded-001", "thread-loaded-002"], result.ThreadIds.Select(static item => item.Value).ToArray());
        Assert.Equal("loaded-cursor-2", result.NextCursor);
    }

    [Fact]
    public async Task StartThreadAsync_AndInterruptTurnAsync_MapIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.StartThreadResult = new ControlPlaneThreadSummary
        {
            ThreadId = new ThreadId("thread-start-001"),
            Preview = "started",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var started = await gateway.StartThreadAsync(
            new HostStartThread
            {
                Model = "gpt-5",
                ModelProvider = "openai",
                PersistExtendedHistory = true,
            },
            CancellationToken.None);
        var interrupted = await gateway.InterruptTurnAsync(new HostInterruptTurn(), CancellationToken.None);

        Assert.Equal("gpt-5", controlPlane.Conversations.StartThreadCommand?.Model);
        Assert.Equal("openai", controlPlane.Conversations.StartThreadCommand?.ModelProvider);
        Assert.True(controlPlane.Conversations.StartThreadCommand?.PersistExtendedHistory);
        Assert.True(controlPlane.Conversations.InterruptTurnInvoked);
        Assert.Equal("thread-start-001", started.Thread?.ThreadId.Value);
        Assert.True(interrupted.Accepted);
    }

    [Fact]
    public async Task RenameArchiveDeleteThreadAsync_MapIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane
        {
            Conversations =
            {
                RenameThreadResult = true,
                ArchiveThreadResult = true,
                DeleteThreadResult = true,
            },
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var renamed = await gateway.RenameThreadAsync(
            new HostRenameThread
            {
                ThreadId = new ThreadId("thread-rename-001"),
                Name = "新的线程名",
            },
            CancellationToken.None);
        var archived = await gateway.ArchiveThreadAsync(
            new HostArchiveThread
            {
                ThreadId = new ThreadId("thread-rename-001"),
            },
            CancellationToken.None);
        var deleted = await gateway.DeleteThreadAsync(
            new HostDeleteThread
            {
                ThreadId = new ThreadId("thread-rename-001"),
            },
            CancellationToken.None);

        Assert.Equal("thread-rename-001", controlPlane.Conversations.RenameThreadCommand?.ThreadId.Value);
        Assert.Equal("新的线程名", controlPlane.Conversations.RenameThreadCommand?.Name);
        Assert.Equal("thread-rename-001", controlPlane.Conversations.ArchiveThreadCommand?.ThreadId.Value);
        Assert.Equal("thread-rename-001", controlPlane.Conversations.DeleteThreadCommand?.ThreadId.Value);
        Assert.True(renamed.Accepted);
        Assert.True(archived.Accepted);
        Assert.True(deleted.Accepted);
    }

    [Fact]
    public async Task ResumeThreadAsync_MapsTypedCommandAndPreservesPendingInputState()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.ResumeThreadResult = new ControlPlaneThreadSnapshot
        {
            Thread = new ControlPlaneThreadSummary
            {
                ThreadId = new ThreadId("thread-resume-001"),
                Preview = "resume",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            PendingInputState = new ControlPlanePendingInputState(
                Entries:
                [
                    new ControlPlanePendingInputStateEntry(
                        "corr-1",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-1",
                        null,
                        StructuredValue.FromString("compare")),
                ],
                InterruptRequestPending: false),
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var result = await gateway.ResumeThreadAsync(
            new HostResumeThread
            {
                ThreadId = new ThreadId("thread-resume-001"),
                ModelProvider = "openai",
                ApprovalPolicy = "on-request",
                PersistExtendedHistory = true,
            },
            CancellationToken.None);

        Assert.Equal("thread-resume-001", controlPlane.Conversations.ResumeThreadCommand?.ThreadId.Value);
        Assert.Equal("openai", controlPlane.Conversations.ResumeThreadCommand?.ModelProvider);
        Assert.True(controlPlane.Conversations.ResumeThreadCommand?.PersistExtendedHistory);
        Assert.Equal("thread-resume-001", result.Snapshot?.Thread.ThreadId.Value);
        Assert.Equal("corr-1", Assert.Single(result.Snapshot!.PendingInputState!.Entries).CorrelationId);
    }

    [Fact]
    public async Task ForkThreadAsync_MapsTypedCommand()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.ForkThreadResult = new ControlPlaneThreadSummary
        {
            ThreadId = new ThreadId("thread-fork-001"),
            Preview = "fork",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var result = await gateway.ForkThreadAsync(
            new HostForkThread
            {
                ThreadId = new ThreadId("thread-source-001"),
                ModelProvider = "anthropic",
                Ephemeral = true,
                PersistExtendedHistory = true,
            },
            CancellationToken.None);

        Assert.Equal("thread-source-001", controlPlane.Conversations.ForkThreadCommand?.ThreadId.Value);
        Assert.Equal("anthropic", controlPlane.Conversations.ForkThreadCommand?.ModelProvider);
        Assert.True(controlPlane.Conversations.ForkThreadCommand?.Ephemeral);
        Assert.Equal("thread-fork-001", result.Thread?.ThreadId.Value);
    }

    [Fact]
    public async Task ReadThreadAsync_AndUpdateThreadMetadataAsync_MapIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.ReadThreadResult = new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = new ThreadId("thread-read-001"),
                Preview = "read",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };
        controlPlane.Conversations.UpdateThreadMetadataResult = new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = new ThreadId("thread-read-001"),
                Preview = "updated",
                GitSha = "abc123",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var readResult = await gateway.ReadThreadAsync(
            new HostReadThreadQuery
            {
                ThreadId = new ThreadId("thread-read-001"),
                IncludeTurns = true,
            },
            CancellationToken.None);
        var updateResult = await gateway.UpdateThreadMetadataAsync(
            new HostUpdateThreadMetadata
            {
                ThreadId = new ThreadId("thread-read-001"),
                HasGitSha = true,
                GitSha = "abc123",
            },
            CancellationToken.None);

        Assert.Equal("thread-read-001", controlPlane.Conversations.ReadThreadQuery?.ThreadId.Value);
        Assert.True(controlPlane.Conversations.ReadThreadQuery?.IncludeTurns);
        Assert.Equal("thread-read-001", controlPlane.Conversations.UpdateThreadMetadataCommand?.ThreadId.Value);
        Assert.Equal("abc123", controlPlane.Conversations.UpdateThreadMetadataCommand?.GitSha);
        Assert.Equal("thread-read-001", readResult.Thread?.ThreadId.Value);
        Assert.Equal("abc123", updateResult.Thread?.GitSha);
    }

    [Fact]
    public async Task UnarchiveThreadAsync_AndRollbackThreadAsync_MapIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.UnarchiveThreadResult = new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = new ThreadId("thread-life-001"),
                Preview = "unarchived",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };
        controlPlane.Conversations.RollbackThreadResult = new ControlPlaneThreadOperationResult
        {
            Thread = new ControlPlaneThreadDetail
            {
                ThreadId = new ThreadId("thread-life-001"),
                Preview = "rolled-back",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var unarchived = await gateway.UnarchiveThreadAsync(
            new HostUnarchiveThread
            {
                ThreadId = new ThreadId("thread-life-001"),
            },
            CancellationToken.None);
        var rolledBack = await gateway.RollbackThreadAsync(
            new HostRollbackThread
            {
                ThreadId = new ThreadId("thread-life-001"),
                NumTurns = 2,
            },
            CancellationToken.None);

        Assert.Equal("thread-life-001", controlPlane.Conversations.UnarchiveThreadCommand?.ThreadId.Value);
        Assert.Equal("thread-life-001", controlPlane.Conversations.RollbackThreadCommand?.ThreadId.Value);
        Assert.Equal(2, controlPlane.Conversations.RollbackThreadCommand?.NumTurns);
        Assert.Equal("thread-life-001", unarchived.Thread?.ThreadId.Value);
        Assert.Equal("thread-life-001", rolledBack.Thread?.ThreadId.Value);
    }

    [Fact]
    public async Task CompactCleanAndUnsubscribeThreadAsync_MapIntoConversationControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.UnsubscribeThreadResult = new ControlPlaneThreadUnsubscribeResult
        {
            Status = "unsubscribed",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var compacted = await gateway.CompactThreadAsync(
            new HostCompactThread
            {
                ThreadId = new ThreadId("thread-maintain-001"),
                KeepRecentTurns = 3,
            },
            CancellationToken.None);
        var cleaned = await gateway.CleanBackgroundTerminalsAsync(
            new HostCleanBackgroundTerminals
            {
                ThreadId = new ThreadId("thread-maintain-001"),
            },
            CancellationToken.None);
        var unsubscribed = await gateway.UnsubscribeThreadAsync(
            new HostUnsubscribeThread
            {
                ThreadId = new ThreadId("thread-maintain-001"),
            },
            CancellationToken.None);

        Assert.Equal("thread-maintain-001", controlPlane.Conversations.CompactThreadCommand?.ThreadId.Value);
        Assert.Equal(3, controlPlane.Conversations.CompactThreadCommand?.KeepRecentTurns);
        Assert.Equal("thread-maintain-001", controlPlane.Conversations.CleanBackgroundTerminalsCommand?.ThreadId.Value);
        Assert.Equal("thread-maintain-001", controlPlane.Conversations.UnsubscribeThreadCommand?.ThreadId.Value);
        Assert.True(compacted.Accepted);
        Assert.True(cleaned.Accepted);
        Assert.Equal("unsubscribed", unsubscribed.Status);
    }

    [Fact]
    public async Task ArtifactReadMethods_MapIntoArtifactControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Artifacts.ConversationSummaryResult = new ControlPlaneConversationArtifact
        {
            ConversationId = "conv-001",
            GitSha = "abc123",
        };
        controlPlane.Artifacts.GitDiffResult = new ControlPlaneGitDiffArtifact
        {
            HasChanges = true,
            Diff = "diff --git a/src b/src",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var summary = await gateway.GetConversationSummaryAsync(
            new HostReadConversationSummaryQuery
            {
                ThreadId = new ThreadId("thread-artifact-001"),
                RolloutPath = @"D:\repo\.tianshu\rollouts\thread-artifact-001.jsonl",
            },
            CancellationToken.None);
        var diff = await gateway.GetGitDiffToRemoteAsync(
            new HostReadGitDiffToRemoteQuery
            {
                ThreadId = new ThreadId("thread-artifact-001"),
            },
            CancellationToken.None);

        Assert.Equal("thread-artifact-001", controlPlane.Artifacts.ConversationSummaryQuery?.ThreadId?.Value);
        Assert.Equal(@"D:\repo\.tianshu\rollouts\thread-artifact-001.jsonl", controlPlane.Artifacts.ConversationSummaryQuery?.RolloutPath);
        Assert.Equal("thread-artifact-001", controlPlane.Artifacts.GitDiffQuery?.ThreadId.Value);
        Assert.Equal("conv-001", summary.Artifact?.ConversationId);
        Assert.True(diff.Artifact.HasChanges);
    }

    [Fact]
    public async Task CatalogAndAgentMethods_MapIntoControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Catalog.CapabilityCatalogResult = new CapabilityCatalogSnapshot(
            activeProviderKey: "openai",
            activeModel: "gpt-5.4");
        controlPlane.Catalog.EngineBindingResult = new ResolvedEngineBinding(
            new EngineBinding(
                "responses",
                "openai",
                "gpt-5.4",
                "gpt-5.4",
                "responses.websocket",
                new CatalogStreamingPreference(
                    "responses.websocket",
                    preferWebsocketTransport: true,
                    useWebsocketTransport: true),
                supportsWebsockets: true));
        controlPlane.Agents.AgentListResult = new ControlPlaneAgentRosterResult
        {
            Agents =
            [
                new ControlPlaneAgentDescriptor
                {
                    ThreadId = new ThreadId("agent-thread-host"),
                    Preview = "host agent",
                    AgentNickname = "Host Agent",
                    AgentRole = "reviewer",
                    Status = "running",
                    UpdatedAt = new DateTimeOffset(2026, 5, 5, 1, 0, 0, TimeSpan.Zero),
                },
            ],
            NextCursor = "agent-next",
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var catalog = await gateway.GetCapabilityCatalogAsync(
            new HostGetCapabilityCatalogQuery
            {
                WorkspacePath = @"D:\repo\tianshu",
                IncludeHiddenModels = true,
                ModelLimit = 42,
            },
            CancellationToken.None);
        var binding = await gateway.ResolveEngineBindingAsync(
            new HostResolveEngineBindingQuery
            {
                WorkspacePath = @"D:\repo\tianshu",
                PreferredProviderKey = "openai",
                PreferredModelKey = "gpt-5.4",
                ReasoningEffort = "high",
                ReasoningSummary = "auto",
                Verbosity = "medium",
                PreferWebsocketTransport = true,
            },
            CancellationToken.None);
        var agents = await gateway.ListAgentsAsync(
            new HostListAgentsQuery
            {
                Limit = 5,
                Cursor = "agent-cursor",
                IncludePrimaryThreads = true,
            },
            CancellationToken.None);

        Assert.Equal(@"D:\repo\tianshu", controlPlane.Catalog.CapabilityCatalogQuery?.WorkspacePath);
        Assert.True(controlPlane.Catalog.CapabilityCatalogQuery?.IncludeHiddenModels);
        Assert.Equal(42, controlPlane.Catalog.CapabilityCatalogQuery?.ModelLimit);
        Assert.Equal("openai", catalog.Catalog.ActiveProviderKey);
        Assert.Equal("gpt-5.4", catalog.Catalog.ActiveModel);

        Assert.Equal(@"D:\repo\tianshu", controlPlane.Catalog.EngineBindingQuery?.WorkspacePath);
        Assert.Equal("openai", controlPlane.Catalog.EngineBindingQuery?.PreferredProviderKey);
        Assert.Equal("gpt-5.4", controlPlane.Catalog.EngineBindingQuery?.PreferredModelKey);
        Assert.Equal("high", controlPlane.Catalog.EngineBindingQuery?.ReasoningEffort);
        Assert.Equal("auto", controlPlane.Catalog.EngineBindingQuery?.ReasoningSummary);
        Assert.Equal("medium", controlPlane.Catalog.EngineBindingQuery?.Verbosity);
        Assert.True(controlPlane.Catalog.EngineBindingQuery?.PreferWebsocketTransport);
        Assert.Equal("responses", binding.Resolution.Binding?.EngineKey);

        Assert.Equal(5, controlPlane.Agents.AgentListQuery?.Limit);
        Assert.Equal("agent-cursor", controlPlane.Agents.AgentListQuery?.Cursor);
        Assert.True(controlPlane.Agents.AgentListQuery?.IncludePrimaryThreads);
        Assert.Equal("agent-next", agents.NextCursor);
        var agent = Assert.Single(agents.Agents);
        Assert.Equal("agent-thread-host", agent.ThreadId.Value);
    }

    [Fact]
    public async Task DiagnosticsMethods_MapIntoDiagnosticsControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Diagnostics.ExecutionTraceResult = new ExecutionTrace(
            new ExecutionTraceId("trace-host-001"),
            new ExecutionId("execution-host-001"));
        controlPlane.Diagnostics.AttemptSummariesResult =
        [
            new AttemptSummary(
                new ExecutionId("execution-host-001"),
                AttemptNumber: 1,
                Succeeded: true,
                StartedAt: new DateTimeOffset(2026, 5, 4, 2, 0, 0, TimeSpan.Zero),
                CompletedAt: new DateTimeOffset(2026, 5, 4, 2, 1, 0, TimeSpan.Zero)),
        ];

        var gateway = new TianShuHostGateway(controlPlane);
        var trace = await gateway.GetExecutionTraceAsync(
            new HostReadExecutionTraceQuery
            {
                TraceId = new ExecutionTraceId("trace-host-001"),
            },
            CancellationToken.None);
        var attempts = await gateway.ListAttemptSummariesAsync(
            new HostListAttemptSummariesQuery
            {
                ExecutionId = new ExecutionId("execution-host-001"),
            },
            CancellationToken.None);

        Assert.Equal("trace-host-001", controlPlane.Diagnostics.ExecutionTraceQuery?.TraceId.Value);
        Assert.Equal("execution-host-001", controlPlane.Diagnostics.AttemptSummariesQuery?.ExecutionId.Value);
        Assert.Equal("trace-host-001", trace.Trace?.Id.Value);
        Assert.Single(attempts.Attempts);
        Assert.Equal(1, attempts.Attempts[0].AttemptNumber);
    }

    [Fact]
    public async Task UploadFeedbackAsync_MapsIntoDiagnosticsControlPlane()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var gateway = new TianShuHostGateway(controlPlane);

        var result = await gateway.UploadFeedbackAsync(
            new HostUploadFeedback
            {
                Classification = "bug",
                IncludeLogs = true,
                ThreadId = "thread-feedback-001",
                Reason = "host-gateway-test",
                ExtraLogFiles = ["./host.log"],
            },
            CancellationToken.None);

        Assert.Equal("bug", controlPlane.Diagnostics.FeedbackUploadCommand?.Classification);
        Assert.True(controlPlane.Diagnostics.FeedbackUploadCommand?.IncludeLogs);
        Assert.Equal("thread-feedback-001", controlPlane.Diagnostics.FeedbackUploadCommand?.ThreadId);
        Assert.Equal("host-gateway-test", controlPlane.Diagnostics.FeedbackUploadCommand?.Reason);
        Assert.Equal("./host.log", Assert.Single(controlPlane.Diagnostics.FeedbackUploadCommand?.ExtraLogFiles ?? []));
        Assert.Equal("thread-feedback-001", result.ThreadId);
    }

    [Fact]
    public async Task SubscribeConversationStreamAsync_MapsTypedStreamEvents()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Conversations.StreamEvents =
        [
            new ControlPlaneConversationStreamEvent
            {
                Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
                ThreadId = new ThreadId("thread-stream-001"),
                Text = "hello",
            },
        ];

        var gateway = new TianShuHostGateway(controlPlane);
        var events = new List<ControlPlaneConversationStreamEvent>();

        await foreach (var item in gateway.SubscribeConversationStreamAsync(
                           new HostConversationStreamSubscription
                           {
                               ThreadId = new ThreadId("thread-stream-001"),
                           },
                           CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal("thread-stream-001", controlPlane.Conversations.StreamThreadId?.Value);
        Assert.Single(events);
        Assert.Equal(ControlPlaneConversationStreamEventKind.AssistantTextDelta, events[0].Kind);
        Assert.Equal("hello", events[0].Text);
    }

    [Fact]
    public async Task SubscribeAsync_WhenGovernanceProjectionEventArrives_ProjectsHostNotification()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Subscriptions.GovernanceEvents = new[]
        {
            new ControlPlaneProjectionEvent
            {
                Kind = "approval_requested",
                Timestamp = DateTimeOffset.UtcNow,
                CallId = new CallId("call-approval-002"),
                Message = "需要审批",
                Payload = StructuredValue.FromObject(
                    new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["title"] = StructuredValue.FromString("审批"),
                    }),
            },
        };

        var gateway = new TianShuHostGateway(controlPlane);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-approval-001"),
                ProjectionScopeKind.ApprovalQueue,
                "approvals-missing"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        Assert.Equal(2, outputs.Count);
        var initialReset = Assert.IsType<ProjectionReset>(Assert.IsType<HostViewUpdate>(outputs[0].ViewUpdate).Reset);
        Assert.Equal("approval_queue_uninitialized", initialReset.Reason);

        var notification = outputs[1].Notification;
        Assert.NotNull(notification);
        Assert.Equal(HostNotificationKind.ApprovalRequested, notification!.Kind);
        Assert.Equal("call-approval-002", notification.RelatedCallId?.Value);
        Assert.Equal("approval_requested", notification.PayloadKind);
        Assert.Equal("审批", notification.Payload?.GetProperty("title").GetString());
    }

    [Fact]
    public async Task SubscribeAsync_WhenThreadSnapshotExists_EmitsInitialHostViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var delta = CreateThreadDelta("thread-view-001", "当前线程");
        await snapshotStore.UpsertAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-view-001"),
            delta,
            CancellationToken.None);

        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-thread-view-001"),
                ProjectionScopeKind.Thread,
                "thread-view-001"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        Assert.Null(output.Notification);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var threadPayload = Assert.IsType<ThreadProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("thread-view-001", threadPayload.Projection.ThreadId.Value);
        Assert.Equal("当前线程", threadPayload.Projection.Title);
    }

    [Fact]
    public async Task SubscribeAsync_WhenThreadTypedDeltaArrives_UpdatesSnapshotAndEmitsHostViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Subscriptions.ThreadEvents = new[]
        {
            new ControlPlaneProjectionEvent
            {
                Kind = "ProjectionDelta",
                Timestamp = DateTimeOffset.UtcNow,
                ThreadId = new ThreadId("thread-view-002"),
                Delta = CreateThreadDelta("thread-view-002", "运行中线程"),
            },
        };

        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-thread-view-002"),
                ProjectionScopeKind.Thread,
                "thread-view-002"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        Assert.Equal(2, outputs.Count);
        var initialReset = Assert.IsType<ProjectionReset>(Assert.IsType<HostViewUpdate>(outputs[0].ViewUpdate).Reset);
        Assert.Equal("thread_projection_unavailable", initialReset.Reason);

        var viewUpdate = Assert.IsType<HostViewUpdate>(outputs[1].ViewUpdate);
        var threadPayload = Assert.IsType<ThreadProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("运行中线程", threadPayload.Projection.Title);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-view-002"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("运行中线程", Assert.IsType<ThreadProjectionPayload>(snapshot!.Delta!.Payload).Projection.Title);
    }

    [Fact]
    public async Task SubscribeAsync_WhenApprovalQueueTypedResetArrives_UpdatesSnapshotAndEmitsHostViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Subscriptions.GovernanceEvents = new[]
        {
            new ControlPlaneProjectionEvent
            {
                Kind = "ProjectionReset",
                Timestamp = DateTimeOffset.UtcNow,
                Reset = new ProjectionReset(
                    ProjectionScopeKind.ApprovalQueue,
                    "approvals-missing",
                    "queue_cleared",
                    new ProjectionCursor("cursor-reset-approval-001")),
            },
        };

        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-approval-view-001"),
                ProjectionScopeKind.ApprovalQueue,
                "approvals-missing"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        Assert.Equal(2, outputs.Count);
        var initialReset = Assert.IsType<ProjectionReset>(Assert.IsType<HostViewUpdate>(outputs[0].ViewUpdate).Reset);
        Assert.Equal("approval_queue_uninitialized", initialReset.Reason);

        var viewUpdate = Assert.IsType<HostViewUpdate>(outputs[1].ViewUpdate);
        Assert.Equal("queue_cleared", Assert.IsType<ProjectionReset>(viewUpdate.Reset).Reason);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.ApprovalQueue, "approvals-missing"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("queue_cleared", snapshot!.Reset!.Reason);
    }

    [Fact]
    public async Task SubscribeAsync_WhenCollaborationSpaceScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-collaboration-view-001"),
                ProjectionScopeKind.CollaborationSpace,
                "space-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var payload = Assert.IsType<CollaborationSpaceProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("space-host", payload.Projection.CollaborationSpace.Id.Value);
        Assert.Equal("space-host", payload.Projection.CollaborationSpace.DisplayName);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.CollaborationSpace, "space-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("space-host", Assert.IsType<CollaborationSpaceProjectionPayload>(snapshot!.Delta!.Payload).Projection.CollaborationSpace.DisplayName);
    }

    [Fact]
    public async Task SubscribeAsync_WhenParticipantScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-participant-view-001"),
                ProjectionScopeKind.Participant,
                "agent-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var payload = Assert.IsType<ParticipantProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("agent-host", payload.Projection.Participant.Id.Value);
        Assert.Equal("owner", payload.Projection.Role);
        Assert.True(payload.Projection.IsActive);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Participant, "agent-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal("owner", Assert.IsType<ParticipantProjectionPayload>(snapshot!.Delta!.Payload).Projection.Role);
    }

    [Theory]
    [InlineData(ProjectionScopeKind.Thread, "thread-missing", "thread_projection_unavailable")]
    [InlineData(ProjectionScopeKind.ApprovalQueue, "approvals-missing", "approval_queue_uninitialized")]
    [InlineData(ProjectionScopeKind.WorkflowBoard, "workflow-missing", "workflow_board_uninitialized")]
    [InlineData(ProjectionScopeKind.TaskBoard, "workflow-missing", "task_board_uninitialized")]
    [InlineData(ProjectionScopeKind.Plan, "workflow-missing", "plan_projection_uninitialized")]
    [InlineData(ProjectionScopeKind.Team, "team-missing", "team_projection_unavailable")]
    [InlineData(ProjectionScopeKind.Artifact, "artifact-missing", "artifact_snapshot_unavailable")]
    [InlineData(ProjectionScopeKind.ArtifactCollection, "space-missing", "artifact_collection_snapshot_unavailable")]
    public async Task SubscribeAsync_WhenFormalScopeNotYetMaterialized_EmitsFormalResetInsteadOfUnsupportedNotification(
        ProjectionScopeKind scopeKind,
        string scopeKey,
        string expectedReason)
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken($"sub-{scopeKind}-{scopeKey}"),
                scopeKind,
                scopeKey));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        Assert.Null(output.Notification);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var reset = Assert.IsType<ProjectionReset>(viewUpdate.Reset);
        Assert.Equal(scopeKind, reset.ScopeKind);
        Assert.Equal(scopeKey, reset.ScopeKey);
        Assert.Equal(expectedReason, reset.Reason);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(scopeKind, scopeKey),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        Assert.Equal(expectedReason, snapshot!.Reset!.Reason);
    }

    [Fact]
    public async Task SubscribeAsync_WhenThreadScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-thread-view-formal-001"),
                ProjectionScopeKind.Thread,
                "thread-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<ThreadProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        Assert.Equal("thread-host", payload.Projection.ThreadId.Value);
        Assert.Equal("Host Thread", payload.Projection.Title);
        Assert.True(payload.Projection.HasActiveTurn);
    }

    [Fact]
    public async Task SubscribeAsync_WhenApprovalQueueScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-approval-queue-view-001"),
                ProjectionScopeKind.ApprovalQueue,
                "approvals"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<ApprovalQueueProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        var approvalItem = Assert.Single(payload.Projection.Items);
        Assert.Equal("approval-host", approvalItem.ApprovalId.Value);
        Assert.Equal("Host Approval", approvalItem.Title);
    }

    [Fact]
    public async Task SubscribeAsync_WhenAgentRosterScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-agent-roster-view-001"),
                ProjectionScopeKind.AgentRoster,
                "agents"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var payload = Assert.IsType<AgentRosterProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        var agent = Assert.Single(payload.Projection.Items);
        Assert.Equal("agent-thread-host", agent.AgentId.Value);
        Assert.Equal("Host Agent", agent.Participant.DisplayName);
        Assert.Equal("reviewer", agent.Role);
        Assert.True(agent.IsBusy);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.AgentRoster, "agents"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        var snapshotPayload = Assert.IsType<AgentRosterProjectionPayload>(snapshot!.Delta!.Payload);
        Assert.Equal("Host Agent", Assert.Single(snapshotPayload.Projection.Items).Participant.DisplayName);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTeamScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-team-view-001"),
                ProjectionScopeKind.Team,
                "team-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var viewUpdate = Assert.IsType<HostViewUpdate>(output.ViewUpdate);
        var payload = Assert.IsType<TeamProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("team-host", payload.Projection.Team.Id.Value);
        Assert.Equal("Host Team", payload.Projection.Team.DisplayName);
        Assert.Equal("Host Agent", Assert.Single(payload.Projection.Members).DisplayName);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Team, "team-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        var snapshotPayload = Assert.IsType<TeamProjectionPayload>(snapshot!.Delta!.Payload);
        Assert.Equal("Host Team", snapshotPayload.Projection.Team.DisplayName);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTeamTypedDeltaArrivesFromAgentSubscription_UpdatesSnapshotAndEmitsHostViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Subscriptions.AgentEvents =
        [
            new ControlPlaneProjectionEvent
            {
                Kind = "ProjectionDelta",
                Timestamp = DateTimeOffset.UtcNow,
                Delta = new ProjectionDelta(
                    new TeamProjectionPayload(
                        new TeamProjection(
                            new Team(
                                new TeamId("team-host"),
                                "Host Team Updated",
                                [new AgentId("agent-thread-host")]),
                            [
                                new Agent(
                                    new AgentId("agent-thread-host"),
                                    new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent"),
                                    "Host Agent",
                                    AgentRole.Reviewer),
                            ]))),
            },
        ];

        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-team-stream-001"),
                ProjectionScopeKind.Team,
                "team-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        Assert.Equal(2, outputs.Count);
        var viewUpdate = Assert.IsType<HostViewUpdate>(outputs[1].ViewUpdate);
        var payload = Assert.IsType<TeamProjectionPayload>(Assert.IsType<ProjectionDelta>(viewUpdate.Delta).Payload);
        Assert.Equal("Host Team Updated", payload.Projection.Team.DisplayName);
        Assert.Null(controlPlane.Subscriptions.LastAgentSubscription?.ThreadId);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Team, "team-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        var snapshotPayload = Assert.IsType<TeamProjectionPayload>(snapshot!.Delta!.Payload);
        Assert.Equal("Host Team Updated", snapshotPayload.Projection.Team.DisplayName);
    }

    [Fact]
    public async Task SubscribeAsync_WhenWorkflowDeltaTargetsDifferentWorkflow_IgnoresTypedProjectionEvent()
    {
        var controlPlane = new FakeTianShuControlPlane();
        controlPlane.Subscriptions.WorkflowEvents =
        [
            new ControlPlaneProjectionEvent
            {
                Kind = "ProjectionDelta",
                Timestamp = DateTimeOffset.UtcNow,
                Delta = new ProjectionDelta(
                    new WorkflowBoardProjectionPayload(
                        new WorkflowBoardProjection(
                            new WorkflowId("workflow-other"),
                            "Other Workflow",
                            new CollaborationSpaceRef(new CollaborationSpaceId("space-other"), "space-other", "Other Space"),
                            0,
                            1,
                            0))),
            },
        ];

        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-workflow-scope-safe-001"),
                ProjectionScopeKind.WorkflowBoard,
                "workflow-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        Assert.Single(outputs);
        var initialPayload = Assert.IsType<WorkflowBoardProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(outputs[0].ViewUpdate).Delta).Payload);
        Assert.Equal("Host Workflow", initialPayload.Projection.DisplayName);
        Assert.Null(controlPlane.Subscriptions.LastWorkflowSubscription?.ThreadId);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.WorkflowBoard, "workflow-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        var snapshotPayload = Assert.IsType<WorkflowBoardProjectionPayload>(snapshot!.Delta!.Payload);
        Assert.Equal("Host Workflow", snapshotPayload.Projection.DisplayName);
    }

    [Fact]
    public async Task SubscribeAsync_WhenWorkflowBoardScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-workflow-board-view-001"),
                ProjectionScopeKind.WorkflowBoard,
                "workflow-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<WorkflowBoardProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        Assert.Equal("workflow-host", payload.Projection.WorkflowId.Value);
        Assert.Equal("Host Workflow", payload.Projection.DisplayName);
        Assert.Equal(1, payload.Projection.RunningTaskCount);
    }

    [Fact]
    public async Task SubscribeAsync_WhenTaskBoardScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-task-board-view-001"),
                ProjectionScopeKind.TaskBoard,
                "workflow-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<TaskBoardProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        var task = Assert.Single(payload.Projection.Items);
        Assert.Equal("task-host", task.TaskId.Value);
        Assert.Equal("running", task.State);
    }

    [Fact]
    public async Task SubscribeAsync_WhenPlanScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-plan-view-001"),
                ProjectionScopeKind.Plan,
                "workflow-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<PlanProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        Assert.Equal("workflow-host", payload.Projection.WorkflowId.Value);
        Assert.Equal("Host Plan", payload.Projection.Plan.Title);
        Assert.Equal("Host Plan Step", Assert.Single(payload.Projection.Plan.Steps).Title);

        var snapshot = await snapshotStore.GetAsync(
            new ProjectionSnapshotKey(ProjectionScopeKind.Plan, "workflow-host"),
            CancellationToken.None);
        Assert.NotNull(snapshot);
        var snapshotPayload = Assert.IsType<PlanProjectionPayload>(snapshot!.Delta!.Payload);
        Assert.Equal("workflow-host", snapshotPayload.Projection.WorkflowId.Value);
        Assert.Equal("Host Plan Step", Assert.Single(snapshotPayload.Projection.Plan.Steps).Title);
    }

    [Fact]
    public async Task SubscribeAsync_WhenArtifactScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-artifact-view-001"),
                ProjectionScopeKind.Artifact,
                "artifact-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<ArtifactProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        Assert.Equal("artifact-host", payload.Projection.ArtifactId.Value);
        Assert.Equal("Host Artifact", payload.Projection.Name);
    }

    [Fact]
    public async Task SubscribeAsync_WhenArtifactCollectionScopeRequested_EmitsInitialTypedViewUpdate()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var snapshotStore = new InMemoryProjectionSnapshotStore();
        var gateway = new TianShuHostGateway(controlPlane, snapshotStore);
        var request = new HostSubscriptionRequest(
            new ProjectionSubscription(
                new SubscriptionToken("sub-artifact-collection-view-001"),
                ProjectionScopeKind.ArtifactCollection,
                "space-host"));

        var outputs = new List<HostOutputEvent>();
        await foreach (var item in gateway.SubscribeAsync(request, CancellationToken.None))
        {
            outputs.Add(item);
        }

        var output = Assert.Single(outputs);
        var payload = Assert.IsType<ArtifactCollectionProjectionPayload>(Assert.IsType<ProjectionDelta>(Assert.IsType<HostViewUpdate>(output.ViewUpdate).Delta).Payload);
        var artifactItem = Assert.Single(payload.Projection.Items);
        Assert.Equal("artifact-host", artifactItem.ArtifactId.Value);
        Assert.Equal("Host Artifact", artifactItem.Name);
    }

    [Fact]
    public async Task FakeTianShuControlPlane_ClosedSiblingSurfaces_ShouldReturnTypedDefaults()
    {
        var controlPlane = new FakeTianShuControlPlane();
        var collaborationId = new CollaborationSpaceId("space-host");
        var participantId = new ParticipantId("agent-host");
        var accountId = new AccountId("account-host");

        var created = await controlPlane.Collaboration.CreateSpaceAsync(
            new CreateCollaborationSpace(
                collaborationId,
                "host-space",
                "Host Space",
                new CollaborationSpaceProfile("宿主协作"),
                CollaborationDefaultSet.Empty),
            CancellationToken.None);
        var configured = await controlPlane.Collaboration.ConfigureSpaceAsync(
            new ConfigureCollaborationSpace(collaborationId, DisplayName: "Host Space Updated"),
            CancellationToken.None);
        var archived = await controlPlane.Collaboration.ArchiveSpaceAsync(new ArchiveCollaborationSpace(collaborationId), CancellationToken.None);
        var spaceOverview = await controlPlane.Collaboration.GetSpaceOverviewAsync(new GetCollaborationSpaceOverview(collaborationId), CancellationToken.None);
        var spaceList = await controlPlane.Collaboration.ListSpacesAsync(new ListCollaborationSpaces(IncludeArchived: true), CancellationToken.None);
        var sessionBound = await controlPlane.Collaboration.BindParticipantToSessionAsync(
            new BindParticipantToSession(new SessionId("session-host"), participantId),
            CancellationToken.None);
        var workflowBound = await controlPlane.Collaboration.BindParticipantToWorkflowAsync(
            new BindParticipantToWorkflow(new WorkflowId("workflow-host"), participantId),
            CancellationToken.None);
        var roleUpdated = await controlPlane.Collaboration.UpdateParticipantRoleAsync(
            new UpdateParticipantRole(participantId, "owner"),
            CancellationToken.None);
        var participant = await controlPlane.Collaboration.GetParticipantProjectionAsync(new GetParticipantProjection(participantId), CancellationToken.None);
        var participants = await controlPlane.Collaboration.ListParticipantsInScopeAsync(new ListParticipantsInScope(collaborationId), CancellationToken.None);

        var snapshot = await controlPlane.Sessions.GetSnapshotAsync(CancellationToken.None);
        var sessionOverview = await controlPlane.Sessions.GetSessionOverviewAsync(new GetSessionOverview(new SessionId("session-host")), CancellationToken.None);
        var sessions = await controlPlane.Sessions.ListSessionsAsync(new ListSessions(collaborationId, IncludeClosed: true), CancellationToken.None);

        var account = await controlPlane.Identity.GetAccountProfileAsync(new GetAccountProfile(accountId), CancellationToken.None);
        var devices = await controlPlane.Identity.ListBoundDevicesAsync(new ListBoundDevices(accountId), CancellationToken.None);
        var memorySpaces = await controlPlane.Memory.ListMemorySpacesAsync(new ListMemorySpaces(), CancellationToken.None);
        var overlay = await controlPlane.Memory.ResolveMemoryOverlayAsync(new ResolveMemoryOverlay(), CancellationToken.None);

        Assert.Equal("Host Space", created.DisplayName);
        Assert.Equal("Host Space Updated", configured.DisplayName);
        Assert.True(archived);
        Assert.Equal(collaborationId, spaceOverview?.SpaceId);
        Assert.Contains(spaceList, item => item.SpaceId == new CollaborationSpaceId("space-host"));
        Assert.True(sessionBound);
        Assert.True(workflowBound);
        Assert.True(roleUpdated);
        Assert.Equal(participantId, participant?.ParticipantId);
        Assert.Contains(participants, item => item.ParticipantId == participantId);

        Assert.Equal("fake-host-gateway", snapshot.RuntimeName);
        Assert.False(snapshot.HasActiveTurn);
        Assert.Equal("session-host", sessionOverview?.SessionId.Value);
        Assert.Contains(sessions, item => item.SessionId.Value == "session-host");

        Assert.Equal(accountId, account?.Id);
        Assert.Single(devices);
        Assert.NotEmpty(memorySpaces);
        Assert.Equal(MemoryMergeDecision.Applied, overlay.MergeDecision);
    }

    [Fact]
    public void HostGatewaySource_ShouldExposeCatalogAgentFormalQueries()
    {
        var repoRoot = FindRepoRoot();
        var interfaceSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.HostGateway", "ITianShuHostGateway.cs"));
        var gatewaySource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.HostGateway", "TianShuHostGateway.cs"));

        Assert.Contains("public interface IHostGateway", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("ValueTask<HostOperationResult> InvokeAsync(HostOperationRequest request, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("IAsyncEnumerable<HostViewUpdate> SubscribeAsync(HostSubscriptionRequest request, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("ValueTask<HostSnapshot> SnapshotAsync(HostSnapshotRequest request, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("Task<HostCapabilityCatalogResult> GetCapabilityCatalogAsync(HostGetCapabilityCatalogQuery query, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("Task<HostResolvedEngineBindingResult> ResolveEngineBindingAsync(HostResolveEngineBindingQuery query, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);
        Assert.Contains("Task<HostAgentListResult> ListAgentsAsync(HostListAgentsQuery query, CancellationToken cancellationToken);", interfaceSource, StringComparison.Ordinal);

        Assert.Contains("controlPlane.ProcessAsync(", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("controlPlane.Catalog.GetCapabilityCatalogAsync(", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("controlPlane.Catalog.ResolveEngineBindingAsync(", gatewaySource, StringComparison.Ordinal);
        Assert.Contains("controlPlane.Agents.ListAgentsAsync(", gatewaySource, StringComparison.Ordinal);
    }

    [Fact]
    public void HostGatewayProject_ShouldNotReferenceKernelExecutionRuntimeOrModuleImplementations()
    {
        var repoRoot = FindRepoRoot();
        var projectSource = File.ReadAllText(Path.Combine(repoRoot, "src", "Core", "TianShu.HostGateway", "TianShu.HostGateway.csproj"));
        var sourceRoot = Path.Combine(repoRoot, "src", "Core", "TianShu.HostGateway");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        var forbiddenProjectReferences = new[]
        {
            "TianShu.Kernel.csproj",
            "TianShu.Kernel.Abstractions.csproj",
            "TianShu.Kernel.Adaptive.csproj",
            "TianShu.Kernel.Strategies.csproj",
            "TianShu.Execution.Runtime.csproj",
            "TianShu.Provider.",
            "TianShu.Tools.",
            "TianShu.IdentityMemory.csproj",
            "TianShu.ArtifactStore.csproj",
            "TianShu.Diagnostics.csproj",
        };
        var forbiddenSourceTokens = new[]
        {
            "KernelRuntimeProductTerminalProjection",
            "KernelRuntimeReplaySummary",
            "KernelRuntimeTurnLoopResult",
            "RuntimeStep",
            "StageGraph",
            "new TianShuControlPlane",
            "TianShu.Execution.Runtime",
            "TianShu.Provider.",
            "TianShu.Tools.",
            "TianShu.IdentityMemory",
            "TianShu.ArtifactStore",
            "TianShu.Diagnostics",
        };

        foreach (var reference in forbiddenProjectReferences)
        {
            Assert.DoesNotContain(reference, projectSource, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var token in forbiddenSourceTokens)
        {
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HostGatewayTestDoubles_ShouldNotRetainNotSupportedFallbacks()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "tests", "TianShu.HostGateway.Tests", "HostGatewayTests.cs"));
        const string fakeControlPlaneMarker = "private sealed class FakeTianShuControlPlane";
        var fakeControlPlaneSectionStart = source.LastIndexOf(fakeControlPlaneMarker, StringComparison.Ordinal);
        Assert.True(fakeControlPlaneSectionStart >= 0, "未找到 HostGateway test doubles 起始位置。");
        var sourceToInspect = source[fakeControlPlaneSectionStart..];
        var forbiddenPatterns = new[]
        {
            "IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StartReviewAsync(ControlPlaneReviewStartCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "CreateJobAsync(ControlPlaneCreateJobCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "DispatchJobAsync(ControlPlaneDispatchJobCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ReportJobItemAsync(ControlPlaneReportJobItemCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ReadJobAsync(ControlPlaneReadJobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListAgentsAsync(ControlPlaneAgentListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken)\r\n            => throw new NotSupportedException();",
            "ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListModelsAsync(ControlPlaneModelCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "GetCapabilityCatalogAsync(GetCapabilityCatalog query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ResolveEngineBindingAsync(ResolveEngineBinding query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListSkillsAsync(ControlPlaneSkillCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "WriteSkillConfigAsync(ControlPlaneSkillConfigWriteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListPluginsAsync(ControlPlanePluginCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ReadPluginAsync(ControlPlanePluginReadQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "InstallPluginAsync(ControlPlanePluginInstallCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "UninstallPluginAsync(ControlPlanePluginUninstallCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListAppsAsync(ControlPlaneAppCatalogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListCollaborationModesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();",
            "ReloadMcpServersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();",
            "StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();",
        };

        foreach (var pattern in forbiddenPatterns)
        {
            Assert.DoesNotContain(pattern, sourceToInspect, StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private sealed class FakeTianShuControlPlane : ITianShuControlPlane
    {
        public ControlOperationRequest? LastUnifiedOperationRequest { get; private set; }

        public ControlOperationResult? UnifiedOperationResult { get; set; }

        public FakeCollaborationControlPlane Collaboration { get; } = new();

        public FakeSessionControlPlane Sessions { get; } = new();

        public FakeConversationControlPlane Conversations { get; } = new();

        public FakeWorkflowControlPlane Workflows { get; } = new();

        public FakeAgentControlPlane Agents { get; } = new();

        public FakeGovernanceControlPlane Governance { get; } = new();

        public FakeCatalogControlPlane Catalog { get; } = new();

        public FakeArtifactControlPlane Artifacts { get; } = new();

        public FakeDiagnosticsControlPlane Diagnostics { get; } = new();

        public FakeIdentityControlPlane Identity { get; } = new();

        public FakeMemoryControlPlane Memory { get; } = new();

        public FakeProjectionSubscriptions Subscriptions { get; } = new();

        public Task<ControlOperationResult> ProcessAsync(ControlOperationRequest request, CancellationToken cancellationToken)
        {
            LastUnifiedOperationRequest = request;
            return Task.FromResult(UnifiedOperationResult ?? ControlOperationResult.Rejected(
                request,
                ControlOperationKind.Unspecified,
                "fake.control.operation.not_configured",
                "Fake control plane has no unified operation result configured."));
        }

        ICollaborationControlPlane ITianShuControlPlane.Collaboration => Collaboration;

        ISessionControlPlane ITianShuControlPlane.Sessions => Sessions;

        IConversationControlPlane ITianShuControlPlane.Conversations => Conversations;

        IWorkflowControlPlane ITianShuControlPlane.Workflows => Workflows;

        IAgentControlPlane ITianShuControlPlane.Agents => Agents;

        IGovernanceControlPlane ITianShuControlPlane.Governance => Governance;

        ICatalogControlPlane ITianShuControlPlane.Catalog => Catalog;

        IArtifactControlPlane ITianShuControlPlane.Artifacts => Artifacts;

        IDiagnosticsControlPlane ITianShuControlPlane.Diagnostics => Diagnostics;

        IIdentityControlPlane ITianShuControlPlane.Identity => Identity;

        IMemoryControlPlane ITianShuControlPlane.Memory => Memory;

        IProjectionSubscriptions ITianShuControlPlane.Subscriptions => Subscriptions;
    }

    private sealed class FakeCollaborationControlPlane : ICollaborationControlPlane
    {
        public Task<CollaborationSpace> CreateSpaceAsync(CreateCollaborationSpace command, CancellationToken cancellationToken)
            => Task.FromResult(new CollaborationSpace(command.SpaceId, command.Key, command.DisplayName, command.Profile, command.Defaults, command.PolicyRef));

        public Task<CollaborationSpace> ConfigureSpaceAsync(ConfigureCollaborationSpace command, CancellationToken cancellationToken)
            => Task.FromResult(new CollaborationSpace(
                command.SpaceId,
                command.SpaceId.Value,
                command.DisplayName ?? command.SpaceId.Value,
                command.Profile ?? new CollaborationSpaceProfile("fake host collaboration"),
                command.Defaults ?? CollaborationDefaultSet.Empty,
                command.PolicyRef));

        public Task<bool> ArchiveSpaceAsync(ArchiveCollaborationSpace command, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<CollaborationSpaceOverviewProjection?> GetSpaceOverviewAsync(GetCollaborationSpaceOverview query, CancellationToken cancellationToken)
            => Task.FromResult<CollaborationSpaceOverviewProjection?>(new CollaborationSpaceOverviewProjection(query.SpaceId, query.SpaceId.Value, query.SpaceId.Value, false));

        public Task<TianShu.Contracts.Projections.CollaborationSpaceProjection?> GetSpaceProjectionAsync(GetCollaborationSpaceProjection query, CancellationToken cancellationToken)
            => Task.FromResult<TianShu.Contracts.Projections.CollaborationSpaceProjection?>(
                new(
                    new CollaborationSpaceRef(query.SpaceId, query.SpaceId.Value, query.SpaceId.Value),
                    ActiveSessionCount: 0,
                    ActiveThreadCount: 0,
                    IsArchived: false));

        public Task<IReadOnlyList<CollaborationSpaceOverviewProjection>> ListSpacesAsync(ListCollaborationSpaces query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<CollaborationSpaceOverviewProjection>>(
            [
                new CollaborationSpaceOverviewProjection(new CollaborationSpaceId("space-host"), "host-space", "Host Space", query.IncludeArchived)
            ]);

        public Task<bool> BindParticipantToSessionAsync(BindParticipantToSession command, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> BindParticipantToWorkflowAsync(BindParticipantToWorkflow command, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<bool> UpdateParticipantRoleAsync(UpdateParticipantRole command, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<TianShu.Contracts.Participants.ParticipantProjection?> GetParticipantProjectionAsync(GetParticipantProjection query, CancellationToken cancellationToken)
            => Task.FromResult<TianShu.Contracts.Participants.ParticipantProjection?>(new(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value, "owner"));

        public Task<TianShu.Contracts.Projections.ParticipantProjection?> GetParticipantViewProjectionAsync(GetParticipantViewProjection query, CancellationToken cancellationToken)
            => Task.FromResult<TianShu.Contracts.Projections.ParticipantProjection?>(
                new(
                    new ParticipantRef(query.ParticipantId, ParticipantKind.Agent, query.ParticipantId.Value),
                    ScopeKind: "participant",
                    ScopeKey: query.ParticipantId.Value,
                    Role: "owner",
                    IsActive: true));

        public Task<IReadOnlyList<TianShu.Contracts.Participants.ParticipantProjection>> ListParticipantsInScopeAsync(ListParticipantsInScope query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TianShu.Contracts.Participants.ParticipantProjection>>(
            [
                new(new ParticipantId("agent-host"), ParticipantKind.Agent, "agent-host", "owner")
            ]);
    }

    private sealed class FakeSessionControlPlane : ISessionControlPlane
    {
        public Task<ControlPlaneSessionSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneSessionSnapshot
            {
                RuntimeName = "fake-host-gateway",
                HasActiveTurn = false,
            });

        public Task<SessionOverviewProjection?> GetSessionOverviewAsync(GetSessionOverview query, CancellationToken cancellationToken)
            => Task.FromResult<SessionOverviewProjection?>(new SessionOverviewProjection(
                query.SessionId,
                "Host Session",
                new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "host-space", "Host Space"),
                SessionMode.Interactive));

        public Task<IReadOnlyList<SessionOverviewProjection>> ListSessionsAsync(ListSessions query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SessionOverviewProjection>>(
            [
                new SessionOverviewProjection(
                    new SessionId("session-host"),
                    "Host Session",
                    new CollaborationSpaceRef(query.CollaborationSpaceId ?? new CollaborationSpaceId("space-host"), "host-space", "Host Space"),
                    SessionMode.Interactive,
                    IsClosed: query.IncludeClosed)
            ]);
    }

    private sealed class FakeConversationControlPlane : IConversationControlPlane
    {
        public ControlPlaneSubmitTurnCommand? SubmitTurnCommand { get; private set; }

        public ControlPlaneSubmitFollowUpCommand? SubmitFollowUpCommand { get; private set; }

        public ControlPlaneThreadListQuery? ListThreadsQuery { get; private set; }

        public ControlPlaneLoadedThreadListQuery? ListLoadedThreadsQuery { get; private set; }

        public ControlPlaneStartThreadCommand? StartThreadCommand { get; private set; }

        public ControlPlaneForkThreadCommand? ForkThreadCommand { get; private set; }

        public ControlPlaneResumeThreadCommand? ResumeThreadCommand { get; private set; }

        public ControlPlaneReadThreadQuery? ReadThreadQuery { get; private set; }

        public ControlPlaneRenameThreadCommand? RenameThreadCommand { get; private set; }

        public ControlPlaneArchiveThreadCommand? ArchiveThreadCommand { get; private set; }

        public ControlPlaneDeleteThreadCommand? DeleteThreadCommand { get; private set; }

        public ControlPlaneClearThreadsCommand? ClearThreadsCommand { get; private set; }

        public ControlPlaneUnarchiveThreadCommand? UnarchiveThreadCommand { get; private set; }

        public ControlPlaneUpdateThreadMetadataCommand? UpdateThreadMetadataCommand { get; private set; }

        public ControlPlaneRollbackThreadCommand? RollbackThreadCommand { get; private set; }

        public ControlPlaneCompactThreadCommand? CompactThreadCommand { get; private set; }

        public ControlPlaneCleanBackgroundTerminalsCommand? CleanBackgroundTerminalsCommand { get; private set; }

        public ControlPlaneUnsubscribeThreadCommand? UnsubscribeThreadCommand { get; private set; }

        public ThreadId? StreamThreadId { get; private set; }

        public ControlPlaneTurnSubmissionResult SubmitTurnResult { get; set; } = new();

        public ControlPlaneTurnSubmissionResult SubmitFollowUpResult { get; set; } = new();

        public ControlPlaneThreadListResult ListThreadsResult { get; set; } = new();

        public ControlPlaneLoadedThreadListResult ListLoadedThreadsResult { get; set; } = new();

        public ControlPlaneThreadSummary? StartThreadResult { get; set; }

        public ControlPlaneThreadSummary? ForkThreadResult { get; set; }

        public ControlPlaneThreadSnapshot? ResumeThreadResult { get; set; }

        public ControlPlaneThreadOperationResult ReadThreadResult { get; set; } = new();

        public bool RenameThreadResult { get; set; }

        public bool ArchiveThreadResult { get; set; }

        public bool DeleteThreadResult { get; set; }

        public ControlPlaneClearThreadsResult ClearThreadsResult { get; set; } = new();

        public ControlPlaneThreadOperationResult UnarchiveThreadResult { get; set; } = new();

        public ControlPlaneThreadOperationResult UpdateThreadMetadataResult { get; set; } = new();

        public ControlPlaneThreadOperationResult RollbackThreadResult { get; set; } = new();

        public ControlPlaneThreadUnsubscribeResult UnsubscribeThreadResult { get; set; } = new();

        public IReadOnlyList<ControlPlaneConversationStreamEvent> StreamEvents { get; set; } = Array.Empty<ControlPlaneConversationStreamEvent>();

        public bool InterruptTurnInvoked { get; private set; }

        public Task<ControlPlaneTurnSubmissionResult> SubmitTurnAsync(ControlPlaneSubmitTurnCommand command, CancellationToken cancellationToken)
        {
            SubmitTurnCommand = command;
            return Task.FromResult(SubmitTurnResult);
        }

        public Task<ControlPlaneTurnSubmissionResult> SubmitFollowUpAsync(ControlPlaneSubmitFollowUpCommand command, CancellationToken cancellationToken)
        {
            SubmitFollowUpCommand = command;
            return Task.FromResult(SubmitFollowUpResult);
        }

        public Task<ControlPlanePendingFollowUpMutationResult> MutatePendingFollowUpAsync(ControlPlaneMutatePendingFollowUpCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlanePendingFollowUpMutationResult());

        public Task InterruptTurnAsync(CancellationToken cancellationToken)
        {
            InterruptTurnInvoked = true;
            return Task.CompletedTask;
        }

        public Task<ControlPlaneThreadListResult> ListThreadsAsync(ControlPlaneThreadListQuery query, CancellationToken cancellationToken)
        {
            ListThreadsQuery = query;
            return Task.FromResult(ListThreadsResult);
        }

        public Task<TianShu.Contracts.Projections.ThreadProjection?> GetThreadProjectionAsync(GetThreadProjection query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.ThreadId == new ThreadId("thread-host")
                    ? new TianShu.Contracts.Projections.ThreadProjection(
                        query.ThreadId,
                        "Host Thread",
                        new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                        new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent"),
                        new TurnId("turn-host"),
                        true)
                    : null);

        public Task<ControlPlaneThreadSummary?> StartThreadAsync(ControlPlaneStartThreadCommand command, CancellationToken cancellationToken)
        {
            StartThreadCommand = command;
            return Task.FromResult(StartThreadResult);
        }

        public Task<ControlPlaneThreadSummary?> ForkThreadAsync(ControlPlaneForkThreadCommand command, CancellationToken cancellationToken)
        {
            ForkThreadCommand = command;
            return Task.FromResult(ForkThreadResult);
        }

        public Task<ControlPlaneThreadSnapshot?> ResumeThreadAsync(ControlPlaneResumeThreadCommand command, CancellationToken cancellationToken)
        {
            ResumeThreadCommand = command;
            ControlPlaneThreadSnapshot? result = ResumeThreadResult ?? new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = command.ThreadId,
                    Preview = "preview",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
            };
            return Task.FromResult<ControlPlaneThreadSnapshot?>(result);
        }

        public Task<ControlPlaneLoadedThreadListResult> ListLoadedThreadsAsync(ControlPlaneLoadedThreadListQuery query, CancellationToken cancellationToken)
        {
            ListLoadedThreadsQuery = query;
            return Task.FromResult(ListLoadedThreadsResult);
        }

        public Task<ControlPlaneThreadOperationResult> ReadThreadAsync(ControlPlaneReadThreadQuery query, CancellationToken cancellationToken)
        {
            ReadThreadQuery = query;
            return Task.FromResult(ReadThreadResult);
        }

        public Task<bool> RenameThreadAsync(ControlPlaneRenameThreadCommand command, CancellationToken cancellationToken)
        {
            RenameThreadCommand = command;
            return Task.FromResult(RenameThreadResult);
        }

        public Task<bool> ArchiveThreadAsync(ControlPlaneArchiveThreadCommand command, CancellationToken cancellationToken)
        {
            ArchiveThreadCommand = command;
            return Task.FromResult(ArchiveThreadResult);
        }

        public Task<bool> DeleteThreadAsync(ControlPlaneDeleteThreadCommand command, CancellationToken cancellationToken)
        {
            DeleteThreadCommand = command;
            return Task.FromResult(DeleteThreadResult);
        }

        public Task<ControlPlaneClearThreadsResult> ClearThreadsAsync(ControlPlaneClearThreadsCommand command, CancellationToken cancellationToken)
        {
            ClearThreadsCommand = command;
            return Task.FromResult(ClearThreadsResult);
        }

        public Task<ControlPlaneThreadCommandAcceptedResult> CompactThreadAsync(ControlPlaneCompactThreadCommand command, CancellationToken cancellationToken)
        {
            CompactThreadCommand = command;
            return Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
        }

        public Task<ControlPlaneThreadCommandAcceptedResult> CleanBackgroundTerminalsAsync(ControlPlaneCleanBackgroundTerminalsCommand command, CancellationToken cancellationToken)
        {
            CleanBackgroundTerminalsCommand = command;
            return Task.FromResult(new ControlPlaneThreadCommandAcceptedResult());
        }

        public Task<ControlPlaneThreadUnsubscribeResult> UnsubscribeThreadAsync(ControlPlaneUnsubscribeThreadCommand command, CancellationToken cancellationToken)
        {
            UnsubscribeThreadCommand = command;
            return Task.FromResult(UnsubscribeThreadResult);
        }

        public Task<ControlPlaneThreadElicitationResult> IncrementThreadElicitationAsync(ControlPlaneIncrementThreadElicitationCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneThreadElicitationResult { Count = 1, Paused = false });

        public Task<ControlPlaneThreadElicitationResult> DecrementThreadElicitationAsync(ControlPlaneDecrementThreadElicitationCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneThreadElicitationResult { Count = 0, Paused = false });

        public Task<ControlPlaneThreadOperationResult> UnarchiveThreadAsync(ControlPlaneUnarchiveThreadCommand command, CancellationToken cancellationToken)
        {
            UnarchiveThreadCommand = command;
            return Task.FromResult(UnarchiveThreadResult);
        }

        public Task<ControlPlaneThreadOperationResult> UpdateThreadMetadataAsync(ControlPlaneUpdateThreadMetadataCommand command, CancellationToken cancellationToken)
        {
            UpdateThreadMetadataCommand = command;
            return Task.FromResult(UpdateThreadMetadataResult);
        }

        public Task<ControlPlaneThreadOperationResult> RollbackThreadAsync(ControlPlaneRollbackThreadCommand command, CancellationToken cancellationToken)
        {
            RollbackThreadCommand = command;
            return Task.FromResult(RollbackThreadResult);
        }

        public async IAsyncEnumerable<ControlPlaneConversationStreamEvent> SubscribeStreamAsync(
            ThreadId? threadId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            StreamThreadId = threadId;
            foreach (var item in StreamEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public Task<ControlPlaneFuzzyFileSearchResult> SearchFuzzyFilesAsync(ControlPlaneFuzzyFileSearchQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneFuzzyFileSearchResult
            {
                Files =
                [
                    new ControlPlaneFuzzyFileSearchFile
                    {
                        Path = query.Query ?? "host/path",
                        FileName = "host-file.txt",
                    },
                ],
            });

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StartFuzzyFileSearchSessionAsync(ControlPlaneStartFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> UpdateFuzzyFileSearchSessionAsync(ControlPlaneUpdateFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());

        public Task<ControlPlaneFuzzyFileSearchCommandAcceptedResult> StopFuzzyFileSearchSessionAsync(ControlPlaneStopFuzzyFileSearchSessionCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneFuzzyFileSearchCommandAcceptedResult());

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StartRealtimeAsync(ControlPlaneRealtimeStartCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeTextAsync(ControlPlaneRealtimeAppendTextCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());

        public Task<ControlPlaneRealtimeCommandAcceptedResult> AppendRealtimeAudioAsync(ControlPlaneRealtimeAppendAudioCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());

        public Task<ControlPlaneRealtimeCommandAcceptedResult> HandoffRealtimeOutputAsync(ControlPlaneRealtimeHandoffOutputCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());

        public Task<ControlPlaneRealtimeCommandAcceptedResult> StopRealtimeAsync(ControlPlaneRealtimeStopCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRealtimeCommandAcceptedResult());
    }

    private sealed class FakeWorkflowControlPlane : IWorkflowControlPlane
    {
        public Task<Workflow> CreateWorkflowAsync(CreateWorkflow command, CancellationToken cancellationToken)
            => Task.FromResult(
                new Workflow(
                    command.WorkflowId,
                    new CollaborationSpaceRef(command.CollaborationSpaceId, command.CollaborationSpaceId.Value, command.CollaborationSpaceId.Value),
                    command.DisplayName,
                    WorkflowState.Draft,
                    command.OwnerParticipant,
                    command.ThreadId));

        public Task<PlanProjection> PublishPlanAsync(PublishPlan command, CancellationToken cancellationToken)
            => Task.FromResult(new PlanProjection(command.WorkflowId, command.Plan));

        public Task<TianShu.Contracts.Workflows.Task> CreateTaskAsync(CreateTask command, CancellationToken cancellationToken)
            => Task.FromResult(command.Task);

        public Task<TianShu.Contracts.Workflows.Task?> UpdateTaskStateAsync(UpdateTaskState command, CancellationToken cancellationToken)
            => Task.FromResult<TianShu.Contracts.Workflows.Task?>(null);

        public Task<WorkflowBoardProjection?> GetWorkflowBoardProjectionAsync(GetWorkflowBoard query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.WorkflowId == new WorkflowId("workflow-host")
                    ? new WorkflowBoardProjection(
                        query.WorkflowId,
                        "Host Workflow",
                        new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                        PendingTaskCount: 0,
                        RunningTaskCount: 1,
                        CompletedTaskCount: 2)
                    : null);

        public Task<TaskBoardProjection?> GetTaskBoardProjectionAsync(GetTaskBoard query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.WorkflowId == new WorkflowId("workflow-host")
                    ? new TaskBoardProjection(
                        query.WorkflowId,
                        [
                            new TaskBoardItem(
                                new TaskId("task-host"),
                                "Host Task",
                                "running",
                                new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent")),
                        ])
                    : null);

        public Task<PlanProjection?> GetPlanProjectionAsync(GetPlanProjection query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.WorkflowId == new WorkflowId("workflow-host")
                    ? new PlanProjection(
                        query.WorkflowId,
                        new Plan(
                            "Host Plan",
                            [
                                new PlanStep(
                                    0,
                                    "Host Plan Step",
                                    "保持 workflow 计划查询骨架可用。"),
                            ]))
                    : null);

        public Task<ControlPlaneReviewStartResult> StartReviewAsync(ControlPlaneReviewStartCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneReviewStartResult
            {
                ReviewThreadId = "review-thread-host",
                Turn = new ControlPlaneReviewTurn
                {
                    Id = "review-turn-host",
                    Status = "queued",
                },
            });

        public Task<ControlPlaneJobOperationResult> CreateJobAsync(ControlPlaneCreateJobCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = command.JobId ?? new JobId("job-host"),
                    Name = command.Name,
                    Status = "created",
                    Instruction = command.Instruction,
                },
            });

        public Task<ControlPlaneJobOperationResult> DispatchJobAsync(ControlPlaneDispatchJobCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = command.JobId,
                    Status = "dispatched",
                },
            });

        public Task<ControlPlaneJobOperationResult> ReportJobItemAsync(ControlPlaneReportJobItemCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneJobOperationResult
            {
                Item = new ControlPlaneJobItemDetails
                {
                    ItemId = command.ItemId,
                    Status = command.Status,
                    LastError = command.LastError,
                    Result = command.Result,
                },
            });

        public Task<ControlPlaneJobOperationResult> ReadJobAsync(ControlPlaneReadJobQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneJobOperationResult
            {
                Job = new ControlPlaneJobDetails
                {
                    Id = query.JobId,
                    Status = "loaded",
                },
            });

        public Task<ControlPlaneJobListResult> ListJobsAsync(ControlPlaneListJobsQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneJobListResult
            {
                Jobs =
                [
                    new ControlPlaneJobDetails
                    {
                        Id = new JobId("job-host-active"),
                        Status = "running",
                    },
                ],
            });
    }

    private sealed class FakeAgentControlPlane : IAgentControlPlane
    {
        public ControlPlaneAgentListQuery? AgentListQuery { get; private set; }

        public ControlPlaneAgentRosterResult AgentListResult { get; set; } = new()
        {
            Agents =
            [
                new ControlPlaneAgentDescriptor
                {
                    ThreadId = new ThreadId("agent-thread-host"),
                    Preview = "host agent",
                    AgentNickname = "Host Agent",
                    AgentRole = "reviewer",
                    Status = "running",
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
            ],
        };

        public Task<AgentRosterProjection?> GetAgentRosterProjectionAsync(GetAgentRoster query, CancellationToken cancellationToken)
            => Task.FromResult<AgentRosterProjection?>(new AgentRosterProjection(
            [
                new AgentRosterEntry(
                    new AgentId("agent-thread-host"),
                    new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent"),
                    "reviewer",
                    1,
                    true),
            ]));

        public Task<TeamProjection?> GetTeamProjectionAsync(GetTeamProjection query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.TeamId == new TeamId("team-host")
                    ? new TeamProjection(
                        new Team(
                            new TeamId("team-host"),
                            "Host Team",
                            [new AgentId("agent-thread-host")]),
                        [
                            new Agent(
                                new AgentId("agent-thread-host"),
                                new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent"),
                                "Host Agent",
                                AgentRole.Reviewer),
                        ])
                    : null);

        public Task<ControlPlaneAgentRosterResult> ListAgentsAsync(ControlPlaneAgentListQuery query, CancellationToken cancellationToken)
        {
            AgentListQuery = query;
            return Task.FromResult(AgentListResult);
        }

        public Task<ControlPlaneAgentThreadRegistrationResult> RegisterAgentThreadAsync(ControlPlaneRegisterAgentThreadCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneAgentThreadRegistrationResult
            {
                Agent = new ControlPlaneAgentDescriptor
                {
                    ThreadId = command.ThreadId,
                    Preview = "registered host agent",
                    AgentNickname = command.AgentNickname,
                    AgentRole = command.AgentRole,
                    UpdatedAt = DateTimeOffset.UtcNow,
                },
            });
    }

    private sealed class FakeGovernanceControlPlane : IGovernanceControlPlane
    {
        public ControlPlaneApprovalResolution? ApprovalResolution { get; private set; }

        public ControlPlanePermissionGrant? PermissionGrant { get; private set; }

        public ControlPlaneUserInputSubmission? UserInputSubmission { get; private set; }

        public bool ResolveApprovalResult { get; set; }

        public bool ResolvePermissionResult { get; set; }

        public bool SubmitUserInputResult { get; set; }

        public Task<ApprovalQueueProjection?> GetApprovalQueueProjectionAsync(ListPendingApprovals query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.RequestedFromParticipantId is null
                || query.RequestedFromParticipantId == new ParticipantId("participant-host")
                    ? new ApprovalQueueProjection(
                    [
                        new ApprovalQueueItem(
                            new ApprovalId("approval-host"),
                            "Host Approval",
                            "Need approval",
                            new ParticipantRef(new ParticipantId("participant-host"), ParticipantKind.Human, "Host User"),
                            new DateTimeOffset(2026, 4, 29, 8, 0, 0, TimeSpan.Zero)),
                    ])
                    : null);

        public Task<IReadOnlyList<UserInputRequest>> ListUserInputRequestsAsync(ListUserInputRequests query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<UserInputRequest>>(
                query.RequestedFromParticipantId is null
                || query.RequestedFromParticipantId == new ParticipantId("participant-host")
                    ?
                    [
                        new UserInputRequest(
                            new UserInputRequestId("request-host"),
                            "请选择配置文件",
                            new ParticipantRef(new ParticipantId("participant-host"), ParticipantKind.Human, "Host User"))
                    ]
                    : []);

        public Task<bool> ResolveApprovalAsync(ControlPlaneApprovalResolution command, CancellationToken cancellationToken)
        {
            ApprovalResolution = command;
            return Task.FromResult(ResolveApprovalResult);
        }

        public Task<bool> ResolvePermissionRequestAsync(ControlPlanePermissionGrant command, CancellationToken cancellationToken)
        {
            PermissionGrant = command;
            return Task.FromResult(ResolvePermissionResult);
        }

        public Task<bool> SubmitUserInputAsync(ControlPlaneUserInputSubmission command, CancellationToken cancellationToken)
        {
            UserInputSubmission = command;
            return Task.FromResult(SubmitUserInputResult);
        }

        public Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneFeedbackUploadResult
            {
                ThreadId = command.ThreadId,
            });
    }

    private sealed class FakeIdentityControlPlane : IIdentityControlPlane
    {
        public Task<Account?> GetAccountProfileAsync(GetAccountProfile query, CancellationToken cancellationToken)
            => Task.FromResult<Account?>(new Account(query.AccountId, "Host Account"));

        public Task<IReadOnlyList<DeviceBinding>> ListBoundDevicesAsync(ListBoundDevices query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeviceBinding>>(
            [
                new DeviceBinding(new DeviceId("device-host"), query.AccountId, "Host Device", "Windows")
            ]);
    }

    private sealed class FakeMemoryControlPlane : IMemoryControlPlane
    {
        public Task<IReadOnlyList<MemorySpace>> ListMemorySpacesAsync(ListMemorySpaces query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemorySpace>>(
            [
                new MemorySpace(new MemorySpaceId("memory-host"), query.ScopeKind ?? MemoryScopeKind.Workspace, "host-space", "Host Memory")
            ]);

        public Task<MemoryOverlay> ResolveMemoryOverlayAsync(ResolveMemoryOverlay query, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryOverlay());

        public Task<IReadOnlyList<MemoryProviderDescriptor>> ListMemoryProvidersAsync(ListMemoryProviders query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemoryProviderDescriptor>>([]);

        public Task<MemoryQueryResult> FilterMemoryAsync(FilterMemory query, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult());

        public Task<MemoryMutationResult> AddMemoryAsync(AddMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<IReadOnlyList<MemoryCandidate>> ExtractMemoryAsync(ExtractMemory command, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MemoryCandidate>>([]);

        public Task<MemoryMutationResult> ImportMemoryAsync(ImportMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryQueryResult> ExportMemoryAsync(ExportMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryQueryResult());

        public Task<MemoryMutationResult> BindMemoryProviderAsync(BindMemoryProvider command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryConsolidationRunResult> RunMemoryConsolidationAsync(RunMemoryConsolidation command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryConsolidationRunResult(0, 0));

        public Task<MemoryMutationResult> ForgetMemoryAsync(ForgetMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryMutationResult> DeleteMemoryAsync(DeleteMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryMutationResult> SupersedeMemoryAsync(SupersedeMemory command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryMutationResult> ApproveMemoryReviewAsync(ApproveMemoryReview command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryMutationResult> RecordMemoryFeedbackAsync(RecordMemoryFeedback command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));

        public Task<MemoryMutationResult> RecordMemoryCitationAsync(RecordMemoryCitation command, CancellationToken cancellationToken)
            => Task.FromResult(new MemoryMutationResult(true));
    }

    private sealed class FakeDiagnosticsControlPlane : IDiagnosticsControlPlane
    {
        public GetExecutionTrace? ExecutionTraceQuery { get; private set; }

        public ListAttemptSummaries? AttemptSummariesQuery { get; private set; }

        public ControlPlaneFeedbackUploadCommand? FeedbackUploadCommand { get; private set; }

        public ExecutionTrace? ExecutionTraceResult { get; set; }

        public IReadOnlyList<AttemptSummary> AttemptSummariesResult { get; set; } = [];

        public Task<ExecutionTrace?> GetExecutionTraceAsync(GetExecutionTrace query, CancellationToken cancellationToken)
        {
            ExecutionTraceQuery = query;
            return Task.FromResult(ExecutionTraceResult);
        }

        public Task<IReadOnlyList<AttemptSummary>> ListAttemptSummariesAsync(ListAttemptSummaries query, CancellationToken cancellationToken)
        {
            AttemptSummariesQuery = query;
            return Task.FromResult(AttemptSummariesResult);
        }

        public Task<ControlPlaneFeedbackUploadResult> UploadFeedbackAsync(ControlPlaneFeedbackUploadCommand command, CancellationToken cancellationToken)
        {
            FeedbackUploadCommand = command;
            return Task.FromResult(new ControlPlaneFeedbackUploadResult
            {
                ThreadId = command.ThreadId,
            });
        }

        public Task<ControlPlaneDebugClearMemoriesResult> ClearDebugMemoriesAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneDebugClearMemoriesResult());
    }

    private sealed class FakeCatalogControlPlane : ICatalogControlPlane
    {
        public GetCapabilityCatalog? CapabilityCatalogQuery { get; private set; }

        public ResolveEngineBinding? EngineBindingQuery { get; private set; }

        public CapabilityCatalogSnapshot CapabilityCatalogResult { get; set; } = new();

        public ResolvedEngineBinding EngineBindingResult { get; set; } = new(null);

        public Task<ControlPlaneConfigSnapshotResult> ReadConfigAsync(ControlPlaneConfigReadQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneConfigSnapshotResult());

        public Task<ControlPlaneConfigRequirementsResult> ReadConfigRequirementsAsync(ControlPlaneConfigRequirementsQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneConfigRequirementsResult());

        public Task<ControlPlaneConfigWriteResult> WriteConfigValueAsync(ControlPlaneConfigValueWriteCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneConfigWriteResult());

        public Task<ControlPlaneConfigWriteResult> WriteConfigBatchAsync(ControlPlaneConfigBatchWriteCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneConfigWriteResult());

        public Task<ControlPlaneModelCatalogResult> ListModelsAsync(ControlPlaneModelCatalogQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneModelCatalogResult());

        public Task<CapabilityCatalogSnapshot> GetCapabilityCatalogAsync(GetCapabilityCatalog query, CancellationToken cancellationToken)
        {
            CapabilityCatalogQuery = query;
            return Task.FromResult(CapabilityCatalogResult);
        }

        public Task<ResolvedEngineBinding> ResolveEngineBindingAsync(ResolveEngineBinding query, CancellationToken cancellationToken)
        {
            EngineBindingQuery = query;
            return Task.FromResult(EngineBindingResult);
        }

        public Task<ControlPlaneSkillCatalogResult> ListSkillsAsync(ControlPlaneSkillCatalogQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneSkillCatalogResult());

        public Task<ControlPlaneSkillConfigWriteResult> WriteSkillConfigAsync(ControlPlaneSkillConfigWriteCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneSkillConfigWriteResult());

        public Task<ControlPlaneRemoteSkillCatalogResult> ListRemoteSkillsAsync(ControlPlaneRemoteSkillCatalogQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRemoteSkillCatalogResult());

        public Task<ControlPlaneRemoteSkillExportResult> ExportRemoteSkillAsync(ControlPlaneRemoteSkillExportCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneRemoteSkillExportResult());

        public Task<ControlPlanePluginCatalogResult> ListPluginsAsync(ControlPlanePluginCatalogQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlanePluginCatalogResult());

        public Task<ControlPlanePluginReadResult> ReadPluginAsync(ControlPlanePluginReadQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlanePluginReadResult());

        public Task<ControlPlanePluginInstallResult> InstallPluginAsync(ControlPlanePluginInstallCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlanePluginInstallResult());

        public Task<ControlPlanePluginUninstallResult> UninstallPluginAsync(ControlPlanePluginUninstallCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlanePluginUninstallResult());

        public Task<ControlPlaneAppCatalogResult> ListAppsAsync(ControlPlaneAppCatalogQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneAppCatalogResult());

        public Task<ControlPlaneExperimentalFeatureCatalogResult> ListExperimentalFeaturesAsync(ControlPlaneExperimentalFeatureQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneExperimentalFeatureCatalogResult());

        public Task<ControlPlaneCollaborationModeCatalogResult> ListCollaborationModesAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneCollaborationModeCatalogResult());

        public Task<ControlPlaneMcpServerCatalogResult> ListMcpServerStatusAsync(ControlPlaneMcpServerStatusQuery query, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneMcpServerCatalogResult());

        public Task<ControlPlaneMcpServerReloadResult> ReloadMcpServersAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneMcpServerReloadResult());

        public Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneProviderPackageReloadResult());

        public Task<ControlPlaneMcpServerOauthLoginStartResult> StartMcpServerOauthLoginAsync(ControlPlaneMcpServerOauthLoginStartCommand command, CancellationToken cancellationToken)
            => Task.FromResult(new ControlPlaneMcpServerOauthLoginStartResult());
    }

    private sealed class FakeArtifactControlPlane : IArtifactControlPlane
    {
        public ControlPlaneConversationArtifactQuery? ConversationSummaryQuery { get; private set; }

        public ControlPlaneGitDiffArtifactQuery? GitDiffQuery { get; private set; }

        public ControlPlaneConversationArtifact? ConversationSummaryResult { get; set; }

        public ControlPlaneGitDiffArtifact GitDiffResult { get; set; } = new();

        public Task<Artifact> PublishArtifactAsync(PublishArtifact command, CancellationToken cancellationToken)
            => Task.FromResult(command.Artifact);

        public Task<Artifact> PromoteArtifactAsync(PromoteArtifact command, CancellationToken cancellationToken)
            => Task.FromResult(new Artifact(
                command.ArtifactId,
                new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                "Host Artifact",
                ArtifactKind.Document,
                state: ArtifactLifecycleState.Promoted));

        public Task<Artifact> AttachArtifactToTaskAsync(AttachArtifactToTask command, CancellationToken cancellationToken)
            => Task.FromResult(new Artifact(
                command.ArtifactId,
                new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                "Host Artifact",
                ArtifactKind.Document,
                state: ArtifactLifecycleState.Published));

        public Task<ArtifactProjection?> GetArtifactProjectionAsync(GetArtifactDetail query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.ArtifactId == new ArtifactId("artifact-host")
                    ? new ArtifactProjection(
                        query.ArtifactId,
                        "Host Artifact",
                        ArtifactKind.Document,
                        ArtifactLifecycleState.Published,
                        new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                        new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent"))
                    : null);

        public Task<ArtifactCollectionProjection?> GetArtifactCollectionProjectionAsync(ListArtifacts query, CancellationToken cancellationToken)
            => Task.FromResult(
                query.CollaborationSpaceId == new CollaborationSpaceId("space-host")
                    ? new ArtifactCollectionProjection(
                        new CollaborationSpaceRef(new CollaborationSpaceId("space-host"), "space-host", "Host Space"),
                        [
                            new ArtifactCollectionItem(
                                new ArtifactId("artifact-host"),
                                "Host Artifact",
                                ArtifactKind.Document,
                                ArtifactLifecycleState.Published,
                                new ParticipantRef(new ParticipantId("agent-thread-host"), ParticipantKind.Agent, "Host Agent")),
                        ])
                    : null);

        public Task<ControlPlaneConversationArtifact?> GetConversationSummaryAsync(ControlPlaneConversationArtifactQuery query, CancellationToken cancellationToken)
        {
            ConversationSummaryQuery = query;
            return Task.FromResult(ConversationSummaryResult);
        }

        public Task<ControlPlaneGitDiffArtifact> GetGitDiffToRemoteAsync(ControlPlaneGitDiffArtifactQuery query, CancellationToken cancellationToken)
        {
            GitDiffQuery = query;
            return Task.FromResult(GitDiffResult);
        }
    }

    private sealed class FakeProjectionSubscriptions : IProjectionSubscriptions
    {
        public IReadOnlyList<ControlPlaneProjectionEvent> ThreadEvents { get; set; } = Array.Empty<ControlPlaneProjectionEvent>();

        public IReadOnlyList<ControlPlaneProjectionEvent> WorkflowEvents { get; set; } = Array.Empty<ControlPlaneProjectionEvent>();

        public IReadOnlyList<ControlPlaneProjectionEvent> AgentEvents { get; set; } = Array.Empty<ControlPlaneProjectionEvent>();

        public IReadOnlyList<ControlPlaneProjectionEvent> GovernanceEvents { get; set; } = Array.Empty<ControlPlaneProjectionEvent>();

        public ControlPlaneThreadSubscription? LastThreadSubscription { get; private set; }

        public ControlPlaneWorkflowSubscription? LastWorkflowSubscription { get; private set; }

        public ControlPlaneAgentSubscription? LastAgentSubscription { get; private set; }

        public ControlPlaneGovernanceSubscription? LastGovernanceSubscription { get; private set; }

        public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeThreadAsync(ControlPlaneThreadSubscription request, CancellationToken cancellationToken)
        {
            LastThreadSubscription = request;
            return ToAsync(ThreadEvents, cancellationToken);
        }

        public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeWorkflowAsync(ControlPlaneWorkflowSubscription request, CancellationToken cancellationToken)
        {
            LastWorkflowSubscription = request;
            return ToAsync(WorkflowEvents, cancellationToken);
        }

        public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeAgentAsync(ControlPlaneAgentSubscription request, CancellationToken cancellationToken)
        {
            LastAgentSubscription = request;
            return ToAsync(AgentEvents, cancellationToken);
        }

        public IAsyncEnumerable<ControlPlaneProjectionEvent> SubscribeGovernanceAsync(ControlPlaneGovernanceSubscription request, CancellationToken cancellationToken)
        {
            LastGovernanceSubscription = request;
            return ToAsync(GovernanceEvents, cancellationToken);
        }

        private static async IAsyncEnumerable<ControlPlaneProjectionEvent> EmptyAsync()
        {
            yield break;
        }

        private static async IAsyncEnumerable<ControlPlaneProjectionEvent> ToAsync(
            IReadOnlyList<ControlPlaneProjectionEvent> items,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }

    private static ProjectionDelta CreateThreadDelta(string threadId, string title)
    {
        var collaboration = new CollaborationSpace(
            new CollaborationSpaceId($"space-{threadId}"),
            $"space-{threadId}",
            "Host",
            new CollaborationSpaceProfile("host"),
            CollaborationDefaultSet.Empty);
        return new ProjectionDelta(
            new ThreadProjectionPayload(
                new TianShu.Contracts.Projections.ThreadProjection(
                    new ThreadId(threadId),
                    title,
                    CollaborationSpaceRef.From(collaboration))),
            new ProjectionCursor($"cursor-{threadId}"));
    }

    private static ProjectionDelta CreateRuntimeThreadDelta(string threadId, string title)
    {
        var collaboration = new CollaborationSpace(
            new CollaborationSpaceId($"space-{threadId}"),
            $"space-{threadId}",
            "Host",
            new CollaborationSpaceProfile("host"),
            CollaborationDefaultSet.Empty);
        return new ProjectionDelta(
            new ThreadProjectionPayload(
                new TianShu.Contracts.Projections.ThreadProjection(
                    new ThreadId(threadId),
                    title,
                    CollaborationSpaceRef.From(collaboration),
                    ActiveTurnId: new TurnId("turn-runtime-projection-001"),
                    HasActiveTurn: true,
                    RuntimeStatus: new ThreadRuntimeStatusProjection(
                        "running",
                        TurnStatus: "running",
                        BackgroundStatus: "running",
                        HasActiveRun: true,
                        ActiveRunRef: "active-run://thread-runtime-projection-001/turn-runtime-projection-001/run-001",
                        NotificationCode: "TurnStarted"),
                    TokenUsage: new ThreadTokenUsageProjection(
                        new ThreadTokenUsageBreakdownProjection(5, 4, 0, 1, 0),
                        new ThreadTokenUsageBreakdownProjection(18, 13, 0, 5, 0),
                        ModelContextWindow: 128000,
                        Estimated: false,
                        Source: "provider.completion.usage"),
                    Diagnostics: new ThreadDiagnosticsProjection(
                        RuntimeTraceRefs: ["trace://runtime/thread-runtime-projection-001"],
                        DiagnosticsRefs: ["diagnostics://runtime/thread-runtime-projection-001"],
                        MetricsEventIds: ["metrics-thread-runtime-projection-001"],
                        FailureCodes: [],
                        MissingReasons: ["price_model_missing"],
                        ContextSlicing: new ThreadContextSlicingDiagnosticsProjection(
                            "context.policy.runtime",
                            BudgetTokens: 60,
                            EstimatedInputTokens: 100,
                            EstimatedIncludedTokens: 60,
                            Segments:
                            [
                                new ThreadContextSegmentProjection(
                                    "history-1",
                                    "ConversationHistory",
                                    "dropped",
                                    DroppedReason: "BudgetExceeded",
                                    EstimatedTokens: 80),
                            ],
                            SourceLayer: "execution.runtime.context_policy")),
                    Evidence: new ThreadEvidenceProjection(
                        TurnLogRef: "turn-log://thread-runtime-projection-001/turn-runtime-projection-001",
                        RolloutRef: "rollout://thread-runtime-projection-001/turn-runtime-projection-001",
                        AuditRefs: ["audit://thread-runtime-projection-001"],
                        DowngradeReasons: []))),
            new ProjectionCursor($"cursor-{threadId}"));
    }
}
