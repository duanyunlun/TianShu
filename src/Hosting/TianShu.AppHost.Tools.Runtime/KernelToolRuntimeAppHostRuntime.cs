using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelToolRuntimeRequestContext(
    string? Model,
    string? ModelProvider,
    KernelServiceTier? ServiceTier,
    KernelApprovalPolicy? ApprovalPolicy,
    JsonElement? SandboxPolicy,
    string? SandboxMode,
    bool AllowLoginShell = true,
    KernelShellEnvironmentPolicy? ShellEnvironmentPolicy = null,
    string? Cwd = null,
    string? ProviderBaseUrl = null,
    string? ProviderApiKeyEnvironmentVariable = null,
    string? ProviderWireApi = null,
    int? ProviderRequestMaxRetries = null,
    int? ProviderStreamMaxRetries = null,
    long? ProviderStreamIdleTimeoutMs = null,
    long? ProviderWebsocketConnectTimeoutMs = null,
    bool? ProviderSupportsWebsockets = null,
    string? WebSearchMode = null,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools = null,
    string? DeveloperInstructions = null,
    string? UserInstructions = null,
    string? ReasoningSummary = null,
    string? Verbosity = null,
    KernelCollaborationModeState? CollaborationMode = null,
    KernelSessionSource? SessionSource = null)
{
    public static KernelToolRuntimeRequestContext FromTurnRequestContext(TurnRequestContext turnContext)
    {
        ArgumentNullException.ThrowIfNull(turnContext);

        return new KernelToolRuntimeRequestContext(
            Model: turnContext.Model,
            ModelProvider: turnContext.ModelProvider,
            ServiceTier: turnContext.ServiceTier,
            ApprovalPolicy: turnContext.ApprovalPolicy,
            SandboxPolicy: turnContext.SandboxPolicy,
            SandboxMode: turnContext.SandboxMode,
            AllowLoginShell: turnContext.AllowLoginShell,
            ShellEnvironmentPolicy: turnContext.ShellEnvironmentPolicy,
            Cwd: turnContext.Cwd,
            ProviderBaseUrl: turnContext.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: turnContext.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: turnContext.ProviderWireApi,
            ProviderRequestMaxRetries: turnContext.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: turnContext.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: turnContext.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: turnContext.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: turnContext.ProviderSupportsWebsockets,
            WebSearchMode: turnContext.WebSearchMode,
            DynamicTools: turnContext.DynamicTools,
            DeveloperInstructions: turnContext.DeveloperInstructions,
            UserInstructions: turnContext.UserInstructions,
            ReasoningSummary: turnContext.ReasoningSummary,
            Verbosity: turnContext.Verbosity,
            CollaborationMode: turnContext.CollaborationMode,
            SessionSource: turnContext.SessionSource);
    }
}

internal sealed class KernelToolRuntimeAppHostRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly KernelAgentOrchestrationManager agentOrchestrationManager;
    private readonly ConcurrentDictionary<string, KernelPendingPermissionRequest> pendingPermissionRequestsByCallId;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> runningTurns;
    private readonly ConcurrentDictionary<string, Task> runningTurnTasks;
    private readonly Func<string> nextThreadId;
    private readonly Func<string?, CancellationToken, Task<KernelSpawnAgentGuardConfiguration>> resolveSpawnAgentGuardConfigurationAsync;
    private readonly Func<int, KernelSpawnSlotReservation> reserveSpawnAgentSlot;
    private readonly Func<string, bool> isTrackedSpawnAgentThread;
    private readonly Action<string> releaseSpawnedAgentThread;
    private readonly Func<string?, string?, CancellationToken, Task<KernelSpawnAgentRoleDefinition>> resolveSpawnAgentRoleAsync;
    private readonly Func<KernelSpawnAgentRoleDefinition, CancellationToken, Task<KernelSpawnAgentRoleOverrides>> loadSpawnAgentRoleOverridesAsync;
    private readonly Func<KernelSpawnAgentRoleDefinition?, IReadOnlyList<string>> resolveSpawnAgentNicknameCandidates;
    private readonly Func<IReadOnlyList<string>, string?, string> reserveSpawnAgentNickname;
    private readonly Action<string, string?> registerSpawnAgentNickname;
    private readonly Action<string, KernelSessionSource?> maybeStartSubagentCompletionWatcher;
    private readonly Action<string, string> registerPendingTurnInterrupt;
    private readonly Action<string, string> enqueueSteerInput;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState, CancellationToken, Task> persistThreadConfigSnapshotAsync;
    private readonly Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot;
    private readonly Func<KernelThreadRecord, KernelRuntimeThread, string, KernelThreadSessionState, IReadOnlyList<KernelTurnInputItem>?, bool, CancellationToken, Task<string>> startBackgroundTurnAsync;
    private readonly Func<string, bool> hasRunningThread;
    private readonly Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;

    public KernelToolRuntimeAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        KernelAgentOrchestrationManager agentOrchestrationManager,
        ConcurrentDictionary<string, KernelPendingPermissionRequest> pendingPermissionRequestsByCallId,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        ConcurrentDictionary<string, Task> runningTurnTasks,
        Func<string> nextThreadId,
        Func<string?, CancellationToken, Task<KernelSpawnAgentGuardConfiguration>> resolveSpawnAgentGuardConfigurationAsync,
        Func<int, KernelSpawnSlotReservation> reserveSpawnAgentSlot,
        Func<string, bool> isTrackedSpawnAgentThread,
        Action<string> releaseSpawnedAgentThread,
        Func<string?, string?, CancellationToken, Task<KernelSpawnAgentRoleDefinition>> resolveSpawnAgentRoleAsync,
        Func<KernelSpawnAgentRoleDefinition, CancellationToken, Task<KernelSpawnAgentRoleOverrides>> loadSpawnAgentRoleOverridesAsync,
        Func<KernelSpawnAgentRoleDefinition?, IReadOnlyList<string>> resolveSpawnAgentNicknameCandidates,
        Func<IReadOnlyList<string>, string?, string> reserveSpawnAgentNickname,
        Action<string, string?> registerSpawnAgentNickname,
        Action<string, KernelSessionSource?> maybeStartSubagentCompletionWatcher,
        Action<string, string> registerPendingTurnInterrupt,
        Action<string, string> enqueueSteerInput,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelThreadRecord, KernelThreadSessionState, CancellationToken, Task> persistThreadConfigSnapshotAsync,
        Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot,
        Func<KernelThreadRecord, KernelRuntimeThread, string, KernelThreadSessionState, IReadOnlyList<KernelTurnInputItem>?, bool, CancellationToken, Task<string>> startBackgroundTurnAsync,
        Func<string, bool> hasRunningThread,
        Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.agentOrchestrationManager = agentOrchestrationManager;
        this.pendingPermissionRequestsByCallId = pendingPermissionRequestsByCallId;
        this.runningTurns = runningTurns;
        this.runningTurnTasks = runningTurnTasks;
        this.nextThreadId = nextThreadId;
        this.resolveSpawnAgentGuardConfigurationAsync = resolveSpawnAgentGuardConfigurationAsync;
        this.reserveSpawnAgentSlot = reserveSpawnAgentSlot;
        this.isTrackedSpawnAgentThread = isTrackedSpawnAgentThread;
        this.releaseSpawnedAgentThread = releaseSpawnedAgentThread;
        this.resolveSpawnAgentRoleAsync = resolveSpawnAgentRoleAsync;
        this.loadSpawnAgentRoleOverridesAsync = loadSpawnAgentRoleOverridesAsync;
        this.resolveSpawnAgentNicknameCandidates = resolveSpawnAgentNicknameCandidates;
        this.reserveSpawnAgentNickname = reserveSpawnAgentNickname;
        this.registerSpawnAgentNickname = registerSpawnAgentNickname;
        this.maybeStartSubagentCompletionWatcher = maybeStartSubagentCompletionWatcher;
        this.registerPendingTurnInterrupt = registerPendingTurnInterrupt;
        this.enqueueSteerInput = enqueueSteerInput;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.persistThreadConfigSnapshotAsync = persistThreadConfigSnapshotAsync;
        this.buildTrackedActiveTurnSnapshot = buildTrackedActiveTurnSnapshot;
        this.startBackgroundTurnAsync = startBackgroundTurnAsync;
        this.hasRunningThread = hasRunningThread;
        this.sendServerRequestAsync = sendServerRequestAsync;
        this.writeNotificationAsync = writeNotificationAsync;
    }

    public async Task UpdatePlanAsync(
        string threadId,
        string turnId,
        KernelPlanUpdateRequest request,
        CancellationToken cancellationToken)
    {
        await writeNotificationAsync("turn/plan/updated", new
        {
            threadId,
            turnId,
            explanation = request.Explanation,
            plan = request.Plan.Select(static step => new
            {
                step = step.Step,
                status = KernelToolRuntimeInteractionHelpers.NormalizePlanStatus(step.Status),
            }).ToArray(),
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KernelRequestUserInputResponse> RequestUserInputAsync(
        string threadId,
        string turnId,
        KernelRequestUserInputRequest request,
        CancellationToken cancellationToken)
    {
        var response = await sendServerRequestAsync(
            "item/tool/requestUserInput",
            new
            {
                threadId,
                turnId,
                itemId = request.ItemId,
                questions = request.Questions.Select(static question => new
                {
                    id = question.Id,
                    header = question.Header,
                    question = question.Question,
                    isOther = question.IsOther,
                    isSecret = question.IsSecret,
                    options = question.Options?.Select(static option => new
                    {
                        label = option.Label,
                        description = option.Description,
                    }).ToArray(),
                }).ToArray(),
            },
            threadId,
            cancellationToken,
            TimeSpan.FromMinutes(2)).ConfigureAwait(false);

        return KernelToolRuntimeInteractionHelpers.ParseRequestUserInputResponse(response);
    }

    public async Task<KernelRequestPermissionsResponse> RequestPermissionsAsync(
        string threadId,
        string turnId,
        KernelRequestPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        pendingPermissionRequestsByCallId[request.ItemId] = new KernelPendingPermissionRequest(
            request.ItemId,
            threadId,
            turnId,
            request.Cwd,
            KernelPermissionGrantProfile.Clone(request.Permissions));

        try
        {
            var response = await sendServerRequestAsync(
                "item/permissions/requestApproval",
                new
                {
                    threadId,
                    turnId,
                    itemId = request.ItemId,
                    reason = request.Reason,
                    permissions = request.Permissions.BuildServerPayload(),
                },
                threadId,
                cancellationToken,
                TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            if (!KernelPermissionGrantProfile.TryParseResponse(response, request.Cwd, out var permissions, out var scope, out var error))
            {
                throw new InvalidOperationException(error ?? "request_permissions returned an invalid payload.");
            }

            return new KernelRequestPermissionsResponse(permissions, scope);
        }
        finally
        {
            pendingPermissionRequestsByCallId.TryRemove(request.ItemId, out _);
        }
    }

    public async Task<KernelSpawnAgentResponse> SpawnAgentAsync(
        string parentThreadId,
        KernelToolRuntimeRequestContext parentTurnContext,
        KernelSpawnAgentRequest request,
        CancellationToken cancellationToken)
    {
        var parentRecord = await threadStore.GetThreadAsync(parentThreadId, cancellationToken).ConfigureAwait(false)
                          ?? throw new InvalidOperationException($"agent with id {parentThreadId} not found");
        if (parentRecord.IsArchived)
        {
            throw new InvalidOperationException($"agent with id {parentThreadId} is closed");
        }

        var prompt = KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(request.Message, request.Items);
        var inputItems = KernelToolRuntimeInteractionHelpers.BuildCollabTurnInputItems(request.Items);
        var newThreadId = nextThreadId();
        var parentRuntime = threadManager.GetOrAttachThread(parentRecord, buildDefaultThreadSession, loaded: true);
        var guardConfiguration = await resolveSpawnAgentGuardConfigurationAsync(
            Normalize(parentTurnContext.Cwd) ?? parentRecord.Cwd,
            cancellationToken).ConfigureAwait(false);
        var childDepth = ResolveNextThreadSpawnDepth(parentRuntime.Session.SessionSource);
        EnsureSpawnAgentDepthWithinLimit(childDepth, guardConfiguration.MaxDepth);
        using var spawnSlotReservation = reserveSpawnAgentSlot(guardConfiguration.MaxThreads);
        var requestedAgentType = Normalize(request.AgentType);
        var normalizedRequestedOverrides = KernelToolRuntimeInteractionHelpers.NormalizeSpawnAgentRequestedModelAndReasoning(
            Normalize(parentTurnContext.Model) ?? parentRuntime.Session.Model,
            request.Model,
            request.ReasoningEffort);
        var resolvedAgentRole = await resolveSpawnAgentRoleAsync(
            Normalize(parentTurnContext.Cwd) ?? parentRecord.Cwd,
            request.AgentType,
            cancellationToken).ConfigureAwait(false);
        var resolvedAgentRoleOverrides = await loadSpawnAgentRoleOverridesAsync(resolvedAgentRole, cancellationToken).ConfigureAwait(false);
        var effectiveAgentType = requestedAgentType;
        var agentNickname = reserveSpawnAgentNickname(resolveSpawnAgentNicknameCandidates(resolvedAgentRole), null);
        var childEphemeral = parentRuntime.Session.Ephemeral;
        KernelTurnRecord? liveParentTurn = null;
        if (request.ForkContext)
        {
            await persistThreadConfigSnapshotAsync(parentRecord, parentRuntime.Session, cancellationToken).ConfigureAwait(false);
            liveParentTurn = buildTrackedActiveTurnSnapshot(parentThreadId, parentRuntime.ActiveTurnId);
            liveParentTurn = KernelToolRuntimeInteractionHelpers.InjectForkedSpawnAgentToolItems(
                liveParentTurn,
                request.ParentCallId,
                request);
        }

        var childRecord = request.ForkContext
            ? await threadStore.ForkThreadAsync(parentThreadId, newThreadId, parentRecord.Cwd, cancellationToken, childEphemeral, liveParentTurn).ConfigureAwait(false)
            : await threadStore.CreateThreadAsync(newThreadId, parentRecord.Cwd, cancellationToken, childEphemeral).ConfigureAwait(false);
        if (childRecord is null)
        {
            throw new InvalidOperationException($"agent with id {parentThreadId} not found");
        }

        var childSession = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(
            parentRuntime.Session,
            BuildSpawnedAgentSessionContext(parentTurnContext),
            childRecord.Cwd,
            resolvedAgentRoleOverrides.Model ?? normalizedRequestedOverrides.Model,
            resolvedAgentRoleOverrides.ReasoningEffort ?? normalizedRequestedOverrides.ReasoningEffort,
            KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSource(parentRuntime.Session.SessionSource, parentThreadId, effectiveAgentType, agentNickname, childDepth),
            resolvedAgentRoleOverrides.DeveloperInstructions);
        var childRuntime = threadManager.AttachThread(childRecord, childSession, loaded: true, publishCreated: true);

        if (!string.IsNullOrWhiteSpace(effectiveAgentType) || !string.IsNullOrWhiteSpace(agentNickname))
        {
            var updatedRecord = await threadStore.SetThreadAgentMetadataAsync(newThreadId, agentNickname, effectiveAgentType, cancellationToken).ConfigureAwait(false);
            if (updatedRecord is not null)
            {
                childRecord = updatedRecord;
                childRuntime.Update(updatedRecord, childSession, loaded: true);
            }
        }

        registerSpawnAgentNickname(newThreadId, childRecord.AgentNickname ?? agentNickname);
        spawnSlotReservation.Commit(newThreadId);

        _ = await startBackgroundTurnAsync(
            childRecord,
            childRuntime,
            prompt,
            childSession,
            inputItems,
            string.Equals(effectiveAgentType, "worker", StringComparison.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);
        maybeStartSubagentCompletionWatcher(newThreadId, childSession.SessionSource);
        return new KernelSpawnAgentResponse(newThreadId, childRecord.AgentNickname);
    }

    public async Task<KernelSendInputResponse> SendInputToAgentAsync(
        KernelToolRuntimeRequestContext parentTurnContext,
        KernelSendInputRequest request,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(request.Id, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"agent with id {request.Id} not found");
        if (record.IsArchived)
        {
            throw new InvalidOperationException($"agent with id {request.Id} is closed");
        }

        var prompt = KernelToolRuntimeInteractionHelpers.BuildCollabPrompt(request.Message, request.Items);
        var inputItems = KernelToolRuntimeInteractionHelpers.BuildCollabTurnInputItems(request.Items);
        var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
        var activeTurnId = runtimeThread.ActiveTurnId;
        if (!string.IsNullOrWhiteSpace(activeTurnId) && runningTurns.ContainsKey(activeTurnId))
        {
            if (request.Interrupt)
            {
                if (runningTurns.TryGetValue(activeTurnId, out var running))
                {
                    registerPendingTurnInterrupt(request.Id, activeTurnId);
                    running.Cancel();
                }

                if (runningTurnTasks.TryGetValue(activeTurnId, out var runningTask))
                {
                    _ = await Task.WhenAny(runningTask, Task.Delay(500, cancellationToken)).ConfigureAwait(false);
                }
            }
            else
            {
                enqueueSteerInput(activeTurnId, prompt);
                return new KernelSendInputResponse($"steer_{Guid.NewGuid():N}");
            }
        }

        var session = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(
            runtimeThread.Session,
            BuildSpawnedAgentSessionContext(parentTurnContext),
            record.Cwd);
        runtimeThread.UpdateSession(session);
        var turnId = await startBackgroundTurnAsync(
            record,
            runtimeThread,
            prompt,
            session,
            inputItems,
            false,
            cancellationToken).ConfigureAwait(false);
        return new KernelSendInputResponse(turnId);
    }

    public async Task<JsonNode?> ResumeAgentAsync(
        string parentThreadId,
        KernelToolRuntimeRequestContext parentTurnContext,
        string agentId,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(agentId, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"agent with id {agentId} not found");

        var currentStatus = await GetAgentStatusNodeAsync(
            agentId,
            treatArchivedAsNotFound: true,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(currentStatus?.ToJsonString(), "\"not_found\"", StringComparison.Ordinal))
        {
            return currentStatus;
        }

        var guardConfiguration = await resolveSpawnAgentGuardConfigurationAsync(
            Normalize(parentTurnContext.Cwd) ?? record.Cwd,
            cancellationToken).ConfigureAwait(false);
        var childDepth = ResolveNextThreadSpawnDepth(parentTurnContext.SessionSource);
        EnsureSpawnAgentDepthWithinLimit(childDepth, guardConfiguration.MaxDepth);

        KernelSpawnSlotReservation? spawnSlotReservation = null;
        if (!isTrackedSpawnAgentThread(agentId))
        {
            spawnSlotReservation = reserveSpawnAgentSlot(guardConfiguration.MaxThreads);
        }

        try
        {
            if (record.IsArchived)
            {
                var unarchived = await threadStore.SetThreadArchivedAsync(agentId, archived: false, cancellationToken).ConfigureAwait(false);
                if (unarchived is not null)
                {
                    record = unarchived;
                }
            }

            var runtimeThread = threadManager.GetOrAttachThread(record, buildDefaultThreadSession, loaded: true);
            var restoredSessionSource = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSource(
                parentTurnContext.SessionSource ?? runtimeThread.Session.SessionSource,
                parentThreadId,
                record.AgentRole,
                record.AgentNickname,
                childDepth);
            var restoredSession = KernelToolRuntimeAgentHelpers.BuildSpawnedAgentSession(
                runtimeThread.Session,
                BuildSpawnedAgentSessionContext(parentTurnContext),
                cwdOverride: null,
                sessionSourceOverride: restoredSessionSource);
            runtimeThread.Update(record, restoredSession, loaded: true);

            registerSpawnAgentNickname(agentId, record.AgentNickname ?? restoredSession.SessionSource.SubAgentSource?.AgentNickname);
            spawnSlotReservation?.Commit(agentId);
            maybeStartSubagentCompletionWatcher(agentId, restoredSession.SessionSource);
            return await GetAgentStatusNodeAsync(agentId, treatArchivedAsNotFound: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            spawnSlotReservation?.Dispose();
        }
    }

    public async Task<KernelWaitAgentsResponse> WaitOnAgentsAsync(
        IReadOnlyList<string> agentIds,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var effectiveTimeoutMs = KernelToolRuntimeAgentHelpers.NormalizeWaitTimeoutMs(timeoutMs);

        var initialFinalStatuses = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var agentId in agentIds)
        {
            var status = await GetAgentStatusNodeAsync(agentId, treatArchivedAsNotFound: true, cancellationToken).ConfigureAwait(false);
            if (KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(status))
            {
                initialFinalStatuses[agentId] = status;
            }
        }

        if (initialFinalStatuses.Count > 0)
        {
            return new KernelWaitAgentsResponse(initialFinalStatuses, TimedOut: false);
        }

        var deadline = DateTimeOffset.UtcNow.AddMilliseconds((double)effectiveTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var finalStatuses = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var agentId in agentIds)
            {
                var status = await GetAgentStatusNodeAsync(agentId, treatArchivedAsNotFound: true, cancellationToken).ConfigureAwait(false);
                if (KernelToolRuntimeAgentHelpers.IsFinalAgentStatus(status))
                {
                    finalStatuses[agentId] = status;
                }
            }

            if (finalStatuses.Count > 0)
            {
                return new KernelWaitAgentsResponse(finalStatuses, TimedOut: false);
            }

            await Task.Delay(KernelToolRuntimeAgentHelpers.WaitPollInterval, cancellationToken).ConfigureAwait(false);
        }

        return new KernelWaitAgentsResponse(new Dictionary<string, JsonNode?>(StringComparer.Ordinal), TimedOut: true);
    }

    public async Task<bool> ReportAgentJobResultAsync(
        string reportingThreadId,
        string jobId,
        string itemId,
        JsonElement result,
        bool stop,
        CancellationToken cancellationToken)
    {
        var recorded = await agentOrchestrationManager.RecordItemResultAsync(
            jobId,
            itemId,
            reportingThreadId,
            result.GetRawText(),
            cancellationToken).ConfigureAwait(false);
        if (recorded is null)
        {
            return false;
        }

        if (stop)
        {
            _ = await agentOrchestrationManager.CancelJobAsync(
                jobId,
                "cancelled by worker request",
                cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<JsonNode?> CloseAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(agentId, cancellationToken).ConfigureAwait(false)
                     ?? throw new InvalidOperationException($"agent with id {agentId} not found");
        if (record.IsArchived)
        {
            throw new InvalidOperationException($"agent with id {agentId} is closed");
        }

        var status = await GetAgentStatusNodeAsync(agentId, treatArchivedAsNotFound: false, cancellationToken).ConfigureAwait(false);
        if (threadManager.TryGetThread(agentId, out var runtimeThread) && runtimeThread?.ActiveTurnId is { Length: > 0 } activeTurnId)
        {
            if (runningTurns.TryGetValue(activeTurnId, out var running))
            {
                registerPendingTurnInterrupt(agentId, activeTurnId);
                running.Cancel();
            }
        }

        _ = await threadStore.SetThreadArchivedAsync(agentId, archived: true, cancellationToken).ConfigureAwait(false);
        releaseSpawnedAgentThread(agentId);
        threadManager.MarkUnloaded(agentId);
        return status;
    }

    public async Task<JsonNode?> GetAgentStatusNodeAsync(
        string agentId,
        bool treatArchivedAsNotFound,
        CancellationToken cancellationToken)
    {
        var record = await threadStore.GetThreadAsync(agentId, cancellationToken).ConfigureAwait(false);
        return KernelToolRuntimeAgentHelpers.BuildAgentStatusNode(
            record,
            treatArchivedAsNotFound,
            hasRunningThread(agentId));
    }

    private static int ResolveNextThreadSpawnDepth(KernelSessionSource? sessionSource)
        => Math.Max(sessionSource?.GetThreadSpawnDepth() ?? 0, 0) + 1;

    private static void EnsureSpawnAgentDepthWithinLimit(int childDepth, int maxDepth)
    {
        if (childDepth > maxDepth)
        {
            throw new InvalidOperationException("Agent depth limit reached. Solve the task yourself.");
        }
    }

    private static string? Normalize(string? value) => KernelToolJsonHelpers.Normalize(value);

    private static KernelSpawnedAgentSessionContext BuildSpawnedAgentSessionContext(KernelToolRuntimeRequestContext context)
    {
        return new KernelSpawnedAgentSessionContext(
            Model: context.Model,
            ModelProvider: context.ModelProvider,
            ServiceTier: context.ServiceTier,
            ApprovalPolicy: context.ApprovalPolicy,
            SandboxPolicy: context.SandboxPolicy,
            SandboxMode: context.SandboxMode,
            AllowLoginShell: context.AllowLoginShell,
            ShellEnvironmentPolicy: context.ShellEnvironmentPolicy,
            Cwd: context.Cwd,
            ProviderBaseUrl: context.ProviderBaseUrl,
            ProviderApiKeyEnvironmentVariable: context.ProviderApiKeyEnvironmentVariable,
            ProviderWireApi: context.ProviderWireApi,
            ProviderRequestMaxRetries: context.ProviderRequestMaxRetries,
            ProviderStreamMaxRetries: context.ProviderStreamMaxRetries,
            ProviderStreamIdleTimeoutMs: context.ProviderStreamIdleTimeoutMs,
            ProviderWebsocketConnectTimeoutMs: context.ProviderWebsocketConnectTimeoutMs,
            ProviderSupportsWebsockets: context.ProviderSupportsWebsockets,
            WebSearchMode: context.WebSearchMode,
            DynamicTools: context.DynamicTools,
            DeveloperInstructions: context.DeveloperInstructions,
            UserInstructions: context.UserInstructions,
            ReasoningSummary: context.ReasoningSummary,
            Verbosity: context.Verbosity,
            CollaborationMode: context.CollaborationMode);
    }
}
