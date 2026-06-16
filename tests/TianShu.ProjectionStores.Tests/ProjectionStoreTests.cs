using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Primitives;
using TianShu.ProjectionStores;

namespace TianShu.ProjectionStores.Tests;

public sealed class ProjectionStoreTests
{
    [Fact]
    public async Task ProjectionSnapshotStore_WhenThreadDeltaUpserted_CanReadTypedSnapshot()
    {
        var store = new InMemoryProjectionSnapshotStore();
        var key = new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-001");
        var collaboration = new CollaborationSpaceRef(new CollaborationSpaceId("space-001"), "design-space", "设计协作");
        var delta = new ProjectionDelta(
            new ThreadProjectionPayload(
                new ThreadProjection(
                    new ThreadId("thread-001"),
                    "实现 Projection Store",
                    collaboration,
                    new ParticipantRef(new ParticipantId("participant-001"), ParticipantKind.Agent, "assistant"),
                    new TurnId("turn-001"),
                    HasActiveTurn: true)),
            new ProjectionCursor("cursor-001"));

        await store.UpsertAsync(key, delta, CancellationToken.None);

        var snapshot = await store.GetAsync(key, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(1, snapshot!.Version);
        Assert.Equal("thread", snapshot.Delta?.Payload.Kind);
        Assert.Null(snapshot.Reset);
        Assert.Equal("cursor-001", snapshot.Delta?.Cursor?.Value);
    }

    [Fact]
    public void ProjectionSnapshotSource_ShouldExposeReadOnlySurface()
    {
        var methodNames = typeof(IProjectionSnapshotSource)
            .GetMethods()
            .Select(static method => method.Name)
            .ToArray();

        Assert.Contains(nameof(IProjectionSnapshotSource.GetAsync), methodNames);
        Assert.DoesNotContain(nameof(IProjectionSnapshotStore.UpsertAsync), methodNames);
        Assert.DoesNotContain(nameof(IProjectionSnapshotStore.ResetAsync), methodNames);
        Assert.True(typeof(IProjectionSnapshotSource).IsAssignableFrom(typeof(IProjectionSnapshotStore)));
    }

    [Fact]
    public async Task ProjectionSnapshotStore_WhenApprovalQueueResetStored_ReplacesCurrentSnapshot()
    {
        var store = new InMemoryProjectionSnapshotStore();
        var key = new ProjectionSnapshotKey(ProjectionScopeKind.ApprovalQueue, "thread-approval-001");
        var participant = new ParticipantRef(new ParticipantId("participant-approval-001"), ParticipantKind.Human, "user");
        await store.UpsertAsync(
            key,
            new ProjectionDelta(
                new ApprovalQueueProjectionPayload(
                    new ApprovalQueueProjection(new[]
                    {
                        new ApprovalQueueItem(
                            new ApprovalId("approval-001"),
                            "shell 执行审批",
                            "需要执行命令",
                            participant,
                            new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero)),
                    }))),
            CancellationToken.None);

        await store.ResetAsync(
            new ProjectionReset(
                ProjectionScopeKind.ApprovalQueue,
                "thread-approval-001",
                "governance_queue_rebuilt",
                new ProjectionCursor("cursor-reset-001")),
            CancellationToken.None);

        var snapshot = await store.GetAsync(key, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Version);
        Assert.Null(snapshot.Delta);
        Assert.Equal("governance_queue_rebuilt", snapshot.Reset?.Reason);
        Assert.Equal("cursor-reset-001", snapshot.Reset?.Cursor?.Value);
    }

    [Fact]
    public async Task ExecutionTraceStore_WhenAttemptAuditAndCheckpointAppended_ReadsComposedTrace()
    {
        var store = new InMemoryExecutionTraceStore();
        var traceId = new ExecutionTraceId("trace-001");
        var executionId = new ExecutionId("execution-001");

        await store.AppendAttemptAsync(
            traceId,
            executionId,
            new AttemptSummary(
                executionId,
                1,
                Succeeded: false,
                new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        await store.AppendAuditRecordAsync(
            traceId,
            executionId,
            new AuditRecord(
                new AuditRecordId("audit-001"),
                "provider",
                "provider reconnect scheduled"),
            CancellationToken.None);

        await store.AppendRecoveryCheckpointAsync(
            traceId,
            executionId,
            new RecoveryCheckpoint(
                executionId,
                "provider_stream",
                StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["attempt"] = StructuredValue.FromNumber("1"),
                })),
            CancellationToken.None);

        var trace = await store.GetAsync(traceId, CancellationToken.None);

        Assert.NotNull(trace);
        Assert.Equal("execution-001", trace!.ExecutionId.Value);
        Assert.Single(trace.Attempts);
        Assert.Single(trace.AuditTrail);
        Assert.Single(trace.RecoveryCheckpoints);
        Assert.Equal("provider_stream", trace.RecoveryCheckpoints[0].Stage);
    }

    [Fact]
    public async Task ExecutionTraceStore_WhenAttemptsListedByExecutionId_ReturnsOrderedMatchingAttempts()
    {
        var store = new InMemoryExecutionTraceStore();
        var executionId = new ExecutionId("execution-attempt-list-001");
        var otherExecutionId = new ExecutionId("execution-attempt-list-other");

        await store.AppendAttemptAsync(
            new ExecutionTraceId("trace-attempt-list-002"),
            executionId,
            new AttemptSummary(
                executionId,
                2,
                Succeeded: false,
                new DateTimeOffset(2026, 4, 14, 10, 2, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await store.AppendAttemptAsync(
            new ExecutionTraceId("trace-attempt-list-001"),
            executionId,
            new AttemptSummary(
                executionId,
                1,
                Succeeded: true,
                new DateTimeOffset(2026, 4, 14, 10, 1, 0, TimeSpan.Zero)),
            CancellationToken.None);
        await store.AppendAttemptAsync(
            new ExecutionTraceId("trace-attempt-list-other"),
            otherExecutionId,
            new AttemptSummary(
                otherExecutionId,
                1,
                Succeeded: true,
                new DateTimeOffset(2026, 4, 14, 10, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        var attempts = await store.ListAttemptsAsync(executionId, CancellationToken.None);

        Assert.Collection(
            attempts,
            attempt =>
            {
                Assert.Equal(1, attempt.AttemptNumber);
                Assert.True(attempt.Succeeded);
            },
            attempt =>
            {
                Assert.Equal(2, attempt.AttemptNumber);
                Assert.False(attempt.Succeeded);
            });
    }

    [Fact]
    public async Task ReplayCheckpointStore_WhenCheckpointSavedLoadedAndRemoved_ReflectsLatestState()
    {
        var store = new InMemoryReplayCheckpointStore();
        var checkpoint = new RecoveryCheckpoint(
            new ExecutionId("execution-replay-001"),
            "approval_gate",
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["callId"] = StructuredValue.FromString("call-001"),
            }));
        var key = ReplayCheckpointKey.From(checkpoint);

        await store.SaveAsync(checkpoint, CancellationToken.None);
        var loaded = await store.GetAsync(key, CancellationToken.None);
        await store.RemoveAsync(key, CancellationToken.None);
        var removed = await store.GetAsync(key, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("approval_gate", loaded!.Stage);
        Assert.Equal("call-001", loaded.State?.Properties["callId"].StringValue);
        Assert.Null(removed);
    }

    [Fact]
    public async Task ProjectionRuntimeStores_WhenRecoveryCheckpointRecorded_UpdatesTraceHistoryAndReplayCurrentState()
    {
        var snapshots = new InMemoryProjectionSnapshotStore();
        var traces = new InMemoryExecutionTraceStore();
        var replay = new InMemoryReplayCheckpointStore();
        var stores = new InMemoryProjectionRuntimeStores(snapshots, traces, replay);
        var traceId = new ExecutionTraceId("trace-runtime-001");
        var executionId = new ExecutionId("execution-runtime-001");
        var checkpoint = new RecoveryCheckpoint(
            executionId,
            "provider_stream",
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["attempt"] = StructuredValue.FromNumber("2"),
            }));

        var trace = await stores.RecordRecoveryCheckpointAsync(traceId, executionId, checkpoint, CancellationToken.None);
        var storedTrace = await stores.ExecutionTraces.GetAsync(traceId, CancellationToken.None);
        var currentCheckpoint = await stores.ReplayCheckpoints.GetAsync(ReplayCheckpointKey.From(checkpoint), CancellationToken.None);

        Assert.Same(snapshots, stores.Snapshots);
        Assert.Same(traces, stores.ExecutionTraces);
        Assert.Same(replay, stores.ReplayCheckpoints);
        Assert.Single(trace.RecoveryCheckpoints);
        Assert.NotNull(storedTrace);
        Assert.Single(storedTrace!.RecoveryCheckpoints);
        Assert.Equal("provider_stream", storedTrace.RecoveryCheckpoints[0].Stage);
        Assert.NotNull(currentCheckpoint);
        Assert.Equal("2", currentCheckpoint!.State?.Properties["attempt"].NumberValue);
    }

    [Fact]
    public async Task ProjectionRuntimeStores_WhenReplayCheckpointRemoved_LeavesTraceHistoryUntouched()
    {
        var stores = new InMemoryProjectionRuntimeStores();
        var traceId = new ExecutionTraceId("trace-runtime-002");
        var executionId = new ExecutionId("execution-runtime-002");
        var checkpoint = new RecoveryCheckpoint(
            executionId,
            "approval_gate",
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["callId"] = StructuredValue.FromString("call-002"),
            }));
        var key = ReplayCheckpointKey.From(checkpoint);

        await stores.RecordRecoveryCheckpointAsync(traceId, executionId, checkpoint, CancellationToken.None);
        await stores.ReplayCheckpoints.RemoveAsync(key, CancellationToken.None);

        var trace = await stores.ExecutionTraces.GetAsync(traceId, CancellationToken.None);
        var replayCheckpoint = await stores.ReplayCheckpoints.GetAsync(key, CancellationToken.None);

        Assert.NotNull(trace);
        Assert.Single(trace!.RecoveryCheckpoints);
        Assert.Equal("approval_gate", trace.RecoveryCheckpoints[0].Stage);
        Assert.Null(replayCheckpoint);
    }

    [Fact]
    public async Task ProjectionRuntimeStores_WhenRecoveryCheckpointRecorded_DoesNotMaterializeProjectionSnapshots()
    {
        var stores = new InMemoryProjectionRuntimeStores();
        var traceId = new ExecutionTraceId("trace-runtime-003");
        var executionId = new ExecutionId("execution-runtime-003");
        var checkpoint = new RecoveryCheckpoint(
            executionId,
            "tool_resume",
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["toolCallId"] = StructuredValue.FromString("tool-003"),
            }));
        var snapshotKey = new ProjectionSnapshotKey(ProjectionScopeKind.Thread, "thread-runtime-003");

        await stores.RecordRecoveryCheckpointAsync(traceId, executionId, checkpoint, CancellationToken.None);

        var snapshot = await stores.Snapshots.GetAsync(snapshotKey, CancellationToken.None);
        var trace = await stores.ExecutionTraces.GetAsync(traceId, CancellationToken.None);

        Assert.Null(snapshot);
        Assert.NotNull(trace);
        Assert.Empty(trace!.Attempts);
        Assert.Empty(trace.AuditTrail);
        Assert.Single(trace.RecoveryCheckpoints);
    }
}
