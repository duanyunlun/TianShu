using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Artifacts;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Host;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;

namespace TianShu.Contracts.Host.Tests;

public sealed class HostContractTests
{
    [Fact]
    public void HostInteractionEnvelope_RequiresItems()
    {
        Assert.Throws<ArgumentException>(() => new HostInteractionEnvelope(
            new InteractionEnvelopeId("host-001"),
            new HostContext(HostSurfaceKind.Cli, "cli"),
            Array.Empty<InteractionItem>()));
    }

    [Fact]
    public void HostViewUpdate_RequiresExactlyOnePayload()
    {
        Assert.Throws<ArgumentException>(() => new HostViewUpdate());
        Assert.Throws<ArgumentException>(() => new HostViewUpdate(
            new ProjectionDelta(new ThreadProjectionPayload(
                new TianShu.Contracts.Projections.ThreadProjection(
                    new ThreadId("thread-001"),
                    "线程",
                    new CollaborationSpaceRef(
                        new CollaborationSpaceId("space-001"),
                        "space",
                        "Space")))),
            new ProjectionReset(ProjectionScopeKind.Thread, "thread-001", "reset")));
    }

    [Fact]
    public void HostSubscriptionRequest_PreservesSubscriptionAndNegotiation()
    {
        var subscription = new ProjectionSubscription(
            new SubscriptionToken("subscription-010"),
            ProjectionScopeKind.Thread,
            "thread-010");
        var negotiation = new HostCapabilityNegotiation(SupportsAgentRoster: true);
        var request = new HostSubscriptionRequest(subscription, negotiation);

        Assert.Equal(subscription, request.Subscription);
        Assert.True(request.Negotiation?.SupportsAgentRoster);
    }

    [Fact]
    public void HostResolveApproval_RequiresDecisionAndPreservesCheckpointIdentifiers()
    {
        Assert.Throws<ArgumentException>(() => new HostResolveApproval(
            new CallId("call-001"),
            string.Empty));

        var command = new HostResolveApproval(
            new CallId("call-001"),
            "approve",
            new ApprovalId("approval-001"));

        Assert.Equal("call-001", command.CallId.Value);
        Assert.Equal("approve", command.Decision);
        Assert.Equal("approval-001", command.ApprovalId?.Value);
    }

    [Fact]
    public void HostGrantPermission_DefaultsToTurnScope()
    {
        var command = new HostGrantPermission(
            new CallId("call-002"),
            new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["mode"] = StructuredValue.FromString("workspace_write"),
            });

        Assert.Equal(HostPermissionScope.Turn, command.Scope);
        Assert.Equal("workspace_write", command.Permissions["mode"].GetString());
    }

    [Fact]
    public void HostSubmitTurn_AndFollowUp_PreserveHistoryAndMode()
    {
        var envelope = new HostInteractionEnvelope(
            new InteractionEnvelopeId("interaction-010"),
            new HostContext(HostSurfaceKind.Vsix, "sidecar"),
            [new TextInteractionItem("继续执行")]);
        ControlPlaneConversationMessage[] history =
        [
            new ControlPlaneConversationMessage
            {
                Role = ControlPlaneConversationRole.User,
                Content = "已有历史",
            },
        ];

        var turn = new HostSubmitTurn(envelope, history);
        var followUp = new HostSubmitFollowUp(envelope, HostFollowUpMode.Interrupt, "corr-1");

        Assert.Equal(envelope, turn.Envelope);
        Assert.Equal("已有历史", Assert.Single(turn.History).Content);
        Assert.Equal(HostFollowUpMode.Interrupt, followUp.Mode);
        Assert.Equal("corr-1", followUp.CorrelationId);
    }

    [Fact]
    public void HostSubmitUserInput_RequiresAnswers()
    {
        Assert.Throws<ArgumentException>(() => new HostSubmitUserInput(
            new CallId("call-003"),
            new Dictionary<string, StructuredValue>(StringComparer.Ordinal)));
    }

    [Fact]
    public void HostOutputEvent_RequiresExactlyOnePayload()
    {
        Assert.Throws<ArgumentException>(() => new HostOutputEvent());

        var notification = new HostNotification("info", HostNotificationKind.Info, "提示");
        var viewUpdate = new HostViewUpdate(
            delta: new ProjectionDelta(new ThreadProjectionPayload(
                new TianShu.Contracts.Projections.ThreadProjection(
                    new ThreadId("thread-011"),
                    "线程",
                    new CollaborationSpaceRef(
                        new CollaborationSpaceId("space-011"),
                        "space",
                        "Space")))));

        Assert.Throws<ArgumentException>(() => new HostOutputEvent(notification, viewUpdate));
        Assert.Equal(notification, new HostOutputEvent(notification: notification).Notification);
    }

    [Fact]
    public void HostOperationRequest_And_Result_PreserveTypedSurface()
    {
        var request = new HostOperationRequest(
            "operation-host-001",
            "cli",
            HostOperationKind.CoreIntent,
            StructuredValue.FromString("turn"),
            new HostContext(HostSurfaceKind.Cli, "cli"));
        var result = new HostOperationResult(
            "operation-host-001",
            HostOperationStatus.Accepted,
            StructuredValue.FromString("accepted"),
            [new HostDiagnosticRef("diagnostic-host-001", "info")]);

        Assert.Equal(HostOperationKind.CoreIntent, request.OperationKind);
        Assert.Equal("turn", request.Payload.GetString());
        Assert.Equal(HostOperationStatus.Accepted, result.Status);
        Assert.Equal("diagnostic-host-001", Assert.Single(result.Diagnostics).DiagnosticId);
    }

    [Fact]
    public void HostViewUpdate_And_Snapshot_CanExposeKernelProjectionReadOnly()
    {
        var projection = new KernelRunStateProjection(
            "projection-kernel-001",
            new KernelRunId("run-host-001"),
            new VersionStamp(1),
            KernelRunLifecycleState.Executing,
            currentStageId: new StageId("stage-host-001"));
        var update = new HostViewUpdate(kernelProjection: projection);
        var snapshot = new HostSnapshot(
            "snapshot-host-001",
            ProjectionScopeKind.Thread,
            "thread-host-001",
            kernelProjections: [projection]);

        Assert.Equal(projection, update.KernelProjection);
        Assert.Equal(projection, Assert.Single(snapshot.KernelProjections));
        Assert.Throws<ArgumentException>(() => new HostViewUpdate(
            delta: new ProjectionDelta(new ThreadProjectionPayload(
                new TianShu.Contracts.Projections.ThreadProjection(
                    new ThreadId("thread-host-002"),
                    "线程",
                    new CollaborationSpaceRef(new CollaborationSpaceId("space-host-002"), "space", "Space")))),
            kernelProjection: projection));
    }

    [Fact]
    public void HostThreadQueries_And_ArtifactResults_PreserveTypedFields()
    {
        var listQuery = new HostListThreadsQuery
        {
            Limit = 10,
            Cursor = "cursor-1",
            Archived = true,
            WorkingDirectory = @"D:\repo",
            SortKey = "updated_at",
            ModelProviders = ["openai"],
            SourceKinds = [ControlPlaneThreadSourceKind.Cli],
            SearchTerm = "memory",
        };
        var loadedListQuery = new HostListLoadedThreadsQuery
        {
            Limit = 5,
            Cursor = "loaded-cursor-1",
        };
        var readQuery = new HostReadThreadQuery
        {
            ThreadId = new ThreadId("thread-001"),
            IncludeTurns = true,
        };
        var summaryQuery = new HostReadConversationSummaryQuery
        {
            ThreadId = new ThreadId("thread-001"),
            RolloutPath = @"D:\repo\.tianshu\rollouts\thread-001.jsonl",
        };
        var diffQuery = new HostReadGitDiffToRemoteQuery
        {
            ThreadId = new ThreadId("thread-001"),
        };
        var snapshotResult = new HostThreadSnapshotResult
        {
            Snapshot = new ControlPlaneThreadSnapshot
            {
                Thread = new ControlPlaneThreadSummary
                {
                    ThreadId = new ThreadId("thread-001"),
                    Preview = "preview",
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
            },
        };
        var artifactResult = new HostConversationArtifactResult
        {
            Artifact = new ControlPlaneConversationArtifact
            {
                ConversationId = "conv-1",
                GitSha = "abc123",
            },
        };
        var diffResult = new HostGitDiffArtifactResult
        {
            Artifact = new ControlPlaneGitDiffArtifact
            {
                HasChanges = true,
                Diff = "diff --git a/src b/src",
            },
        };
        var loadedThreadResult = new HostLoadedThreadListResult
        {
            ThreadIds = [new ThreadId("thread-001"), new ThreadId("thread-002")],
            NextCursor = "loaded-cursor-2",
        };

        Assert.Equal(10, listQuery.Limit);
        Assert.Equal(5, loadedListQuery.Limit);
        Assert.Equal("loaded-cursor-1", loadedListQuery.Cursor);
        Assert.Equal("cursor-1", listQuery.Cursor);
        Assert.Equal(@"D:\repo", listQuery.WorkingDirectory);
        Assert.Equal("thread-001", readQuery.ThreadId.Value);
        Assert.True(readQuery.IncludeTurns);
        Assert.Equal(@"D:\repo\.tianshu\rollouts\thread-001.jsonl", summaryQuery.RolloutPath);
        Assert.Equal("thread-001", diffQuery.ThreadId.Value);
        Assert.Equal("thread-001", snapshotResult.Snapshot?.Thread.ThreadId.Value);
        Assert.Equal("corr-1", Assert.Single(snapshotResult.Snapshot!.PendingInputState!.Entries).CorrelationId);
        Assert.Equal("conv-1", artifactResult.Artifact?.ConversationId);
        Assert.Equal("abc123", artifactResult.Artifact?.GitSha);
        Assert.Equal(["thread-001", "thread-002"], loadedThreadResult.ThreadIds.Select(static item => item.Value).ToArray());
        Assert.Equal("loaded-cursor-2", loadedThreadResult.NextCursor);
        Assert.True(diffResult.Artifact.HasChanges);
        Assert.Equal("diff --git a/src b/src", diffResult.Artifact.Diff);
    }

    [Fact]
    public void HostThreadLifecycleCommands_PreserveTypedConfiguration()
    {
        var start = new HostStartThread
        {
            Model = "gpt-5",
            ModelProvider = "openai",
            ApprovalPolicy = "on-request",
            PersistExtendedHistory = true,
        };
        var interrupt = new HostInterruptTurn();
        var rename = new HostRenameThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
            Name = "新的线程名",
        };
        var archive = new HostArchiveThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };
        var delete = new HostDeleteThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };
        var unarchive = new HostUnarchiveThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };
        var rollback = new HostRollbackThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
            NumTurns = 2,
        };
        var compact = new HostCompactThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
            KeepRecentTurns = 3,
        };
        var clean = new HostCleanBackgroundTerminals
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };
        var unsubscribe = new HostUnsubscribeThread
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };
        var streamSubscription = new HostConversationStreamSubscription
        {
            ThreadId = new ThreadId("thread-lifecycle-001"),
        };

        Assert.Equal("gpt-5", start.Model);
        Assert.Equal("openai", start.ModelProvider);
        Assert.True(start.PersistExtendedHistory);
        Assert.NotNull(interrupt);
        Assert.Equal("新的线程名", rename.Name);
        Assert.Equal("thread-lifecycle-001", archive.ThreadId.Value);
        Assert.Equal("thread-lifecycle-001", delete.ThreadId.Value);
        Assert.Equal("thread-lifecycle-001", unarchive.ThreadId.Value);
        Assert.Equal(2, rollback.NumTurns);
        Assert.Equal(3, compact.KeepRecentTurns);
        Assert.Equal("thread-lifecycle-001", clean.ThreadId.Value);
        Assert.Equal("thread-lifecycle-001", unsubscribe.ThreadId.Value);
        Assert.Equal("thread-lifecycle-001", streamSubscription.ThreadId?.Value);
    }
}
