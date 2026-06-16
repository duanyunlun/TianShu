using System.Collections.Concurrent;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.Execution.Runtime;
using static TianShu.AppHost.Tools.KernelToolJsonHelpers;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelThreadLifecycleAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks;
    private readonly Func<string> nextThreadId;
    private readonly Func<JsonElement, int, string, CancellationToken, Task> writeErrorAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, object, CancellationToken, Task> writeBroadcastNotificationAsync;
    private readonly Func<JsonElement, string?, CancellationToken, Task<string?>> validateThreadIdAsync;
    private readonly Func<string?, string> resolveThreadListDefaultModelProvider;
    private readonly Func<string, KernelThreadStartRequest, KernelThreadSessionState> buildThreadSessionStateForNewThread;
    private readonly Func<string, KernelThreadForkRequest, string?, KernelThreadSessionState> buildThreadSessionStateForFork;
    private readonly Func<KernelThreadRecord, KernelThreadResumeRequest, KernelThreadSessionState> buildThreadSessionStateWithConfigLoadHandling;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<KernelThreadSessionState, string?, KernelThreadResumeRequest, KernelThreadSessionState> buildResumedThreadSession;
    private readonly Func<KernelThreadSessionState, string?, KernelThreadForkRequest, KernelThreadSessionState> buildForkedThreadSession;
    private readonly Func<KernelThreadRecord, bool, KernelThreadSessionState?, KernelTurnRecord?, KernelThreadSessionResponsePayload> buildThreadSessionResponse;
    private readonly Func<KernelThreadSessionState, CancellationToken, Task> ensureRequiredMcpServersInitializedWithThreadErrorAsync;
    private readonly Func<KernelThreadSessionState, CancellationToken, Task> updateMcpSandboxStateAsync;
    private readonly Action<string> trackThreadSubscription;
    private readonly Func<string?, string?, bool> hasTrackedTurnActivity;
    private readonly Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot;
    private readonly Func<string?, CancellationToken, Task> replayPendingInteractiveRequestsAsync;
    private readonly Func<string?, CancellationToken, Task> emitExperimentalInstructionsDeprecationNoticeIfNeededAsync;
    private readonly Func<string, bool> removeTrackedThreadSubscription;
    private readonly Action<string> forgetThreadSubscription;
    private readonly Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync;
    private readonly Func<string, string?, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync;
    private readonly Func<string?, string?, string, CancellationToken, bool, Task> resolvePendingUserInputRequestsForThreadLifecycleAsync;
    private readonly Func<KernelRealtimeSessionState, string, Task> closeRealtimeTransportAsync;
    private readonly Action<string> releaseSpawnedAgentThread;
    private readonly Func<KernelThreadRecord, bool, KernelThreadPayload> toThreadPayload;
    private readonly Func<string, bool> tryBeginThreadRollback;
    private readonly Action<string> endThreadRollback;
    private readonly Action<string> cleanBackgroundTerminals;
    private readonly IReadOnlyList<KernelThreadSourceKind> interactiveThreadSourceKinds;

    public KernelThreadLifecycleAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        ConcurrentDictionary<string, Task> runningTurnTasks,
        Func<string> nextThreadId,
        Func<JsonElement, int, string, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, object, CancellationToken, Task> writeBroadcastNotificationAsync,
        Func<JsonElement, string?, CancellationToken, Task<string?>> validateThreadIdAsync,
        Func<string?, string> resolveThreadListDefaultModelProvider,
        Func<string, KernelThreadStartRequest, KernelThreadSessionState> buildThreadSessionStateForNewThread,
        Func<string, KernelThreadForkRequest, string?, KernelThreadSessionState> buildThreadSessionStateForFork,
        Func<KernelThreadRecord, KernelThreadResumeRequest, KernelThreadSessionState> buildThreadSessionStateWithConfigLoadHandling,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelThreadSessionState, string?, KernelThreadResumeRequest, KernelThreadSessionState> buildResumedThreadSession,
        Func<KernelThreadSessionState, string?, KernelThreadForkRequest, KernelThreadSessionState> buildForkedThreadSession,
        Func<KernelThreadRecord, bool, KernelThreadSessionState?, KernelTurnRecord?, KernelThreadSessionResponsePayload> buildThreadSessionResponse,
        Func<KernelThreadSessionState, CancellationToken, Task> ensureRequiredMcpServersInitializedWithThreadErrorAsync,
        Func<KernelThreadSessionState, CancellationToken, Task> updateMcpSandboxStateAsync,
        Action<string> trackThreadSubscription,
        Func<string?, string?, bool> hasTrackedTurnActivity,
        Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot,
        Func<string?, CancellationToken, Task> replayPendingInteractiveRequestsAsync,
        Func<string?, CancellationToken, Task> emitExperimentalInstructionsDeprecationNoticeIfNeededAsync,
        Func<string, bool> removeTrackedThreadSubscription,
        Action<string> forgetThreadSubscription,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync,
        Func<string, string?, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync,
        Func<string?, string?, string, CancellationToken, bool, Task> resolvePendingUserInputRequestsForThreadLifecycleAsync,
        Func<KernelRealtimeSessionState, string, Task> closeRealtimeTransportAsync,
        Action<string> releaseSpawnedAgentThread,
        Func<KernelThreadRecord, bool, KernelThreadPayload> toThreadPayload,
        Func<string, bool> tryBeginThreadRollback,
        Action<string> endThreadRollback,
        Action<string> cleanBackgroundTerminals,
        IReadOnlyList<KernelThreadSourceKind> interactiveThreadSourceKinds)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.runningTurns = runningTurns;
        this.runningTurnTasks = runningTurnTasks;
        this.nextThreadId = nextThreadId;
        this.writeErrorAsync = writeErrorAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeNotificationAsync = writeNotificationAsync;
        this.writeBroadcastNotificationAsync = writeBroadcastNotificationAsync;
        this.validateThreadIdAsync = validateThreadIdAsync;
        this.resolveThreadListDefaultModelProvider = resolveThreadListDefaultModelProvider;
        this.buildThreadSessionStateForNewThread = buildThreadSessionStateForNewThread;
        this.buildThreadSessionStateForFork = buildThreadSessionStateForFork;
        this.buildThreadSessionStateWithConfigLoadHandling = buildThreadSessionStateWithConfigLoadHandling;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.buildResumedThreadSession = buildResumedThreadSession;
        this.buildForkedThreadSession = buildForkedThreadSession;
        this.buildThreadSessionResponse = buildThreadSessionResponse;
        this.ensureRequiredMcpServersInitializedWithThreadErrorAsync = ensureRequiredMcpServersInitializedWithThreadErrorAsync;
        this.updateMcpSandboxStateAsync = updateMcpSandboxStateAsync;
        this.trackThreadSubscription = trackThreadSubscription;
        this.hasTrackedTurnActivity = hasTrackedTurnActivity;
        this.buildTrackedActiveTurnSnapshot = buildTrackedActiveTurnSnapshot;
        this.replayPendingInteractiveRequestsAsync = replayPendingInteractiveRequestsAsync;
        this.emitExperimentalInstructionsDeprecationNoticeIfNeededAsync = emitExperimentalInstructionsDeprecationNoticeIfNeededAsync;
        this.removeTrackedThreadSubscription = removeTrackedThreadSubscription;
        this.forgetThreadSubscription = forgetThreadSubscription;
        this.writeThreadStatusChangedAsync = writeThreadStatusChangedAsync;
        this.resolvePendingInteractiveRequestsForThreadLifecycleAsync = resolvePendingInteractiveRequestsForThreadLifecycleAsync;
        this.resolvePendingUserInputRequestsForThreadLifecycleAsync = resolvePendingUserInputRequestsForThreadLifecycleAsync;
        this.closeRealtimeTransportAsync = closeRealtimeTransportAsync;
        this.releaseSpawnedAgentThread = releaseSpawnedAgentThread;
        this.toThreadPayload = toThreadPayload;
        this.tryBeginThreadRollback = tryBeginThreadRollback;
        this.endThreadRollback = endThreadRollback;
        this.cleanBackgroundTerminals = cleanBackgroundTerminals;
        this.interactiveThreadSourceKinds = interactiveThreadSourceKinds;
    }

    public async Task HandleThreadListAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var limit = ReadInt(@params, "limit") ?? 20;
        var cwd = ReadString(@params, "cwd");
        if (!TryReadOptionalStringArray(@params, "modelProviders", out var modelProviders, out var modelProvidersError))
        {
            await writeErrorAsync(id, -32600, modelProvidersError ?? "modelProviders 无效。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!TryReadThreadSourceKinds(@params, "sourceKinds", out var sourceKinds, out var sourceKindsError))
        {
            await writeErrorAsync(id, -32600, sourceKindsError ?? "sourceKinds 无效。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var query = new KernelThreadListQuery(
            Limit: limit,
            Cursor: ReadString(@params, "cursor"),
            Cwd: cwd,
            Archived: ReadBool(@params, "archived") ?? false,
            SortKey: ReadString(@params, "sortKey") ?? "created_at",
            ModelProviders: modelProviders,
            SourceKinds: sourceKinds.Count == 0 ? interactiveThreadSourceKinds : sourceKinds,
            SearchTerm: ReadString(@params, "searchTerm"),
            DefaultModelProvider: resolveThreadListDefaultModelProvider(cwd),
            DefaultSource: KernelSessionSource.VsCode);

        KernelThreadListPage page;
        try
        {
            page = await threadStore.ListThreadsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("invalid cursor:", StringComparison.Ordinal))
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(
            id,
            new KernelThreadListResponsePayload(
                Data: page.Data.Select(x => toThreadPayload(x, false)).ToArray(),
                NextCursor: page.NextCursor),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadStartAsync(
        JsonElement id,
        KernelThreadStartRequest request,
        CancellationToken cancellationToken)
    {
        var threadId = nextThreadId();
        var session = buildThreadSessionStateForNewThread(threadId, request);
        await ensureRequiredMcpServersInitializedWithThreadErrorAsync(session, cancellationToken).ConfigureAwait(false);

        var record = await threadStore
            .CreateThreadAsync(threadId, request.Cwd, cancellationToken, session.Ephemeral)
            .ConfigureAwait(false);
        var runtimeThread = threadManager.AttachThread(record, session, loaded: true, publishCreated: true);
        trackThreadSubscription(runtimeThread.Record.Id);

        await PersistThreadConfigSnapshotAsync(runtimeThread.Record, runtimeThread.Session, cancellationToken).ConfigureAwait(false);
        await updateMcpSandboxStateAsync(runtimeThread.Session, cancellationToken).ConfigureAwait(false);

        var response = buildThreadSessionResponse(runtimeThread.Record, false, runtimeThread.Session, null);
        await writeResultAsync(id, response, cancellationToken).ConfigureAwait(false);
        await emitExperimentalInstructionsDeprecationNoticeIfNeededAsync(runtimeThread.Record.Cwd, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("thread/started", new
        {
            thread = response.Thread,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadResumeAsync(
        JsonElement id,
        KernelThreadResumeRequest request,
        CancellationToken cancellationToken)
    {
        var requestedThreadId = request.ThreadId;
        var requestedPath = NormalizeRolloutPath(request.Path);
        var hasHistoryOverride = HasHistoryOverride(request, out var historyOverride);

        if (hasHistoryOverride)
        {
            if (!string.IsNullOrWhiteSpace(requestedThreadId) && TryGetLoadedThreadForResume(requestedThreadId, out _))
            {
                await writeErrorAsync(id, -32600, "cannot resume thread with history while running", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (historyOverride.Count == 0)
            {
                await writeErrorAsync(id, -32602, "history must not be empty", cancellationToken).ConfigureAwait(false);
                return;
            }

            var historyThread = await CreateThreadFromHistoryAsync(request, historyOverride, cancellationToken).ConfigureAwait(false);
            trackThreadSubscription(historyThread.Record.Id);
            await updateMcpSandboxStateAsync(historyThread.Session, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(
                id,
                buildThreadSessionResponse(historyThread.Record, true, historyThread.Session, null),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            if (!string.IsNullOrWhiteSpace(requestedThreadId) && TryGetLoadedThreadForResume(requestedThreadId, out var runningThread))
            {
                if (!HasRunningThreadResumeOverrides(request))
                {
                    var activeRolloutPath = await ResolveResumeRolloutPathOrWriteErrorAsync(id, runningThread!.Record, cancellationToken).ConfigureAwait(false);
                    if (activeRolloutPath is null)
                    {
                        return;
                    }

                    if (!RolloutPathsEqual(requestedPath, activeRolloutPath))
                    {
                        await writeErrorAsync(id, -32600, "mismatched path for running thread", cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }

                await WriteRunningThreadResumeResponseAsync(id, runningThread!, request, cancellationToken).ConfigureAwait(false);
                return;
            }

            var recordByPath = await LoadThreadFromRolloutPathAsync(requestedPath, cancellationToken).ConfigureAwait(false);
            if (recordByPath is null)
            {
                await writeErrorAsync(id, -32004, $"rollout not found: {requestedPath}", cancellationToken).ConfigureAwait(false);
                return;
            }

            var resumeCwdByPath = Normalize(request.Cwd) ?? recordByPath.Cwd;
            var sessionByPath = threadManager.TryGetThread(recordByPath.Id, out var existingThreadByPath) && existingThreadByPath is not null
                ? buildResumedThreadSession(existingThreadByPath.Session, resumeCwdByPath, request)
                : buildThreadSessionStateWithConfigLoadHandling(recordByPath, request);
            await ensureRequiredMcpServersInitializedWithThreadErrorAsync(sessionByPath, cancellationToken).ConfigureAwait(false);

            var attachedThread = threadManager.AttachThread(recordByPath, sessionByPath, loaded: true, publishCreated: false);
            trackThreadSubscription(recordByPath.Id);
            await PersistThreadConfigSnapshotAsync(recordByPath, attachedThread.Session, cancellationToken).ConfigureAwait(false);
            await updateMcpSandboxStateAsync(attachedThread.Session, cancellationToken).ConfigureAwait(false);
            await writeResultAsync(
                id,
                buildThreadSessionResponse(recordByPath, true, attachedThread.Session, null),
                cancellationToken).ConfigureAwait(false);
            await emitExperimentalInstructionsDeprecationNoticeIfNeededAsync(recordByPath.Cwd, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(requestedThreadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalizedRequestedThreadId = await validateThreadIdAsync(id, requestedThreadId, cancellationToken).ConfigureAwait(false);
        if (normalizedRequestedThreadId is null)
        {
            return;
        }

        requestedThreadId = normalizedRequestedThreadId;
        if (TryGetLoadedThreadForResume(requestedThreadId, out var runningThreadById))
        {
            if (!HasRunningThreadResumeOverrides(request)
                && await ResolveResumeRolloutPathOrWriteErrorAsync(id, runningThreadById!.Record, cancellationToken).ConfigureAwait(false) is null)
            {
                return;
            }

            await WriteRunningThreadResumeResponseAsync(id, runningThreadById!, request, cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await LoadThreadRecordPreferringRolloutAsync(requestedThreadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{requestedThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (await ResolveResumeRolloutPathOrWriteErrorAsync(id, record, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        var resumeCwd = Normalize(request.Cwd) ?? record.Cwd;
        var session = threadManager.TryGetThread(requestedThreadId, out var existingThread) && existingThread is not null
            ? buildResumedThreadSession(existingThread.Session, resumeCwd, request)
            : buildThreadSessionStateWithConfigLoadHandling(record, request);
        await ensureRequiredMcpServersInitializedWithThreadErrorAsync(session, cancellationToken).ConfigureAwait(false);

        var thread = threadManager.AttachThread(record, session, loaded: true, publishCreated: false);
        trackThreadSubscription(record.Id);
        await PersistThreadConfigSnapshotAsync(record, thread.Session, cancellationToken).ConfigureAwait(false);
        await updateMcpSandboxStateAsync(thread.Session, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(
            id,
            buildThreadSessionResponse(record, true, thread.Session, null),
            cancellationToken).ConfigureAwait(false);
        await emitExperimentalInstructionsDeprecationNoticeIfNeededAsync(record.Cwd, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasRunningThreadResumeOverrides(KernelThreadResumeRequest request)
        => Normalize(request.Model) is not null
           || Normalize(request.ModelProvider) is not null
           || request.ServiceTier.IsSpecified
           || Normalize(request.Cwd) is not null
           || request.ApprovalPolicy is not null
           || request.Sandbox is not null
           || request.Config is not null
           || Normalize(request.BaseInstructions) is not null
           || Normalize(request.DeveloperInstructions) is not null
           || request.Personality is not null
           || request.SessionSource is not null;

    public async Task HandleThreadReadAsync(
        JsonElement id,
        string threadId,
        bool includeTurns,
        CancellationToken cancellationToken)
    {
        var record = await LoadThreadRecordPreferringRolloutAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var hasLoadedRuntimeThread = threadManager.TryGetThread(threadId, out var runtimeThread)
            && runtimeThread is not null
            && runtimeThread.IsLoaded;
        var runtimeRecord = hasLoadedRuntimeThread ? runtimeThread!.Record : record;
        var runtimeSession = hasLoadedRuntimeThread ? runtimeThread!.Session : buildDefaultThreadSession(record);
        if (includeTurns)
        {
            if (runtimeSession.Ephemeral)
            {
                await writeErrorAsync(id, -32600, "ephemeral threads do not support includeTurns", cancellationToken).ConfigureAwait(false);
                return;
            }

            var rolloutPath = threadStore.RolloutRecorder.ResolveRolloutPath(runtimeRecord.Id, runtimeRecord.IsArchived);
            if (!File.Exists(rolloutPath))
            {
                await writeErrorAsync(
                    id,
                    -32600,
                    $"thread {threadId} is not materialized yet; includeTurns is unavailable before first user message",
                    cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await writeResultAsync(
            id,
            buildThreadSessionResponse(runtimeRecord, includeTurns, runtimeSession, null),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadIncrementElicitationAsync(
        JsonElement id,
        KernelThreadElicitationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedThreadId = Normalize(request.ThreadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        normalizedThreadId = await validateThreadIdAsync(id, normalizedThreadId, cancellationToken).ConfigureAwait(false);
        if (normalizedThreadId is null)
        {
            return;
        }

        var record = await LoadThreadRecordPreferringRolloutAsync(normalizedThreadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{normalizedThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        ulong count;
        try
        {
            count = runtimeThread.IncrementOutOfBandElicitationCount();
        }
        catch (OverflowException)
        {
            await writeErrorAsync(id, -32603, "out-of-band elicitation count overflowed", cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnsureThreadRolloutMaterializedAsync(record, runtimeThread.Session, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(
            id,
            new KernelThreadElicitationResponsePayload(
                Count: count,
                Paused: count > 0),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadDecrementElicitationAsync(
        JsonElement id,
        KernelThreadElicitationRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedThreadId = Normalize(request.ThreadId);
        if (string.IsNullOrWhiteSpace(normalizedThreadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        normalizedThreadId = await validateThreadIdAsync(id, normalizedThreadId, cancellationToken).ConfigureAwait(false);
        if (normalizedThreadId is null)
        {
            return;
        }

        var record = await LoadThreadRecordPreferringRolloutAsync(normalizedThreadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{normalizedThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        if (!runtimeThread.TryDecrementOutOfBandElicitationCount(out var count))
        {
            await writeErrorAsync(id, -32600, "out-of-band elicitation count is already zero", cancellationToken).ConfigureAwait(false);
            return;
        }

        await EnsureThreadRolloutMaterializedAsync(record, runtimeThread.Session, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(
            id,
            new KernelThreadElicitationResponsePayload(
                Count: count,
                Paused: count > 0),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleAgentThreadRegisterAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore.SetThreadAgentMetadataAsync(
            threadId!,
            ReadString(@params, "agentNickname"),
            ReadString(@params, "agentRole"),
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new
        {
            thread = toThreadPayload(record, false),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadSetNameAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        if (!HasProperty(@params, "name"))
        {
            await writeErrorAsync(id, -32602, "name 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var name = ReadString(@params, "name");
        var record = await threadStore.SetThreadNameAsync(threadId, name, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        await writeBroadcastNotificationAsync("thread/name/updated", new
        {
            threadId = record.Id,
            threadName = record.Name,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadArchiveAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        var existingRecord = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (existingRecord is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var isEphemeral = existingRecord.ConfigSnapshot?.Ephemeral == true
            || (threadManager.TryGetThread(threadId, out var existingRuntimeThread)
                && existingRuntimeThread?.Session.Ephemeral == true);
        if (!isEphemeral)
        {
            var rolloutPath = threadStore.RolloutRecorder.ResolveRolloutPath(threadId, archived: false);
            if (!File.Exists(rolloutPath))
            {
                await writeErrorAsync(id, -32600, $"no rollout found for thread id {threadId}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await DrainLoadedThreadAsync(
                threadId,
                lifecyclePhase: "thread_archived",
                closeReason: "thread_archived",
                removeRuntimeThread: false,
                cancellationToken)
            .ConfigureAwait(false);

        KernelThreadRecord? record;
        try
        {
            record = await threadStore.SetThreadArchivedAsync(threadId, archived: true, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("no rollout found for thread id ", StringComparison.Ordinal))
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = threadManager.RemoveThread(record.Id);
        releaseSpawnedAgentThread(record.Id);
        forgetThreadSubscription(record.Id);

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("thread/archived", new
        {
            threadId = record.Id,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DrainLoadedThreadAsync(
        string threadId,
        string lifecyclePhase,
        string closeReason,
        bool removeRuntimeThread,
        CancellationToken cancellationToken)
    {
        if (!threadManager.TryGetThread(threadId, out var runtimeThread) || runtimeThread is null)
        {
            return;
        }

        var activeTurnId = Normalize(runtimeThread.ActiveTurnId);
        if (!string.IsNullOrWhiteSpace(activeTurnId))
        {
            if (runningTurns.TryGetValue(activeTurnId, out var running))
            {
                try
                {
                    running.Cancel();
                }
                catch
                {
                }
            }

            if (runningTurnTasks.TryGetValue(activeTurnId, out var runningTask))
            {
                try
                {
                    await runningTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                }
            }
        }

        await resolvePendingInteractiveRequestsForThreadLifecycleAsync(
                threadId,
                activeTurnId,
                lifecyclePhase,
                cancellationToken,
                false)
            .ConfigureAwait(false);
        await resolvePendingUserInputRequestsForThreadLifecycleAsync(
                threadId,
                activeTurnId,
                lifecyclePhase,
                cancellationToken,
                false)
            .ConfigureAwait(false);

        if (runtimeThread.RealtimeSession is KernelRealtimeSessionState realtimeSession)
        {
            runtimeThread.SetRealtimeSession(null);
            realtimeSession.TryMarkClosedNotificationWritten();
            await closeRealtimeTransportAsync(realtimeSession, closeReason).ConfigureAwait(false);
        }

        cleanBackgroundTerminals(threadId);

        if (removeRuntimeThread)
        {
            _ = threadManager.RemoveThread(threadId);
        }

        try
        {
            await threadStore.RolloutRecorder.CloseThreadWriterAsync(threadId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async Task HandleThreadUnsubscribeAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        var wasLoaded = threadManager.IsLoaded(threadId);
        if (!wasLoaded)
        {
            forgetThreadSubscription(threadId);
            await writeResultAsync(id, new { status = "notLoaded" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!removeTrackedThreadSubscription(threadId))
        {
            await writeResultAsync(id, new { status = "notSubscribed" }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var threadRecord = threadManager.TryGetThread(threadId, out var runtimeThread) && runtimeThread is not null
            ? runtimeThread.Record
            : new KernelThreadRecord { Id = threadId };
        await writeResultAsync(id, new { status = "unsubscribed" }, cancellationToken).ConfigureAwait(false);

        await DrainLoadedThreadAsync(
                threadId,
                "thread_closed",
                "thread_closed",
                true,
                cancellationToken)
            .ConfigureAwait(false);
        await writeThreadStatusChangedAsync(threadRecord, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("thread/closed", new
        {
            threadId,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadCompactStartAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        var record = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var keepRecentTurns = ReadInt(@params, "keepRecentTurns")
            ?? ReadInt(@params, "keepRecent")
            ?? 12;
        var compactionTurnId = $"compact_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";
        var compactionItemId = $"context_compaction_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";

        await writeNotificationAsync("item/started", new
        {
            threadId = record.Id,
            turnId = compactionTurnId,
            item = new
            {
                id = compactionItemId,
                type = "contextCompaction",
            },
        }, CancellationToken.None).ConfigureAwait(false);

        var compacted = await threadStore.CompactThreadAsync(record.Id, keepRecentTurns, cancellationToken, compactionTurnId).ConfigureAwait(false);
        var effectiveTurnId = compacted?.Turns
            .FirstOrDefault(static turn => turn.Id.StartsWith("compact_", StringComparison.OrdinalIgnoreCase))
            ?.Id
            ?? compactionTurnId;

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("item/completed", new
        {
            threadId = record.Id,
            turnId = effectiveTurnId,
            item = new
            {
                id = compactionItemId,
                type = "contextCompaction",
            },
        }, CancellationToken.None).ConfigureAwait(false);
        await writeNotificationAsync("thread/compacted", new
        {
            threadId = record.Id,
            turnId = effectiveTurnId,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadBackgroundTerminalsCleanAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        cleanBackgroundTerminals(threadId);
        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadDeleteAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (TryGetRunningThread(threadId!, out _))
        {
            await writeErrorAsync(id, -32600, $"线程正在运行，无法删除：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (threadManager.TryGetThread(threadId!, out var loadedThread)
            && loadedThread?.RealtimeSession is not null)
        {
            await writeErrorAsync(id, -32600, $"线程处于 realtime 会话中，无法删除：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore.DeleteThreadAsync(threadId!, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = threadManager.RemoveThread(record.Id);
        releaseSpawnedAgentThread(record.Id);

        await writeResultAsync(id, new
        {
            threadId = record.Id,
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("thread/deleted", new
        {
            threadId = record.Id,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadClearAsync(
        JsonElement id,
        CancellationToken cancellationToken)
    {
        if (!runningTurns.IsEmpty)
        {
            await writeErrorAsync(id, -32600, "存在正在运行的线程，无法清空会话。", cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var loadedThreadId in threadManager.GetLoadedThreadIds())
        {
            if (threadManager.TryGetThread(loadedThreadId, out var loadedThread)
                && loadedThread?.RealtimeSession is not null)
            {
                await writeErrorAsync(id, -32600, $"线程处于 realtime 会话中，无法清空会话：{loadedThreadId}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var deletedCount = await threadStore.ClearThreadsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var threadId in threadManager.ClearThreads())
        {
            releaseSpawnedAgentThread(threadId);
        }

        await writeResultAsync(id, new
        {
            deletedCount,
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("thread/cleared", new
        {
            deletedCount,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadUnarchiveAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, ReadString(@params, "threadId"), cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        KernelThreadRecord? record;
        try
        {
            record = await threadStore.SetThreadArchivedAsync(threadId, archived: false, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("no archived rollout found for thread id ", StringComparison.Ordinal))
        {
            await writeErrorAsync(id, -32600, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new
        {
            thread = toThreadPayload(record, false),
        }, cancellationToken).ConfigureAwait(false);

        await writeNotificationAsync("thread/unarchived", new
        {
            threadId = record.Id,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadMetadataUpdateAsync(
        JsonElement id,
        KernelThreadMetadataUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var threadId = await validateThreadIdAsync(id, request.ThreadId, cancellationToken).ConfigureAwait(false);
        if (threadId is null)
        {
            return;
        }

        var gitInfo = request.GitInfo;
        if (gitInfo is null
            || (!gitInfo.Sha.IsSpecified
                && !gitInfo.Branch.IsSpecified
                && !gitInfo.OriginUrl.IsSpecified))
        {
            await writeErrorAsync(id, -32602, "gitInfo must include at least one field", cancellationToken).ConfigureAwait(false);
            return;
        }

        var hasSha = gitInfo.Sha.IsSpecified;
        var hasBranch = gitInfo.Branch.IsSpecified;
        var hasOriginUrl = gitInfo.OriginUrl.IsSpecified;
        var sha = Normalize(gitInfo.Sha.Value);
        var branch = Normalize(gitInfo.Branch.Value);
        var originUrl = Normalize(gitInfo.OriginUrl.Value);

        if (hasSha && gitInfo.Sha.Value is not null && sha is null)
        {
            await writeErrorAsync(id, -32602, "gitInfo.sha must not be empty", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (hasBranch && gitInfo.Branch.Value is not null && branch is null)
        {
            await writeErrorAsync(id, -32602, "gitInfo.branch must not be empty", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (hasOriginUrl && gitInfo.OriginUrl.Value is not null && originUrl is null)
        {
            await writeErrorAsync(id, -32602, "gitInfo.originUrl must not be empty", cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = await LoadThreadRecordPreferringRolloutAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (existing.ConfigSnapshot?.Ephemeral == true)
        {
            await writeErrorAsync(
                id,
                -32600,
                $"ephemeral thread does not support metadata updates: {threadId}",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore
            .UpdateThreadGitInfoAsync(
                threadId,
                hasSha,
                sha,
                hasBranch,
                branch,
                hasOriginUrl,
                originUrl,
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new
        {
            thread = toThreadPayload(record, false),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadPendingInputStateUpdateAsync(
        JsonElement id,
        KernelThreadPendingInputStateUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var threadId = Normalize(request.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!request.PendingInputState.IsSpecified)
        {
            await writeErrorAsync(id, -32602, "pendingInputState 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var existing = await LoadThreadRecordPreferringRolloutAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var record = await threadStore
            .SetThreadPendingInputStateAsync(
                threadId,
                KernelPendingInputStateFactory.Normalize(request.PendingInputState.Value),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await writeResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadRollbackAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var threadId = ReadString(@params, "threadId");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var numTurns = ReadInt(@params, "numTurns");
        if (numTurns is null || numTurns <= 0)
        {
            await writeErrorAsync(id, -32602, "numTurns 必须为大于 0 的整数。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalizedThreadId = Normalize(threadId)!;
        if (!threadManager.TryGetThread(normalizedThreadId, out var runtimeThread)
            || runtimeThread is null
            || !runtimeThread.IsLoaded)
        {
            await writeErrorAsync(id, -32600, $"thread not found: {threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (runtimeThread.Session.Ephemeral)
        {
            await writeErrorAsync(id, -32600, "thread has no persisted rollout", cancellationToken).ConfigureAwait(false);
            return;
        }

        var rolloutPath = threadStore.RolloutRecorder.ResolveRolloutPath(normalizedThreadId, archived: false);
        if (!File.Exists(rolloutPath))
        {
            await writeErrorAsync(id, -32600, "thread has no persisted rollout", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!tryBeginThreadRollback(normalizedThreadId))
        {
            await writeErrorAsync(id, -32602, "rollback already in progress for this thread", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var record = await threadStore.RollbackThreadTurnsAsync(threadId, numTurns.Value, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                await writeErrorAsync(id, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
                return;
            }

            _ = threadManager.AttachThread(record, runtimeThread.Session, loaded: true, publishCreated: false);
            await resolvePendingInteractiveRequestsForThreadLifecycleAsync(
                    normalizedThreadId,
                    null,
                    "thread_rolled_back",
                    cancellationToken,
                    false)
                .ConfigureAwait(false);
            await resolvePendingUserInputRequestsForThreadLifecycleAsync(
                    normalizedThreadId,
                    null,
                    "thread_rolled_back",
                    cancellationToken,
                    false)
                .ConfigureAwait(false);

            await writeResultAsync(id, new
            {
                thread = toThreadPayload(record, true),
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            endThreadRollback(normalizedThreadId);
        }
    }

    public async Task HandleThreadLoadedListAsync(
        JsonElement id,
        JsonElement @params,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, ReadInt(@params, "limit") ?? int.MaxValue);
        var cursor = ReadString(@params, "cursor");
        var all = threadManager.GetLoadedThreadIds().ToList();

        if (all.Count == 0)
        {
            await writeResultAsync(id, new
            {
                data = Array.Empty<string>(),
                nextCursor = (string?)null,
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var start = 0;
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!Guid.TryParse(cursor, out var parsedCursor))
            {
                await writeErrorAsync(id, -32600, $"invalid cursor: {cursor}", cancellationToken).ConfigureAwait(false);
                return;
            }

            var normalizedCursor = parsedCursor.ToString();
            var idx = all.BinarySearch(normalizedCursor, StringComparer.Ordinal);
            start = idx >= 0 ? idx + 1 : ~idx;
        }

        var end = Math.Min(start + limit, all.Count);
        var page = all.Skip(start).Take(end - start).ToArray();
        var nextCursor = end < all.Count && page.Length > 0 ? page[^1] : null;

        await writeResultAsync(id, new
        {
            data = page,
            nextCursor,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadForkAsync(
        JsonElement id,
        KernelThreadForkRequest request,
        CancellationToken cancellationToken)
    {
        var sourceThreadId = request.ThreadId;
        var requestedPath = NormalizeRolloutPath(request.Path);
        KernelThreadRecord? sourceRecord;

        if (!string.IsNullOrWhiteSpace(requestedPath))
        {
            sourceRecord = await LoadThreadFromRolloutPathAsync(requestedPath, cancellationToken).ConfigureAwait(false);
            if (sourceRecord is null)
            {
                await writeErrorAsync(id, -32004, $"rollout not found: {requestedPath}", cancellationToken).ConfigureAwait(false);
                return;
            }

            sourceThreadId = sourceRecord.Id;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(sourceThreadId))
            {
                await writeErrorAsync(id, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
                return;
            }

            var normalizedSourceThreadId = await validateThreadIdAsync(id, sourceThreadId, cancellationToken).ConfigureAwait(false);
            if (normalizedSourceThreadId is null)
            {
                return;
            }

            sourceThreadId = normalizedSourceThreadId;
            sourceRecord = await LoadThreadRecordPreferringRolloutAsync(sourceThreadId, cancellationToken).ConfigureAwait(false);
            if (sourceRecord is null)
            {
                await writeErrorAsync(id, -32004, $"线程不存在：{sourceThreadId}", cancellationToken).ConfigureAwait(false);
                return;
            }

            var rolloutPath = threadStore.RolloutRecorder.ResolveRolloutPath(sourceRecord.Id, sourceRecord.IsArchived);
            if (!File.Exists(rolloutPath))
            {
                await writeErrorAsync(id, -32600, $"no rollout found for thread id {sourceThreadId}", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var sourceSession = threadManager.TryGetThread(sourceThreadId!, out var sourceThread)
            ? sourceThread?.Session
            : null;
        var newThreadId = nextThreadId();
        var effectiveForkCwd = Normalize(request.Cwd) ?? sourceRecord.Cwd;
        var forkedSession = sourceSession is null
            ? buildThreadSessionStateForFork(newThreadId, request, effectiveForkCwd)
            : buildForkedThreadSession(sourceSession, effectiveForkCwd, request);
        var forked = await threadStore
            .ForkThreadAsync(sourceThreadId!, newThreadId, effectiveForkCwd, cancellationToken, forkedSession.Ephemeral)
            .ConfigureAwait(false);
        if (forked is null)
        {
            await writeErrorAsync(id, -32004, $"线程不存在：{sourceThreadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = threadManager.AttachThread(forked, forkedSession, loaded: true, publishCreated: true);
        trackThreadSubscription(forked.Id);
        await EnsureThreadRolloutMaterializedAsync(forked, forkedSession, cancellationToken).ConfigureAwait(false);
        await updateMcpSandboxStateAsync(forkedSession, cancellationToken).ConfigureAwait(false);

        var response = buildThreadSessionResponse(forked, true, forkedSession, null);
        await writeResultAsync(id, response, cancellationToken).ConfigureAwait(false);
        await writeNotificationAsync("thread/started", new
        {
            thread = buildThreadSessionResponse(forked, false, forkedSession, null).Thread,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PersistThreadConfigSnapshotAsync(
        KernelThreadRecord record,
        KernelThreadSessionState session,
        CancellationToken cancellationToken)
    {
        var snapshot = KernelThreadConfigSnapshotFactory.FromSession(session);
        if (record.ConfigSnapshot is not null && Equals(record.ConfigSnapshot, snapshot))
        {
            return;
        }

        record.ConfigSnapshot = snapshot.DeepClone();
        _ = await threadStore.UpsertThreadAsync(record, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureThreadRolloutMaterializedAsync(
        KernelThreadRecord record,
        KernelThreadSessionState session,
        CancellationToken cancellationToken)
    {
        await PersistThreadConfigSnapshotAsync(record, session, cancellationToken).ConfigureAwait(false);
        if (session.Ephemeral)
        {
            return;
        }

        await threadStore.RolloutRecorder
            .EnsureSessionMetaAsync(
                record.Id,
                KernelRolloutStateMapper.ToRolloutThreadRecord(
                    record,
                    record.ConfigSnapshot ?? KernelThreadConfigSnapshotFactory.FromSession(session)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public bool TryGetRunningThread(string threadId, out KernelRuntimeThread? thread)
    {
        thread = null;
        if (!threadManager.TryGetThread(threadId, out var runtimeThread)
            || runtimeThread is null
            || !TryNormalizeTrackedActiveTurn(runtimeThread, out _))
        {
            return false;
        }

        thread = runtimeThread;
        return true;
    }

    public bool TryGetLoadedThreadForResume(string threadId, out KernelRuntimeThread? thread)
    {
        thread = null;
        if (!threadManager.TryGetThread(threadId, out var runtimeThread) || runtimeThread is null || !runtimeThread.IsLoaded)
        {
            return false;
        }

        _ = TryNormalizeTrackedActiveTurn(runtimeThread, out _);
        thread = runtimeThread;
        return true;
    }

    public async Task<KernelThreadRecord?> LoadThreadRecordPreferringRolloutAsync(string threadId, CancellationToken cancellationToken)
    {
        var storeRecord = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (storeRecord is null || TryGetLoadedThreadForResume(threadId, out _))
        {
            return storeRecord;
        }

        var rolloutRecord = await threadStore.RolloutRecorder
            .RehydrateThreadAsync(
                threadStore.RolloutRecorder.ResolveRolloutPath(threadId, storeRecord.IsArchived),
                cancellationToken)
            .ConfigureAwait(false);
        if (rolloutRecord is null)
        {
            return storeRecord;
        }

        var hydratedRecord = KernelRolloutStateMapper.FromRolloutThreadRecord(rolloutRecord);
        MergeThreadRecordMetadata(hydratedRecord, storeRecord);
        hydratedRecord.IsArchived = threadStore.RolloutRecorder.TryResolveArchivedState(threadId) ?? storeRecord.IsArchived;
        if (hydratedRecord.IsArchived)
        {
            hydratedRecord.StatusType = "notLoaded";
            hydratedRecord.ActiveFlags = [];
        }

        return await threadStore.UpsertThreadAsync(hydratedRecord, cancellationToken).ConfigureAwait(false);
    }

    private bool TryNormalizeTrackedActiveTurn(KernelRuntimeThread runtimeThread, out string? activeTurnId)
    {
        activeTurnId = runtimeThread.ActiveTurnId;
        if (string.IsNullOrWhiteSpace(activeTurnId))
        {
            return false;
        }

        var hasRunningTurnToken = runningTurns.TryGetValue(activeTurnId, out _);
        var hasRunningTask = false;
        if (runningTurnTasks.TryGetValue(activeTurnId, out var runningTask))
        {
            if (runningTask.IsCompleted)
            {
                runningTurnTasks.TryRemove(activeTurnId, out _);
                if (hasRunningTurnToken && runningTurns.TryRemove(activeTurnId, out var staleTurnToken))
                {
                    staleTurnToken.Dispose();
                    hasRunningTurnToken = false;
                }
            }
            else
            {
                hasRunningTask = true;
            }
        }

        if (!hasRunningTurnToken
            && !hasRunningTask
            && !hasTrackedTurnActivity(runtimeThread.Record.Id, activeTurnId))
        {
            _ = runtimeThread.ClearActiveTurn(activeTurnId);
            NormalizeStaleActiveTurnRecord(runtimeThread.Record, activeTurnId);
            activeTurnId = null;
            return false;
        }

        return true;
    }

    private static void NormalizeStaleActiveTurnRecord(KernelThreadRecord record, string turnId)
    {
        record.StatusType = "idle";
        record.ActiveFlags = [];
        record.UpdatedAt = DateTimeOffset.UtcNow;

        foreach (var turn in record.Turns)
        {
            if (!string.Equals(turn.Id, turnId, StringComparison.Ordinal)
                || !string.Equals(turn.Status, "inProgress", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            turn.Status = "interrupted";
            if (turn.CompletedAt == default)
            {
                turn.CompletedAt = record.UpdatedAt;
            }

            break;
        }
    }

    private async Task WriteRunningThreadResumeResponseAsync(
        JsonElement id,
        KernelRuntimeThread runningThread,
        KernelThreadResumeRequest request,
        CancellationToken cancellationToken)
    {
        var resumeCwd = Normalize(request.Cwd) ?? runningThread.Record.Cwd;
        var resumedSession = buildResumedThreadSession(runningThread.Session, resumeCwd, request);
        await ensureRequiredMcpServersInitializedWithThreadErrorAsync(resumedSession, cancellationToken).ConfigureAwait(false);
        runningThread.UpdateSession(resumedSession);
        var attachedRunningThread = threadManager.AttachThread(runningThread.Record, resumedSession, loaded: true, publishCreated: false);
        trackThreadSubscription(attachedRunningThread.Record.Id);
        var activeTurn = buildTrackedActiveTurnSnapshot(attachedRunningThread.Record.Id, attachedRunningThread.ActiveTurnId);
        await PersistThreadConfigSnapshotAsync(attachedRunningThread.Record, attachedRunningThread.Session, cancellationToken).ConfigureAwait(false);
        await updateMcpSandboxStateAsync(attachedRunningThread.Session, cancellationToken).ConfigureAwait(false);
        await writeResultAsync(
            id,
            buildThreadSessionResponse(attachedRunningThread.Record, true, attachedRunningThread.Session, activeTurn),
            cancellationToken).ConfigureAwait(false);
        await replayPendingInteractiveRequestsAsync(attachedRunningThread.Record.Id, cancellationToken).ConfigureAwait(false);
        await emitExperimentalInstructionsDeprecationNoticeIfNeededAsync(attachedRunningThread.Record.Cwd, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveResumeRolloutPathOrWriteErrorAsync(
        JsonElement id,
        KernelThreadRecord record,
        CancellationToken cancellationToken)
    {
        var rolloutPath = threadStore.RolloutRecorder.ResolveRolloutPath(record.Id, record.IsArchived);
        if (File.Exists(rolloutPath))
        {
            return rolloutPath;
        }

        await writeErrorAsync(id, -32600, $"no rollout found for thread id {record.Id}", cancellationToken).ConfigureAwait(false);
        return null;
    }

    private async Task<KernelThreadRecord?> LoadThreadFromRolloutPathAsync(string rolloutPath, CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizeRolloutPath(rolloutPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        var record = await threadStore.RolloutRecorder.RehydrateThreadAsync(normalizedPath!, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        return await threadStore
            .UpsertThreadAsync(KernelRolloutStateMapper.FromRolloutThreadRecord(record), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void MergeThreadRecordMetadata(KernelThreadRecord target, KernelThreadRecord source)
    {
        target.Name = source.Name;
        target.AgentNickname = source.AgentNickname;
        target.AgentRole = source.AgentRole;
        target.IsArchived = source.IsArchived;
        target.GitInfo = source.GitInfo is null
            ? null
            : new KernelGitInfoRecord
            {
                Sha = source.GitInfo.Sha,
                Branch = source.GitInfo.Branch,
                OriginUrl = source.GitInfo.OriginUrl,
            };

        if (string.IsNullOrWhiteSpace(target.Cwd))
        {
            target.Cwd = source.Cwd;
        }

        target.ForkedFromThreadId ??= source.ForkedFromThreadId;
        target.ConfigSnapshot ??= source.ConfigSnapshot?.DeepClone();
        target.PendingInputState ??= source.PendingInputState?.DeepClone();
        if (target.SeedHistory.Count == 0 && source.SeedHistory.Count > 0)
        {
            target.SeedHistory = source.SeedHistory
                .Select(KernelConversationHistoryUtilities.Clone)
                .ToList();
        }
    }

    private async Task<(KernelThreadRecord Record, KernelThreadSessionState Session)> CreateThreadFromHistoryAsync(
        KernelThreadResumeRequest request,
        IReadOnlyList<KernelConversationHistoryItem> history,
        CancellationToken cancellationToken)
    {
        var threadId = nextThreadId();
        var session = buildThreadSessionStateForNewThread(
            threadId,
            new KernelThreadStartRequest
            {
                Model = request.Model,
                ModelProvider = request.ModelProvider,
                ServiceTier = request.ServiceTier,
                Cwd = request.Cwd,
                ApprovalPolicy = request.ApprovalPolicy,
                Sandbox = request.Sandbox,
                Config = request.Config,
                BaseInstructions = request.BaseInstructions,
                DeveloperInstructions = request.DeveloperInstructions,
                Personality = request.Personality,
                PersistExtendedHistory = request.PersistExtendedHistory,
            });
        await ensureRequiredMcpServersInitializedWithThreadErrorAsync(session, cancellationToken).ConfigureAwait(false);

        var created = await threadStore.CreateThreadAsync(threadId, request.Cwd, cancellationToken).ConfigureAwait(false);
        var withHistory = await threadStore.SetSeedHistoryAsync(created.Id, history, cancellationToken).ConfigureAwait(false) ?? created;
        _ = threadManager.AttachThread(withHistory, session, loaded: true, publishCreated: false);
        await EnsureThreadRolloutMaterializedAsync(withHistory, session, cancellationToken).ConfigureAwait(false);
        return (withHistory, session);
    }

    private static string? NormalizeRolloutPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static bool RolloutPathsEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizeRolloutPath(left);
        var normalizedRight = NormalizeRolloutPath(right);
        if (normalizedLeft is null || normalizedRight is null)
        {
            return false;
        }

        return string.Equals(
            normalizedLeft,
            normalizedRight,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool HasHistoryOverride(KernelThreadResumeRequest request, out IReadOnlyList<KernelConversationHistoryItem> history)
    {
        history = request.History?.Items ?? Array.Empty<KernelConversationHistoryItem>();
        return request.History?.ShouldOverride == true;
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static bool HasProperty(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(propertyName, out _);

    private static bool TryReadOptionalStringArray(
        JsonElement json,
        string propertyName,
        out IReadOnlyList<string>? values,
        out string? error)
    {
        error = null;
        values = null;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = $"{propertyName} 必须是字符串数组。";
            return false;
        }

        var parsed = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"{propertyName} 必须是字符串数组。";
                return false;
            }

            var normalized = Normalize(item.GetString());
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                parsed.Add(normalized!);
            }
        }

        values = parsed;
        return true;
    }

    private static bool TryReadThreadSourceKinds(
        JsonElement json,
        string propertyName,
        out IReadOnlyList<KernelThreadSourceKind> sourceKinds,
        out string? error)
    {
        error = null;
        sourceKinds = Array.Empty<KernelThreadSourceKind>();
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = "sourceKinds 必须是字符串数组。";
            return false;
        }

        var parsed = new List<KernelThreadSourceKind>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = "sourceKinds 必须是字符串数组。";
                return false;
            }

            if (!KernelThreadSourceKind.TryParse(item.GetString(), out var kind) || kind is null)
            {
                error = $"不支持的 sourceKind：{item.GetString()}";
                return false;
            }

            if (!parsed.Contains(kind))
            {
                parsed.Add(kind);
            }
        }

        sourceKinds = parsed;
        return true;
    }
}
