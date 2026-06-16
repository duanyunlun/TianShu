using System.Text.Json;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Collaboration;
using TianShu.Contracts.Execution;
using TianShu.Contracts.Interactions;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Participants;
using TianShu.Contracts.Primitives;
using TianShu.ControlPlane;
using TianShu.Kernel;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using ContractExecutionContext = TianShu.Contracts.Execution.ExecutionContext;

namespace TianShu.AppHost.Tests;

public sealed class KernelThreadStoreTests
{
    [Fact]
    public async Task CreateThreadAndReload_ShouldPersistThreadRecord()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_test_001", "D:/Repo", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(created.Id, "turn_1", "你好", "收到", "completed", CancellationToken.None);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var list = await reloaded.ListThreadsAsync(10, null, archived: false, CancellationToken.None);

            Assert.Single(list);
            Assert.Equal("thread_test_001", list[0].Id);
            Assert.Equal("D:/Repo", list[0].Cwd);
            Assert.Equal("你好", list[0].LastUserMessage);
            Assert.Equal("收到", list[0].LastAssistantMessage);
            Assert.Single(list[0].Turns);
            Assert.Equal("turn_1", list[0].Turns[0].Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ActiveTurnInteractionEnvelope_ShouldPersistAcrossThreadStoreAndRollout()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(1_746_000_000_000);
            var envelope = new InteractionEnvelopeRef(
                new InteractionEnvelopeId("interaction_turn_state_001"),
                InteractionSourceKind.Host,
                "cli",
                createdAt);

            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_turn_envelope_001", "D:/Repo", CancellationToken.None);
            _ = await store.UpsertActiveTurnAsync(
                created.Id,
                new KernelTurnRecord
                {
                    Id = "turn_env_001",
                    StartedAt = createdAt,
                    CompletedAt = createdAt,
                    Status = "inProgress",
                    UserMessage = "继续",
                    InteractionEnvelope = envelope,
                },
                CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(
                created.Id,
                "turn_env_001",
                "继续",
                "收到",
                "completed",
                CancellationToken.None,
                startedAt: createdAt,
                completedAt: createdAt.AddSeconds(1));
            await store.RolloutRecorder.AppendTurnResultAsync(
                created.Id,
                "turn_env_001",
                "completed",
                "继续",
                "收到",
                CancellationToken.None,
                items: null,
                error: null,
                startedAt: createdAt,
                completedAt: createdAt.AddSeconds(1),
                interactionEnvelope: envelope);
            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var record = await reloaded.GetThreadAsync(created.Id, CancellationToken.None);

            var turn = Assert.Single(record!.Turns);
            Assert.NotNull(turn.InteractionEnvelope);
            Assert.Equal("interaction_turn_state_001", turn.InteractionEnvelope!.Id.Value);
            Assert.Equal(InteractionSourceKind.Host, turn.InteractionEnvelope.SourceKind);
            Assert.Equal("cli", turn.InteractionEnvelope.Surface);

            var rolloutRecord = await reloaded.RolloutRecorder.RehydrateThreadAsync(
                reloaded.RolloutRecorder.GetRolloutPath(created.Id),
                CancellationToken.None);
            var rolloutTurn = Assert.Single(rolloutRecord!.Turns);
            Assert.NotNull(rolloutTurn.InteractionEnvelope);
            Assert.Equal("interaction_turn_state_001", rolloutTurn.InteractionEnvelope!.Id.Value);
            Assert.Equal(InteractionSourceKind.Host, rolloutTurn.InteractionEnvelope.SourceKind);
            Assert.Equal("cli", rolloutTurn.InteractionEnvelope.Surface);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DisableEnabledThreadMemoryModesAsync_ShouldPersistDisabledState()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            _ = await store.CreateThreadAsync("thread_memory_enabled", "D:/Repo", CancellationToken.None);
            var disabledThread = await store.CreateThreadAsync("thread_memory_disabled", "D:/Repo", CancellationToken.None);
            disabledThread.MemoryMode = "disabled";
            _ = await store.UpsertThreadAsync(disabledThread, CancellationToken.None);

            var changedCount = await store.DisableEnabledThreadMemoryModesAsync(CancellationToken.None);

            Assert.Equal(1, changedCount);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var enabledThread = await reloaded.GetThreadAsync("thread_memory_enabled", CancellationToken.None);
            var stillDisabledThread = await reloaded.GetThreadAsync("thread_memory_disabled", CancellationToken.None);

            Assert.NotNull(enabledThread);
            Assert.NotNull(stillDisabledThread);
            Assert.Equal("disabled", enabledThread!.MemoryMode);
            Assert.Equal("disabled", stillDisabledThread!.MemoryMode);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetThreadMemoryModeAsync_ShouldMapLegacyStorageModesToContract()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            _ = await store.CreateThreadAsync("thread_memory_enabled", "D:/Repo", CancellationToken.None);

            var disabledThread = await store.CreateThreadAsync("thread_memory_disabled", "D:/Repo", CancellationToken.None);
            disabledThread.MemoryMode = "disabled";
            _ = await store.UpsertThreadAsync(disabledThread, CancellationToken.None);

            var readOnlyThread = await store.CreateThreadAsync("thread_memory_readonly", "D:/Repo", CancellationToken.None);
            readOnlyThread.MemoryMode = "read-only";
            _ = await store.UpsertThreadAsync(readOnlyThread, CancellationToken.None);

            var pollutedThread = await store.CreateThreadAsync("thread_memory_polluted", "D:/Repo", CancellationToken.None);
            pollutedThread.MemoryMode = "polluted";
            _ = await store.UpsertThreadAsync(pollutedThread, CancellationToken.None);

            _ = await store.CreateThreadAsync("thread_memory_ephemeral", "D:/Repo", CancellationToken.None, ephemeral: true);

            Assert.Equal(ThreadMemoryMode.ReadWrite, await store.GetThreadMemoryModeAsync("thread_memory_enabled", CancellationToken.None));
            Assert.Equal(ThreadMemoryMode.Disabled, await store.GetThreadMemoryModeAsync("thread_memory_disabled", CancellationToken.None));
            Assert.Equal(ThreadMemoryMode.ReadOnly, await store.GetThreadMemoryModeAsync("thread_memory_readonly", CancellationToken.None));
            Assert.Equal(ThreadMemoryMode.ReadOnly, await store.GetThreadMemoryModeAsync("thread_memory_polluted", CancellationToken.None));
            Assert.Equal(ThreadMemoryMode.Ephemeral, await store.GetThreadMemoryModeAsync("thread_memory_ephemeral", CancellationToken.None));
            Assert.Null(await store.GetThreadMemoryModeAsync("thread_memory_missing", CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListThreads_WithCwdFilter_ShouldOnlyReturnMatchedThreads()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            await store.CreateThreadAsync("thread_a", "D:/Repo/A", CancellationToken.None);
            await store.CreateThreadAsync("thread_b", "D:/Repo/B", CancellationToken.None);

            var list = await store.ListThreadsAsync(10, "D:/Repo/B", archived: false, CancellationToken.None);
            Assert.Single(list);
            Assert.Equal("thread_b", list[0].Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListThreads_WithTypedSourceKindFilter_ShouldDifferentiateSubAgentKinds()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        static KernelThreadConfigSnapshot BuildSnapshot(KernelThreadRecord thread, KernelSessionSource source)
            => KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(thread, "gpt-5", "openai", "on-request")
                    .ApplyThreadStart(new KernelThreadStartRequest { SessionSource = source })
                    .Build());

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var reviewThread = await store.CreateThreadAsync("thread_subagent_review", "D:/Repo", CancellationToken.None);
            reviewThread.ConfigSnapshot = BuildSnapshot(
                reviewThread,
                KernelSessionSource.SubAgent(KernelSubAgentSource.Review));
            _ = await store.UpsertThreadAsync(reviewThread, CancellationToken.None);

            var spawnThread = await store.CreateThreadAsync("thread_subagent_spawn", "D:/Repo", CancellationToken.None);
            spawnThread.ConfigSnapshot = BuildSnapshot(
                spawnThread,
                KernelSessionSource.SubAgent(
                    KernelSubAgentSource.ThreadSpawn("thread-parent-001", 2, "reviewer", "review")));
            _ = await store.UpsertThreadAsync(spawnThread, CancellationToken.None);

            var page = await store.ListThreadsAsync(
                new KernelThreadListQuery(
                    Limit: 10,
                    Cursor: null,
                    Cwd: null,
                    Archived: false,
                    SortKey: "updated_at",
                    ModelProviders: Array.Empty<string>(),
                    SourceKinds: [KernelThreadSourceKind.SubAgentReview],
                    SearchTerm: null,
                    DefaultModelProvider: "openai",
                    DefaultSource: KernelSessionSource.AppServer),
                CancellationToken.None);

            var thread = Assert.Single(page.Data);
            Assert.Equal("thread_subagent_review", thread.Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListThreads_WithSubAgentOtherFilter_ShouldExcludeMemoryConsolidation()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        static KernelThreadConfigSnapshot BuildSnapshot(KernelThreadRecord thread, KernelSessionSource source)
            => KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(thread, "gpt-5", "openai", "on-request")
                    .ApplyThreadStart(new KernelThreadStartRequest { SessionSource = source })
                    .Build());

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var memoryThread = await store.CreateThreadAsync("thread_subagent_memory", "D:/Repo", CancellationToken.None);
            memoryThread.ConfigSnapshot = BuildSnapshot(
                memoryThread,
                KernelSessionSource.SubAgent(KernelSubAgentSource.MemoryConsolidation));
            _ = await store.UpsertThreadAsync(memoryThread, CancellationToken.None);

            var otherThread = await store.CreateThreadAsync("thread_subagent_other", "D:/Repo", CancellationToken.None);
            otherThread.ConfigSnapshot = BuildSnapshot(
                otherThread,
                KernelSessionSource.SubAgent(KernelSubAgentSource.Other("handoff")));
            _ = await store.UpsertThreadAsync(otherThread, CancellationToken.None);

            var page = await store.ListThreadsAsync(
                new KernelThreadListQuery(
                    Limit: 10,
                    Cursor: null,
                    Cwd: null,
                    Archived: false,
                    SortKey: "updated_at",
                    ModelProviders: Array.Empty<string>(),
                    SourceKinds: [KernelThreadSourceKind.SubAgentOther],
                    SearchTerm: null,
                    DefaultModelProvider: "openai",
                    DefaultSource: KernelSessionSource.VsCode),
                CancellationToken.None);

            var thread = Assert.Single(page.Data);
            Assert.Equal("thread_subagent_other", thread.Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ListThreads_WithMissingSessionSource_ShouldUseVsCodeFallback()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var legacyThread = await store.CreateThreadAsync("thread_missing_source", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(legacyThread, "gpt-5", "openai", "on-request")
                    .Build());
            legacyThread.ConfigSnapshot = snapshot with
            {
                SessionSource = null!,
            };
            _ = await store.UpsertThreadAsync(legacyThread, CancellationToken.None);

            var page = await store.ListThreadsAsync(
                new KernelThreadListQuery(
                    Limit: 10,
                    Cursor: null,
                    Cwd: null,
                    Archived: false,
                    SortKey: "updated_at",
                    ModelProviders: Array.Empty<string>(),
                    SourceKinds: [KernelThreadSourceKind.VsCode],
                    SearchTerm: null,
                    DefaultModelProvider: "openai",
                    DefaultSource: KernelSessionSource.VsCode),
                CancellationToken.None);

            var thread = Assert.Single(page.Data);
            Assert.Equal("thread_missing_source", thread.Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpsertThreadAsync_WithMissingSessionSource_ShouldBackfillVsCodeIntoSnapshot()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var legacyThread = await store.CreateThreadAsync("thread_missing_source_backfill", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(legacyThread, "gpt-5", "openai", "on-request")
                    .Build());
            legacyThread.ConfigSnapshot = snapshot with
            {
                SessionSource = null!,
            };

            var updated = await store.UpsertThreadAsync(legacyThread, CancellationToken.None);
            Assert.Equal(KernelSessionSource.VsCode, updated.ConfigSnapshot!.SessionSource);

            var reloaded = await store.GetThreadAsync("thread_missing_source_backfill", CancellationToken.None);
            Assert.NotNull(reloaded);
            Assert.Equal(KernelSessionSource.VsCode, reloaded!.ConfigSnapshot!.SessionSource);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ArchiveAndUnarchive_ShouldMoveThreadAcrossFilters()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_archive", "D:/Repo", CancellationToken.None);
            _ = await store.SetThreadArchivedAsync(created.Id, true, CancellationToken.None);

            var active = await store.ListThreadsAsync(10, null, archived: false, CancellationToken.None);
            var archived = await store.ListThreadsAsync(10, null, archived: true, CancellationToken.None);

            Assert.Empty(active);
            Assert.Single(archived);
            Assert.Equal(created.Id, archived[0].Id);

            _ = await store.SetThreadArchivedAsync(created.Id, false, CancellationToken.None);
            active = await store.ListThreadsAsync(10, null, archived: false, CancellationToken.None);
            Assert.Single(active);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ForkAndRollback_ShouldPreserveAndTrimTurns()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");
        KernelThreadStore? store = null;

        try
        {
            store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var source = await store.CreateThreadAsync("thread_source", "D:/Repo", CancellationToken.None);
            _ = await store.SetThreadNameAsync(source.Id, "源线程名称", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(source.Id, "turn_1", "Q1", "A1", "completed", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(source.Id, "turn_2", "Q2", "A2", "completed", CancellationToken.None);

            var forked = await store.ForkThreadAsync(source.Id, "thread_fork", null, CancellationToken.None);
            Assert.NotNull(forked);
            Assert.Equal(2, forked!.Turns.Count);
            Assert.Equal(source.Id, forked.ForkedFromThreadId);
            Assert.Null(forked.Name);

            var rolled = await store.RollbackThreadTurnsAsync(forked.Id, 1, CancellationToken.None);
            Assert.NotNull(rolled);
            Assert.Single(rolled!.Turns);
            Assert.Equal("turn_1", rolled.Turns[0].Id);
            Assert.Equal("A1", rolled.LastAssistantMessage);
        }
        finally
        {
            if (store is not null)
            {
                await store.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
            }

            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateMetadata_ShouldApplyGitInfoPatch()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_git", "D:/Repo", CancellationToken.None);

            var updated = await store.UpdateThreadGitInfoAsync(
                created.Id,
                hasSha: true,
                sha: "abc123",
                hasBranch: true,
                branch: "main",
                hasOriginUrl: false,
                originUrl: null,
                CancellationToken.None);

            Assert.NotNull(updated);
            Assert.NotNull(updated!.GitInfo);
            Assert.Equal("abc123", updated.GitInfo!.Sha);
            Assert.Equal("main", updated.GitInfo.Branch);

            updated = await store.UpdateThreadGitInfoAsync(
                created.Id,
                hasSha: false,
                sha: null,
                hasBranch: true,
                branch: null,
                hasOriginUrl: false,
                originUrl: null,
                CancellationToken.None);

            Assert.NotNull(updated);
            Assert.NotNull(updated!.GitInfo);
            Assert.Null(updated.GitInfo!.Branch);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadStore_ShouldMirrorThreadStateIntoSqlite()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_sqlite_001", "D:/Repo", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(created.Id, "turn_sqlite_001", "Q", "A", "completed", CancellationToken.None);

            var mirrored = await store.StateStore.GetThreadAsync(created.Id, CancellationToken.None);

            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.Equal(created.Id, mirrored.ThreadId);
            Assert.Single(mirroredPayload.Turns);
            Assert.Equal("A", mirroredPayload.LastAssistantMessage);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadStore_ShouldPersistSessionStateAcrossJsonSqliteAndRollout()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_session_state_001", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder
                    .FromRecord(created, "gpt-5", "openai", "on-request")
                    .Build());
            created.Name = "计划收口会话";
            created.ConfigSnapshot = snapshot with
            {
                CollaborationMode = new KernelCollaborationModeState(
                    KernelCollaborationModeState.PlanMode,
                    new KernelCollaborationModeSettings("gpt-5", "high", "keep planning")),
            };

            var updated = await store.UpsertThreadAsync(created, CancellationToken.None);

            Assert.NotNull(updated.SessionState);
            Assert.Equal("thread_session_state_001", updated.SessionState!.SessionId);
            Assert.Equal("计划收口会话", updated.SessionState.Title);
            Assert.Equal("planning", updated.SessionState.SessionMode);
            Assert.Equal("thread_session_state_001", updated.SessionState.ActiveThreadId);
            Assert.False(updated.SessionState.IsClosed);
            Assert.False(updated.SessionState.HasActiveTurn);

            var mirrored = await store.StateStore.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.NotNull(mirroredPayload.SessionState);
            Assert.Equal("计划收口会话", mirroredPayload.SessionState!.Title);
            Assert.Equal("planning", mirroredPayload.SessionState.SessionMode);

            var jsonReloaded = new KernelThreadStore(file);
            await jsonReloaded.InitializeAsync(CancellationToken.None);
            var fromJson = await jsonReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(fromJson);
            Assert.NotNull(fromJson!.SessionState);
            Assert.Equal("计划收口会话", fromJson.SessionState!.Title);
            Assert.Equal("planning", fromJson.SessionState.SessionMode);

            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);
            File.Delete(file);

            var rolloutReloaded = new KernelThreadStore(file);
            await rolloutReloaded.InitializeAsync(CancellationToken.None);
            var fromRollout = await rolloutReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(fromRollout);
            Assert.NotNull(fromRollout!.SessionState);
            Assert.Equal("thread_session_state_001", fromRollout.SessionState!.SessionId);
            Assert.Equal("计划收口会话", fromRollout.SessionState.Title);
            Assert.Equal("planning", fromRollout.SessionState.SessionMode);
            Assert.Equal("thread_session_state_001", fromRollout.SessionState.ActiveThreadId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadStore_ShouldPersistSessionOrchestrationStateAcrossJsonSqliteAndRollout()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_orchestration_state_001", "D:/Repo", CancellationToken.None);
            var entryPlanner = new SessionCoreLoopEntryPlanner(BuiltInStageDefinitions.All);
            var step = entryPlanner.PlanEntry(
                new SessionOrchestrationInput(
                    new SessionId(created.Id),
                    new ThreadId(created.Id),
                    "turn-orchestration-001",
                    previousStageId: BuiltInStageDefinitions.Coding,
                    requestedStageId: BuiltInStageDefinitions.Review,
                    contextLedgerSegments:
                    [
                        new StageContextSegment(
                            "decision",
                            "coding stage completed with implementation changes",
                            title: "Coding handoff",
                            source: new ResourceRef("turn", "turn-coding-001"),
                            required: true,
                            estimatedTokens: 12),
                    ],
                    contextBudgetTokens: 128),
                static request => new SessionCoreLoopRouteResult(new ModelRouteResolutionResult(
                    "workbench",
                    request.RouteKind,
                    "review-provider",
                    "review-model",
                    0,
                    "openai_chat_completions"))).OrchestrationStep;

            var afterStep = await store.ApplySessionOrchestrationStepAsync(
                created.Id,
                step.Decision,
                step.ContextPackage,
                CancellationToken.None);

            var checkpoint = new SessionCheckpointFactory().Create(
                step,
                StageExecutionState.Completed,
                new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero),
                completedAt: new DateTimeOffset(2026, 6, 9, 8, 1, 0, TimeSpan.Zero),
                summary: "review completed",
                artifactRefs:
                [
                    new ArtifactRef(new ArtifactId("artifact-review-001"), "review.md", "markdown"),
                ],
                diagnostics: StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["executionRequestId"] = "execution-review-001",
                    ["executorBinding"] = "review",
                }),
                nextStageSuggestions: [BuiltInStageDefinitions.Summarization]);

            var afterCheckpoint = await store.AppendStageCheckpointAsync(created.Id, checkpoint, CancellationToken.None);

            Assert.NotNull(afterStep?.SessionState?.Orchestration);
            Assert.NotNull(afterCheckpoint?.SessionState?.Orchestration);
            Assert.Equal(BuiltInStageDefinitions.Review, afterCheckpoint!.SessionState!.Orchestration!.CurrentStageId);
            Assert.Equal(step.Decision.DecisionId, afterCheckpoint.SessionState.Orchestration.LastDecision?.DecisionId);
            Assert.Equal(step.ContextPackage.PackageId, afterCheckpoint.SessionState.Orchestration.LastContextPackage?.PackageId);
            Assert.Equal(2, afterCheckpoint.SessionState.Orchestration.ContextLedgerSegments.Count);
            var checkpointSegment = Assert.Single(
                afterCheckpoint.SessionState.Orchestration.ContextLedgerSegments,
                static segment => segment.Source?.Kind == "stage_checkpoint");
            Assert.Equal(checkpoint.CheckpointId, checkpointSegment.Source?.Key);
            Assert.Contains("review completed", checkpointSegment.Content, StringComparison.Ordinal);
            var storedCheckpoint = Assert.Single(afterCheckpoint.SessionState.Orchestration.Checkpoints);
            Assert.Equal(checkpoint.CheckpointId, storedCheckpoint.CheckpointId);
            Assert.Equal("review completed", storedCheckpoint.Summary);
            Assert.Equal(ModelRouteKind.Review.Value, storedCheckpoint.ModelRouteKind);
            Assert.Equal("artifact-review-001", Assert.Single(storedCheckpoint.ArtifactRefs).Id);
            Assert.Equal("execution-review-001", storedCheckpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
            Assert.Equal("review", storedCheckpoint.Diagnostics?.Properties["executorBinding"].StringValue);

            var mirrored = await store.StateStore.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.Equal(BuiltInStageDefinitions.Review, mirroredPayload.SessionState?.Orchestration?.CurrentStageId);
            Assert.Equal(step.ContextPackage.PackageId, mirroredPayload.SessionState?.Orchestration?.LastContextPackage?.PackageId);
            Assert.Single(mirroredPayload.SessionState!.Orchestration!.Checkpoints);
            Assert.Equal(2, mirroredPayload.SessionState.Orchestration.ContextLedgerSegments.Count);

            var jsonReloaded = new KernelThreadStore(file);
            await jsonReloaded.InitializeAsync(CancellationToken.None);
            var fromJson = await jsonReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.Equal(BuiltInStageDefinitions.Review, fromJson?.SessionState?.Orchestration?.CurrentStageId);
            var jsonCheckpoint = Assert.Single(fromJson!.SessionState!.Orchestration!.Checkpoints);
            Assert.Equal(checkpoint.CheckpointId, jsonCheckpoint.CheckpointId);
            Assert.Equal("execution-review-001", jsonCheckpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
            Assert.Contains(
                fromJson.SessionState.Orchestration.ContextLedgerSegments,
                static segment => segment.Source?.Kind == "stage_checkpoint"
                                  && segment.Content.Contains("review completed", StringComparison.Ordinal));

            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);
            File.Delete(file);

            var rolloutReloaded = new KernelThreadStore(file);
            await rolloutReloaded.InitializeAsync(CancellationToken.None);
            var fromRollout = await rolloutReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(fromRollout?.SessionState?.Orchestration);
            Assert.Equal(BuiltInStageDefinitions.Review, fromRollout!.SessionState!.Orchestration!.CurrentStageId);
            Assert.Equal(step.Decision.DecisionId, fromRollout.SessionState.Orchestration.LastDecision?.DecisionId);
            var rolloutCheckpoint = Assert.Single(fromRollout.SessionState.Orchestration.Checkpoints);
            Assert.Equal(checkpoint.CheckpointId, rolloutCheckpoint.CheckpointId);
            Assert.Equal("execution-review-001", rolloutCheckpoint.Diagnostics?.Properties["executionRequestId"].StringValue);
            Assert.Contains(
                fromRollout.SessionState.Orchestration.ContextLedgerSegments,
                static segment => segment.Source?.Kind == "stage_checkpoint"
                                  && segment.Content.Contains("review completed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ArchiveAndUnarchive_ShouldUpdateSessionClosedState()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_session_archive_001", "D:/Repo", CancellationToken.None);

            var archived = await store.SetThreadArchivedAsync(created.Id, true, CancellationToken.None);
            Assert.NotNull(archived);
            Assert.NotNull(archived!.SessionState);
            Assert.True(archived.SessionState!.IsClosed);
            Assert.Null(archived.SessionState.ActiveThreadId);

            var mirrored = await store.StateStore.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.True(mirroredPayload.SessionState!.IsClosed);
            Assert.Null(mirroredPayload.SessionState.ActiveThreadId);

            var reopened = await store.SetThreadArchivedAsync(created.Id, false, CancellationToken.None);
            Assert.NotNull(reopened);
            Assert.NotNull(reopened!.SessionState);
            Assert.False(reopened.SessionState!.IsClosed);
            Assert.Equal(created.Id, reopened.SessionState.ActiveThreadId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadStore_Initialize_ShouldBackfillExistingJsonIntoSqlite()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_backfill_001", "D:/Repo", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(created.Id, "turn_backfill_001", "Q", "A", "completed", CancellationToken.None);

            File.Delete(store.StateStore.DatabasePath);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var mirrored = await reloaded.StateStore.GetThreadAsync(created.Id, CancellationToken.None);

            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.Equal(created.Id, mirrored.ThreadId);
            Assert.Single(mirroredPayload.Turns);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task ThreadStore_ShouldRehydrateFromRolloutWhenJsonMissing()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_rollout_001", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "on-request").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);
            await store.RolloutRecorder.AppendTurnResultAsync(created.Id, "turn_rollout_001", "completed", "Q", "A", CancellationToken.None);

            File.Delete(file);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var thread = await reloaded.GetThreadAsync(created.Id, CancellationToken.None);

            Assert.NotNull(thread);
            Assert.Single(thread!.Turns);
            Assert.Equal("A", thread.LastAssistantMessage);
        }
        finally
        {
            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task ThreadStore_ShouldRehydratePendingInputStateFromRolloutWhenJsonMissing()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_rollout_pending_001", root, CancellationToken.None);
            var updated = await store.SetThreadPendingInputStateAsync(
                created.Id,
                new KernelPendingInputStateRecord(
                    Entries:
                    [
                        new KernelPendingInputStateEntryRecord(
                            "corr-rollout-pending-interrupt-001",
                            "Interrupt",
                            "Interrupt",
                            "interrupt_requested",
                            "turn-rollout-pending-001",
                            "turn-rollout-pending-interrupt-001",
                            new KernelPendingFollowUpCompareKeyRecord("rollout interrupt message", 0),
                            "QueuedUserMessage"),
                    ],
                    InterruptRequestPending: false,
                    SubmitPendingSteersAfterInterrupt: true,
                    PendingSteers:
                    [
                        new KernelPendingInputStateEntryRecord(
                            "corr-rollout-pending-001",
                            "Steer",
                            "Steer",
                            "awaiting_commit",
                            "turn-rollout-pending-001",
                            null,
                            new KernelPendingFollowUpCompareKeyRecord("rollout pending message", 0),
                            "PendingSteer"),
                    ]),
                CancellationToken.None);

            Assert.NotNull(updated);
            Assert.NotNull(updated!.PendingInputState);
            Assert.False(updated.PendingInputState!.InterruptRequestPending);
            Assert.True(updated.PendingInputState.SubmitPendingSteersAfterInterrupt);
            Assert.Equal(
                "corr-rollout-pending-interrupt-001",
                Assert.Single(updated.PendingInputState.Entries).CorrelationId);
            Assert.Equal(
                "corr-rollout-pending-001",
                Assert.Single(updated.PendingInputState.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);

            File.Delete(file);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var thread = await reloaded.GetThreadAsync(created.Id, CancellationToken.None);

            Assert.NotNull(thread);
            Assert.NotNull(thread!.PendingInputState);
            Assert.False(thread.PendingInputState!.InterruptRequestPending);
            Assert.True(thread.PendingInputState.SubmitPendingSteersAfterInterrupt);
            Assert.Equal(
                "corr-rollout-pending-interrupt-001",
                Assert.Single(thread.PendingInputState.Entries).CorrelationId);
            var entry = Assert.Single(thread.PendingInputState.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>());
            Assert.Equal("corr-rollout-pending-001", entry.CorrelationId);
            Assert.Equal("awaiting_commit", entry.LifecycleState);
            Assert.Equal("PendingSteer", entry.PendingBucket);
            Assert.Null(entry.CompareKey);
            Assert.Empty(entry.Inputs ?? Array.Empty<KernelPendingUserInputRecord>());
        }
        finally
        {
            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_ShouldPreserveExplicitFlagsFromState()
    {
        var state = KernelPendingInputStateFactory.Normalize(
            new KernelPendingInputStateRecord(
                Entries: Array.Empty<KernelPendingInputStateEntryRecord>(),
                InterruptRequestPending: true,
                SubmitPendingSteersAfterInterrupt: true,
                PendingSteers:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-flags-001",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-explicit-kernel-flags-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel explicit flags", 0),
                        "PendingSteer"),
                ]));

        Assert.NotNull(state);
        Assert.True(state!.InterruptRequestPending);
        Assert.True(state.SubmitPendingSteersAfterInterrupt);
        Assert.Empty(state.Entries);
        Assert.Empty(state.QueuedUserMessages ?? Array.Empty<KernelPendingInputStateEntryRecord>());
        Assert.Equal(
            "corr-explicit-kernel-flags-001",
            Assert.Single(state.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenSubmitFlagExplicitlyFalse_ShouldNotDeriveTrueFromEntries()
    {
        var state = KernelPendingInputStateFactory.Normalize(
            new KernelPendingInputStateRecord(
                Entries:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-interrupt-false-001",
                        "Interrupt",
                        "Interrupt",
                        "interrupt_requested",
                        "turn-explicit-kernel-false-001",
                        "turn-explicit-kernel-false-001",
                        new KernelPendingFollowUpCompareKeyRecord("kernel interrupt", 0),
                        "QueuedUserMessage"),
                ],
                InterruptRequestPending: true,
                SubmitPendingSteersAfterInterrupt: false,
                PendingSteers:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-steer-false-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-explicit-kernel-false-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel steer", 0),
                        "PendingSteer"),
                ]));

        Assert.NotNull(state);
        Assert.True(state!.InterruptRequestPending);
        Assert.False(state.SubmitPendingSteersAfterInterrupt);
        var interruptEntry = Assert.Single(state.Entries);
        Assert.Equal("corr-explicit-kernel-interrupt-false-001", interruptEntry.CorrelationId);
        Assert.Equal(
            "corr-explicit-kernel-steer-false-001",
            Assert.Single(state.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenInterruptFlagExplicitlyFalse_ShouldNotDeriveTrueFromEntries()
    {
        var state = KernelPendingInputStateFactory.Normalize(
            new KernelPendingInputStateRecord(
                Entries:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-interrupt-flag-false-001",
                        "Interrupt",
                        "Interrupt",
                        "interrupt_requested",
                        "turn-explicit-kernel-interrupt-flag-false-001",
                        "turn-explicit-kernel-interrupt-flag-false-001",
                        new KernelPendingFollowUpCompareKeyRecord("kernel interrupt false", 0),
                        "QueuedUserMessage"),
                ],
                InterruptRequestPending: false,
                SubmitPendingSteersAfterInterrupt: true,
                PendingSteers:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-interrupt-flag-false-steer-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-explicit-kernel-interrupt-flag-false-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel steer", 0),
                        "PendingSteer"),
                ]));

        Assert.NotNull(state);
        Assert.False(state!.InterruptRequestPending);
        Assert.True(state.SubmitPendingSteersAfterInterrupt);
        var interruptEntry = Assert.Single(state.Entries);
        Assert.Equal("corr-explicit-kernel-interrupt-flag-false-001", interruptEntry.CorrelationId);
        Assert.Equal(
            "corr-explicit-kernel-interrupt-flag-false-steer-001",
            Assert.Single(state.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenLegacyEntriesContainQueuedAndSteerItems_ShouldNotPromoteTopLevelCollections()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "entries": [
                {
                  "correlationId": "corr-kernel-legacy-interrupt-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Interrupt",
                  "lifecycleState": "interrupt_requested",
                  "expectedTurnId": "turn-kernel-legacy-001",
                  "turnId": "turn-kernel-legacy-interrupt-001",
                  "compareKey": { "message": "kernel legacy interrupt", "imageCount": 0 },
                  "pendingBucket": "QueuedUserMessage"
                },
                {
                  "correlationId": "corr-kernel-legacy-queue-001",
                  "requestedMode": "Queue",
                  "effectiveMode": "Queue",
                  "lifecycleState": "queued",
                  "expectedTurnId": "turn-kernel-legacy-001",
                  "turnId": null,
                  "compareKey": { "message": "kernel legacy queue", "imageCount": 0 },
                  "pendingBucket": "QueuedUserMessage"
                },
                {
                  "correlationId": "corr-kernel-legacy-steer-001",
                  "requestedMode": "Steer",
                  "effectiveMode": "Steer",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-kernel-legacy-001",
                  "turnId": null,
                  "compareKey": { "message": "kernel legacy steer", "imageCount": 0 },
                  "pendingBucket": "PendingSteer"
                }
              ],
              "interruptRequestPending": true,
              "submitPendingSteersAfterInterrupt": true
            }
            """);

        Assert.True(KernelPendingInputStateFactory.TryRead(document.RootElement, out var state));

        Assert.NotNull(state);
        Assert.True(state!.InterruptRequestPending);
        Assert.False(state.SubmitPendingSteersAfterInterrupt);
        Assert.Equal("corr-kernel-legacy-interrupt-001", Assert.Single(state.Entries).CorrelationId);
        Assert.Empty(state.QueuedUserMessages ?? Array.Empty<KernelPendingInputStateEntryRecord>());
        Assert.Empty(state.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>());
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenExplicitQueuesPresent_ShouldPromoteThemToTopLevelCollections()
    {
        var state = KernelPendingInputStateFactory.Normalize(
            new KernelPendingInputStateRecord(
                Entries:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-queues-interrupt-001",
                        "Interrupt",
                        "Interrupt",
                        "interrupt_requested",
                        "turn-explicit-kernel-queues-001",
                        "turn-explicit-kernel-queues-001",
                        new KernelPendingFollowUpCompareKeyRecord("kernel interrupt entry", 0),
                        "QueuedUserMessage"),
                ],
                InterruptRequestPending: true,
                SubmitPendingSteersAfterInterrupt: true,
                QueuedUserMessages:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-queues-queue-001",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-explicit-kernel-queues-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel queue entry", 0),
                        "QueuedUserMessage"),
                ],
                PendingSteers:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-queues-steer-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-explicit-kernel-queues-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel steer entry", 0),
                        "PendingSteer"),
                ]));

        Assert.NotNull(state);
        Assert.True(state!.InterruptRequestPending);
        Assert.True(state.SubmitPendingSteersAfterInterrupt);
        Assert.Equal(
            "corr-explicit-kernel-queues-queue-001",
            Assert.Single(state.QueuedUserMessages ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);
        Assert.Equal(
            "corr-explicit-kernel-queues-steer-001",
            Assert.Single(state.PendingSteers ?? Array.Empty<KernelPendingInputStateEntryRecord>()).CorrelationId);
        var entry = Assert.Single(state.Entries);
        Assert.Equal("corr-explicit-kernel-queues-interrupt-001", entry.CorrelationId);
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenTopLevelCollectionsExplicitlyEmpty_ShouldNotDeriveThemFromEntries()
    {
        var state = KernelPendingInputStateFactory.Normalize(
            new KernelPendingInputStateRecord(
                Entries:
                [
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-empty-queue-001",
                        "Queue",
                        "Queue",
                        "awaiting_commit",
                        "turn-explicit-kernel-empty-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel stale queue projection", 0),
                        "QueuedUserMessage"),
                    new KernelPendingInputStateEntryRecord(
                        "corr-explicit-kernel-empty-steer-001",
                        "Steer",
                        "Steer",
                        "awaiting_commit",
                        "turn-explicit-kernel-empty-001",
                        null,
                        new KernelPendingFollowUpCompareKeyRecord("kernel stale steer projection", 0),
                        "PendingSteer"),
                ],
                InterruptRequestPending: false,
                SubmitPendingSteersAfterInterrupt: false,
                QueuedUserMessages: Array.Empty<KernelPendingInputStateEntryRecord>(),
                PendingSteers: Array.Empty<KernelPendingInputStateEntryRecord>()));

        Assert.Null(state);
    }

    [Fact]
    public void PendingInputStateFactory_Normalize_WhenLegacyInterruptEntryReachedAwaitingCommit_ShouldNotPromoteItToQueuedUserMessages()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "entries": [
                {
                  "correlationId": "corr-kernel-legacy-interrupt-awaiting-001",
                  "requestedMode": "Interrupt",
                  "effectiveMode": "Queue",
                  "lifecycleState": "awaiting_commit",
                  "expectedTurnId": "turn-kernel-legacy-interrupt-awaiting-001",
                  "turnId": "turn-kernel-legacy-interrupt-awaiting-002",
                  "compareKey": { "message": "kernel legacy interrupt follow-up", "imageCount": 0 },
                  "pendingBucket": "QueuedUserMessage"
                }
              ],
              "interruptRequestPending": false,
              "submitPendingSteersAfterInterrupt": false
            }
            """);

        Assert.True(KernelPendingInputStateFactory.TryRead(document.RootElement, out var state));
        Assert.Null(state);
    }

    [Fact]
    public async Task RolloutRecorder_ShouldPersistChineseTextWithoutUnicodeEscaping()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_rollout_utf8_001", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "on-request").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);
            await store.RolloutRecorder.AppendTurnResultAsync(
                created.Id,
                "turn_rollout_utf8_001",
                "completed",
                "当前工作目录是什么？",
                "当前工作目录是 D:/Repo。",
                CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            var rolloutText = await File.ReadAllTextAsync(store.RolloutRecorder.GetRolloutPath(created.Id), CancellationToken.None);

            Assert.Contains("当前工作目录是什么？", rolloutText, StringComparison.Ordinal);
            Assert.Contains("当前工作目录是 D:/Repo。", rolloutText, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u5F53\\u524D\\u5DE5\\u4F5C\\u76EE\\u5F55\\u662F", rolloutText, StringComparison.Ordinal);
            await store.RolloutRecorder.DeleteThreadAsync(created.Id, CancellationToken.None);
        }
        finally
        {
            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task RolloutRecorder_ShouldKeepWriterHandleOpenUntilExplicitClose()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_rollout_live_reader_001", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "never").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);

            var rolloutPath = store.RolloutRecorder.GetRolloutPath(created.Id);
            Assert.ThrowsAny<IOException>(() => new FileStream(
                rolloutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read));

            await store.RolloutRecorder.AppendTurnResultAsync(
                created.Id,
                "turn_rollout_live_reader_001",
                "completed",
                "Q",
                "A",
                CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            using (var readerHandle = new FileStream(
                rolloutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                var rolloutText = await File.ReadAllTextAsync(rolloutPath, CancellationToken.None);
                Assert.Contains("\"type\":\"session_meta\"", rolloutText, StringComparison.Ordinal);
                Assert.Contains("\"type\":\"turn\"", rolloutText, StringComparison.Ordinal);
                Assert.Contains("\"assistantMessage\":\"A\"", rolloutText, StringComparison.Ordinal);
            }
            await store.RolloutRecorder.DeleteThreadAsync(created.Id, CancellationToken.None);
        }
        finally
        {
            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task RolloutRecorder_ShouldPersistExecutionRequestsAndEvents()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);
            var created = await store.CreateThreadAsync("thread_execution_rollout_001", "D:/Repo", CancellationToken.None);
            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "on-request").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);

            var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(1_744_071_234_000);
            var context = new ContractExecutionContext(
                new CollaborationSpaceRef(
                    new CollaborationSpaceId("tianshu-runtime"),
                    "tianshu-runtime",
                    "TianShu Runtime"),
                new InteractionEnvelopeRef(
                    new InteractionEnvelopeId("interaction_execution_rollout_001"),
                    InteractionSourceKind.Host,
                    "kernel-app-server",
                    createdAt),
                new ParticipantRef(
                    new ParticipantId("kernel-app-server"),
                    ParticipantKind.Service,
                    "Kernel AppServer"),
                threadId: new ThreadId(created.Id),
                turnId: new TurnId("turn_execution_rollout_001"),
                workingDirectory: "D:/Repo",
                metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["executionKind"] = StructuredValue.FromString("CodeMode"),
                    ["itemId"] = StructuredValue.FromString("item_execution_rollout_001"),
                }));
            var request = new ExecutionRequest(
                new ExecutionId("exec_rollout_001"),
                ExecutionKind.EnvironmentAction,
                "execute",
                context,
                StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["code"] = "console.log('hello');",
                }),
                createdAt);
            IExecutionEvent @event = new ExecutionCompleted(
                request.ExecutionId,
                occurredAt: request.CreatedAt.AddSeconds(1),
                message: "code mode execution completed",
                data: StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["status"] = "running",
                    ["handle"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["nativeHandleId"] = "cell_001",
                    },
                }));

            await store.RolloutRecorder.AppendExecutionRequestAsync(created.Id, request, CancellationToken.None);
            await store.RolloutRecorder.AppendExecutionEventAsync(created.Id, @event, CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            var rolloutText = await File.ReadAllTextAsync(store.RolloutRecorder.GetRolloutPath(created.Id), CancellationToken.None);

            Assert.Contains("\"type\":\"execution_request\"", rolloutText, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"execution_event\"", rolloutText, StringComparison.Ordinal);
            Assert.Contains("\"executionId\":\"exec_rollout_001\"", rolloutText, StringComparison.Ordinal);
            Assert.Contains("\"executionKind\":\"CodeMode\"", rolloutText, StringComparison.Ordinal);
            Assert.Contains("\"eventKind\":\"Completed\"", rolloutText, StringComparison.Ordinal);
            Assert.Contains("\"nativeHandleId\":\"cell_001\"", rolloutText, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                DeleteDirectory(root);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Fact]
    public async Task ReloadFromJson_ShouldDeserializeConfigSnapshotShellEnvironmentPolicy()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_config_snapshot", "D:/Repo", CancellationToken.None);
            created.ConfigSnapshot = new KernelThreadConfigSnapshot(
                Model: "gpt-5",
                ModelProviderId: "openai",
                ServiceTier: null,
                ApprovalPolicy: "on-request",
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "danger-full-access" }),
                SandboxMode: "danger-full-access",
                Cwd: "D:/Repo",
                Ephemeral: false,
                AllowLoginShell: true,
                ShellEnvironmentPolicy: new KernelShellEnvironmentPolicy(
                    inherit: KernelShellEnvironmentPolicyInherit.None,
                    ignoreDefaultExcludes: false,
                    excludePatterns: ["SECRET_*"],
                    setVariables: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["FOO"] = "BAR",
                    },
                    includeOnlyPatterns: ["PATH", "FOO"],
                    useProfile: true),
                ProviderBaseUrl: null,
                ProviderApiKeyEnvironmentVariable: null,
                ProviderWireApi: "responses",
                ProviderRequestMaxRetries: null,
                ProviderStreamMaxRetries: null,
                ProviderStreamIdleTimeoutMs: null,
                ProviderWebsocketConnectTimeoutMs: null,
                ProviderSupportsWebsockets: null,
                ProviderHttpFallbackEnabled: false,
                WebSearchMode: null,
                ServiceName: null,
                BaseInstructions: null,
                DeveloperInstructions: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                Verbosity: null,
                Personality: null,
                DynamicTools: null,
                CollaborationMode: null,
                PersistExtendedHistory: true,
                SessionSource: "appServer");

            _ = await store.UpsertThreadAsync(created, CancellationToken.None);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var thread = await reloaded.GetThreadAsync(created.Id, CancellationToken.None);

            Assert.NotNull(thread);
            Assert.NotNull(thread!.ConfigSnapshot);
            Assert.Equal(KernelShellEnvironmentPolicyInherit.None, thread.ConfigSnapshot!.ShellEnvironmentPolicy.Inherit);
            Assert.False(thread.ConfigSnapshot.ShellEnvironmentPolicy.IgnoreDefaultExcludes);
            Assert.Equal(["SECRET_*"], thread.ConfigSnapshot.ShellEnvironmentPolicy.ExcludePatterns);
            Assert.Equal("BAR", thread.ConfigSnapshot.ShellEnvironmentPolicy.SetVariables["FOO"]);
            Assert.Equal(["PATH", "FOO"], thread.ConfigSnapshot.ShellEnvironmentPolicy.IncludeOnlyPatterns);
            Assert.True(thread.ConfigSnapshot.ShellEnvironmentPolicy.UseProfile);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SetSeedHistoryAsync_WhenTypedSeedHistoryPresent_RoundTripsThroughSqliteJsonAndRollout()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var created = await store.CreateThreadAsync("thread_typed_seed_history_001", "D:/Repo", CancellationToken.None);
            KernelConversationHistoryItem[] seedHistory =
            [
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    Content = "typed authoritative",
                    Inputs =
                    [
                        new KernelConversationInputRecord
                        {
                            Type = "mention",
                            Name = "worker-1",
                            Path = "app://worker-1",
                        },
                        new KernelConversationInputRecord
                        {
                            Type = "text",
                            Text = "请继续处理",
                        },
                    ],
                },
                new KernelConversationHistoryItem
                {
                    Role = "assistant",
                    Content = "previous answer",
                },
            ];

            var updated = await store.SetSeedHistoryAsync(created.Id, seedHistory, CancellationToken.None);
            Assert.NotNull(updated);
            Assert.Equal(2, updated!.SeedHistory.Count);
            Assert.Equal(2, updated.SeedHistory[0].Inputs.Count);

            var mirrored = await store.StateStore.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(mirrored);
            var mirroredPayload = KernelStoredThreadStateTestHelper.DeserializePayload(mirrored!);
            Assert.Equal(2, mirroredPayload.SeedHistory.Count);
            Assert.Equal("mention", mirroredPayload.SeedHistory[0].Inputs[0].Type);
            Assert.Equal("worker-1", mirroredPayload.SeedHistory[0].Inputs[0].Name);
            Assert.Equal("text", mirroredPayload.SeedHistory[0].Inputs[1].Type);
            Assert.Equal("请继续处理", mirroredPayload.SeedHistory[0].Inputs[1].Text);

            var snapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(created, "gpt-5", "openai", "on-request").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                created.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(created, snapshot),
                CancellationToken.None);
            await store.RolloutRecorder.AppendCompactionAsync(
                created.Id,
                "compact_typed_seed_history_001",
                KernelRolloutStateMapper.SerializeSeedHistory(seedHistory),
                Array.Empty<KernelRolloutTurnRecord>(),
                CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(created.Id, CancellationToken.None);

            var jsonReloaded = new KernelThreadStore(file);
            await jsonReloaded.InitializeAsync(CancellationToken.None);
            var fromJson = await jsonReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(fromJson);
            Assert.Equal(2, fromJson!.SeedHistory.Count);
            Assert.Equal("mention", fromJson.SeedHistory[0].Inputs[0].Type);
            Assert.Equal("app://worker-1", fromJson.SeedHistory[0].Inputs[0].Path);

            File.Delete(file);

            var rolloutReloaded = new KernelThreadStore(file);
            await rolloutReloaded.InitializeAsync(CancellationToken.None);
            var fromRollout = await rolloutReloaded.GetThreadAsync(created.Id, CancellationToken.None);
            Assert.NotNull(fromRollout);
            Assert.Equal(2, fromRollout!.SeedHistory.Count);
            Assert.Equal("mention", fromRollout.SeedHistory[0].Inputs[0].Type);
            Assert.Equal("worker-1", fromRollout.SeedHistory[0].Inputs[0].Name);
            Assert.Equal("text", fromRollout.SeedHistory[0].Inputs[1].Type);
            Assert.Equal("请继续处理", fromRollout.SeedHistory[0].Inputs[1].Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task CompactThread_ShouldSummarizeOldTurnsAndKeepRecentTurns()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var thread = await store.CreateThreadAsync("thread_compact", "D:/Repo", CancellationToken.None);
            for (var i = 1; i <= 6; i++)
            {
                _ = await store.AppendCompletedTurnAsync(
                    thread.Id,
                    $"turn_{i}",
                    $"Q{i}",
                    $"A{i}",
                    "completed",
                    CancellationToken.None);
            }

            var compacted = await store.CompactThreadAsync(thread.Id, keepRecentTurns: 2, CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(thread.Id, CancellationToken.None);
            Assert.NotNull(compacted);
            Assert.True(compacted!.Turns.Count >= 3);
            Assert.StartsWith("compact_", compacted.Turns[0].Id, StringComparison.Ordinal);
            Assert.True(compacted.Turns[0].IsContextCompaction);
            Assert.Null(compacted.Turns[0].UserMessage);
            Assert.Null(compacted.Turns[0].AssistantMessage);
            Assert.Equal(2, compacted.SeedHistory.Count);
            Assert.Equal("user", compacted.SeedHistory[0].Role);
            Assert.Equal("上下文压缩摘要", compacted.SeedHistory[0].Content);
            Assert.Equal("assistant", compacted.SeedHistory[1].Role);
            Assert.Contains("Q: Q1", compacted.SeedHistory[1].Content, StringComparison.Ordinal);
            Assert.Equal("turn_5", compacted.Turns[1].Id);
            Assert.Equal("turn_6", compacted.Turns[2].Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
    [Fact]
    public async Task InitializeAsync_ShouldRehydrateCompactedThreadFromRollout()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var thread = await store.CreateThreadAsync("thread_compact_rollout", "D:/Repo", CancellationToken.None);
            var snapshot = new KernelThreadConfigSnapshot(
                Model: "gpt-5",
                ModelProviderId: "openai",
                ServiceTier: null,
                ApprovalPolicy: "on-request",
                SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "danger-full-access" }),
                SandboxMode: "danger-full-access",
                Cwd: "D:/Repo",
                Ephemeral: false,
                AllowLoginShell: true,
                ShellEnvironmentPolicy: KernelShellEnvironmentPolicy.Default,
                ProviderBaseUrl: null,
                ProviderApiKeyEnvironmentVariable: null,
                ProviderWireApi: null,
                ProviderRequestMaxRetries: null,
                ProviderStreamMaxRetries: null,
                ProviderStreamIdleTimeoutMs: null,
                ProviderWebsocketConnectTimeoutMs: null,
                ProviderSupportsWebsockets: null,
                ProviderHttpFallbackEnabled: false,
                WebSearchMode: null,
                ServiceName: null,
                BaseInstructions: null,
                DeveloperInstructions: null,
                ReasoningEffort: null,
                ReasoningSummary: null,
                Verbosity: null,
                Personality: null,
                DynamicTools: null,
                CollaborationMode: null,
                PersistExtendedHistory: true,
                SessionSource: "appServer");
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                thread.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(thread, snapshot),
                CancellationToken.None);

            for (var index = 1; index <= 6; index++)
            {
                var turnId = $"turn_{index}";
                var userMessage = $"Q{index}";
                var assistantMessage = $"A{index}";
                _ = await store.AppendCompletedTurnAsync(
                    thread.Id,
                    turnId,
                    userMessage,
                    assistantMessage,
                    "completed",
                    CancellationToken.None);
                await store.RolloutRecorder.AppendTurnResultAsync(
                    thread.Id,
                    turnId,
                    "completed",
                    userMessage,
                    assistantMessage,
                    CancellationToken.None);
            }

            _ = await store.CompactThreadAsync(thread.Id, keepRecentTurns: 2, CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(thread.Id, CancellationToken.None);
            File.Delete(file);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var restored = await reloaded.GetThreadAsync(thread.Id, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.Equal(2, restored!.SeedHistory.Count);
            Assert.Equal("上下文压缩摘要", restored.SeedHistory[0].Content);
            Assert.Contains("Q: Q1", restored.SeedHistory[1].Content, StringComparison.Ordinal);
            Assert.True(restored.Turns.Count >= 3);
            Assert.True(restored.Turns[0].IsContextCompaction);
            Assert.Equal("turn_5", restored.Turns[1].Id);
            Assert.Equal("turn_6", restored.Turns[2].Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ThreadStore_ShouldRehydrateUpdatedConfigSnapshotFromRolloutWhenJsonMissing()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");

        try
        {
            var store = new KernelThreadStore(file);
            await store.InitializeAsync(CancellationToken.None);

            var thread = await store.CreateThreadAsync("thread_rollout_session_state", "D:/Repo", CancellationToken.None);
            var initialSnapshot = KernelThreadConfigSnapshotFactory.FromSession(
                KernelThreadSessionBuilder.FromRecord(thread, "gpt-5", "openai", "on-request").Build());
            await store.RolloutRecorder.EnsureSessionMetaAsync(
                thread.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(thread, initialSnapshot),
                CancellationToken.None);

            thread.ForkedFromThreadId = "thread_origin_001";
            thread.ConfigSnapshot = initialSnapshot with
            {
                ProviderSupportsWebsockets = true,
                ProviderHttpFallbackEnabled = true,
                ProviderWireApi = "responses",
            };
            thread.SeedHistory =
            [
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    Content = "run tool",
                    Inputs =
                    [
                        new KernelConversationInputRecord
                        {
                            Type = "text",
                            Text = "run tool",
                        },
                    ],
                    RawResponseItem = JsonSerializer.SerializeToElement(new
                    {
                        type = "message",
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "input_text",
                                text = "run tool",
                            },
                        },
                    }),
                },
                new KernelConversationHistoryItem
                {
                    Role = "user",
                    RawResponseItem = JsonSerializer.SerializeToElement(new
                    {
                        type = "function_call",
                        name = "shell_command",
                        arguments = """{"command":"pwd"}""",
                        call_id = "call_session_state_001",
                    }),
                },
            ];

            _ = await store.UpsertThreadAsync(thread, CancellationToken.None);
            await store.RolloutRecorder.AppendSessionStateAsync(
                thread.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(thread),
                CancellationToken.None);
            await store.RolloutRecorder.CloseThreadWriterAsync(thread.Id, CancellationToken.None);
            File.Delete(file);

            var reloaded = new KernelThreadStore(file);
            await reloaded.InitializeAsync(CancellationToken.None);
            var restored = await reloaded.GetThreadAsync(thread.Id, CancellationToken.None);

            Assert.NotNull(restored);
            Assert.NotNull(restored!.ConfigSnapshot);
            Assert.Equal("thread_origin_001", restored.ForkedFromThreadId);
            Assert.True(restored.ConfigSnapshot!.ProviderSupportsWebsockets);
            Assert.True(restored.ConfigSnapshot.ProviderHttpFallbackEnabled);
            Assert.Equal("responses", restored.ConfigSnapshot.ProviderWireApi);
            Assert.Equal(2, restored.SeedHistory.Count);
            Assert.True(restored.SeedHistory[1].RawResponseItem.HasValue);
            Assert.Equal("function_call", restored.SeedHistory[1].RawResponseItem.Value.GetProperty("type").GetString());
            Assert.Equal("call_session_state_001", restored.SeedHistory[1].RawResponseItem.Value.GetProperty("call_id").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ClearThreadsAsync_ShouldRemoveJsonSqliteAndRolloutFiles()
    {
        var root = CreateTempDirectory();
        var file = Path.Combine(root, "threads.json");
        var sessions = Path.Combine(root, "sessions");
        var archivedSessions = Path.Combine(sessions, "archived");

        try
        {
            var store = new KernelThreadStore(file, sessions, archivedSessions);
            await store.InitializeAsync(CancellationToken.None);

            var active = await store.CreateThreadAsync("thread_clear_active", "D:/Repo", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(active.Id, "turn_active", "hi", "ok", "completed", CancellationToken.None);
            var archived = await store.CreateThreadAsync("thread_clear_archived", "D:/Repo", CancellationToken.None);
            _ = await store.AppendCompletedTurnAsync(archived.Id, "turn_archived", "hi", "ok", "completed", CancellationToken.None);
            _ = await store.SetThreadArchivedAsync(archived.Id, archived: true, CancellationToken.None);

            var activeRolloutPath = store.RolloutRecorder.GetRolloutPath(active.Id);
            var archivedRolloutPath = store.RolloutRecorder.GetArchivedRolloutPath(archived.Id);
            Assert.True(File.Exists(activeRolloutPath));
            Assert.True(File.Exists(archivedRolloutPath));
            Assert.NotNull(await store.StateStore.GetThreadAsync(active.Id, CancellationToken.None));

            var deletedCount = await store.ClearThreadsAsync(CancellationToken.None);

            Assert.Equal(2, deletedCount);
            Assert.Empty(await store.ListThreadsAsync(10, null, archived: false, CancellationToken.None));
            Assert.Empty(await store.ListThreadsAsync(10, null, archived: true, CancellationToken.None));
            Assert.Null(await store.StateStore.GetThreadAsync(active.Id, CancellationToken.None));
            Assert.Null(await store.StateStore.GetThreadAsync(archived.Id, CancellationToken.None));
            Assert.False(File.Exists(activeRolloutPath));
            Assert.False(File.Exists(archivedRolloutPath));
            Assert.Empty(Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.TopDirectoryOnly));
            Assert.Empty(Directory.EnumerateFiles(archivedSessions, "*.jsonl", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "tianshu-kernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
        }
    }
}







