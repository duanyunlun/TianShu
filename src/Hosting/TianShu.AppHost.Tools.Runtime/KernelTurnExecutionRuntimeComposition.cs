using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Orchestration;
using TianShu.Contracts.Primitives;
using TianShu.Diagnostics;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelTurnExecutionRuntimeComposition
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<string?, string?> normalize;
    private readonly Func<KernelCollaborationModeState?, bool> isPlanCollaborationMode;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;
    private readonly Func<KernelRuntimeThread, CancellationToken, Task> persistRuntimeThreadSessionSnapshotAsync;
    private readonly Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync;
    private readonly Func<TurnOperationState, Task> ensureAgentMessageStartedAsync;
    private readonly Func<string, string, TurnRequestContext, Activity?> startTurnActivity;
    private readonly Func<string?, ContextBudgetProfile> resolveContextBudgetProfile;
    private readonly Func<string, TurnRequestContext, CancellationToken, Task<IReadOnlyList<ContextSegment>>> resolveContextOverlaySegmentsAsync;
    private readonly Func<string, string, string, string, TurnRequestContext, CancellationToken, Task> maybeExtractMemoryFromCompletedTurnAsync;
    private readonly Func<string, string, TurnRequestContext, CancellationToken, Task> maybeRunPreSamplingAutoCompactAsync;
    private readonly Func<string?, string> buildRealtimeStartDeveloperInstruction;
    private readonly Func<TurnOperationState, IEnumerable<JsonElement>, IEnumerable<JsonElement>, CancellationToken, Task> emitWebSearchOutputItemNotificationsAsync;
    private readonly Func<TurnOperationState, IEnumerable<JsonElement>, string?, CancellationToken, Task> emitImageGenerationOutputItemNotificationsAsync;
    private readonly IDiagnosticEventSink diagnosticEventSink;
    private readonly IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly string defaultModel;
    private readonly TimeSpan responsesStreamRetryBaseDelay;
    private readonly TimeSpan maxResponsesStreamRetryDelay;
    private readonly KernelTurnBackgroundSchedulerRuntime backgroundTurnSchedulerRuntime;
    private readonly KernelTurnInterruptRuntime turnInterruptRuntime;
    private readonly KernelTurnActiveSnapshotRuntime activeSnapshotRuntime;
    private readonly KernelTurnStartStateRuntime turnStartStateRuntime;
    private readonly KernelTurnLaunchRuntime turnLaunchRuntime;
    private readonly KernelTurnTerminalStateRuntime terminalStateRuntime;
    private readonly KernelTurnFinalizationRuntime finalizationRuntime;
    private readonly KernelTurnStageNotificationRuntime stageNotificationRuntime;
    private readonly KernelResponsesStreamNotificationRuntime responsesStreamNotificationRuntime;
    private readonly KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime;
    private readonly KernelResponsesStreamProcessingRuntime responsesStreamProcessingRuntime;
    private readonly KernelResponsesAssistantCompletionRuntime responsesAssistantCompletionRuntime;
    private readonly KernelResponsesRequestCompositionRuntime responsesRequestCompositionRuntime;
    private readonly KernelResponsesToolContinuationRuntime responsesToolContinuationRuntime;
    private readonly KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime;
    private readonly KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime;
    private readonly KernelResponsesWebSocketStreamTransportRuntime responsesWebSocketStreamTransportRuntime;
    private readonly KernelTurnRunnerRuntime turnRunnerRuntime;
    private readonly KernelTurnModelStageRuntime modelStageRuntime;
    private readonly KernelTurnOperationChainRuntime operationChainRuntime;
    private readonly KernelTerminalTurnProjectionCommitRuntime terminalTurnProjectionCommitRuntime;
    private readonly KernelTurnInputResolutionRuntime inputResolutionRuntime;
    private readonly KernelTurnDependencyResolutionRuntime dependencyResolutionRuntime;
    private readonly KernelTurnProviderAssistantRuntime providerAssistantRuntime;
    private readonly KernelTurnAssistantExecutionRuntime assistantExecutionRuntime;
    private readonly KernelTurnAssistantOutputStreamingRuntime assistantOutputStreamingRuntime;
    private readonly KernelTurnSteerInputRuntime steerInputRuntime;
    private readonly KernelContextSlicingDiagnosticsRuntime contextSlicingDiagnosticsRuntime;
    private readonly KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime;

    public KernelTurnLaunchRuntime TurnLaunchRuntime
        => turnLaunchRuntime;

    public KernelTurnInterruptRuntime TurnInterruptRuntime
        => turnInterruptRuntime;

    public KernelTurnRunnerRuntime TurnRunnerRuntime
        => turnRunnerRuntime;

    public KernelTerminalTurnProjectionCommitRuntime TerminalTurnProjectionCommitRuntime
        => terminalTurnProjectionCommitRuntime;

    public KernelTurnExecutionRuntimeComposition(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        ConcurrentDictionary<string, CancellationTokenSource> runningTurns,
        ConcurrentDictionary<string, Task> runningTurnTasks,
        ConcurrentDictionary<string, ConcurrentQueue<string>> steerInputsByTurn,
        ConcurrentDictionary<string, KernelPermissionGrantProfile> grantedPermissionTurnByTurn,
        Func<JsonElement, int, string, object?, CancellationToken, Task> writeErrorAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<string> nextTurnId,
        Func<long> nextUserMessageItemSequence,
        Func<string?, int> countTextChars,
        Func<KernelTurnStartRequest, int> countTurnInputTextChars,
        int maxUserInputTextChars,
        Func<string?, string?> normalize,
        Func<KernelCollaborationModeState?, bool> isPlanCollaborationMode,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<KernelThreadSessionState, KernelTurnStartRequest, KernelThreadSessionState> applyTurnOverrides,
        Func<KernelRuntimeThread, KernelThreadSessionState, KernelTurnStartRequest, CancellationToken, Task<TurnRequestContext>> buildTurnRequestContext,
        Func<KernelThreadSessionState, CancellationToken, Task> updateMcpSandboxStateAsync,
        Func<KernelTurnStartRequest, string> extractUserText,
        Action<string, string, string?, IReadOnlyList<KernelTurnInputItem>?> seedTrackedTurnUserMessage,
        Func<string, IReadOnlyList<string>> drainSteerInputs,
        Func<string?, string?, KernelTurnRecord?> buildTrackedActiveTurnSnapshot,
        Func<string, string, string, object> createResponsesMessage,
        Func<string, CancellationToken, Task<bool>> isEphemeralThreadAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistRolloutAsync,
        Func<KernelRuntimeThread, CancellationToken, Task> persistRuntimeThreadSessionSnapshotAsync,
        Func<KernelThreadRecord, CancellationToken, Task> writeThreadStatusChangedAsync,
        Func<string, string, string, CancellationToken, bool, Task> resolvePendingInteractiveRequestsForThreadLifecycleAsync,
        Func<string?, string?, CancellationToken, Task> flushPendingTurnInterruptResponsesAsync,
        Action<string, string> registerPendingTurnInterrupt,
        Action<string, string, JsonElement> registerPendingTurnInterruptResponse,
        Action<string, string> clearPendingTurnInterrupt,
        Action<string?, string?> clearPendingTurnInterruptResponses,
        Func<string, string, TurnRequestContext, string, string?, string?, string?, string?, string, KernelTurnErrorRecord?, bool, Task> persistTurnSessionBeforeTerminalAsync,
        Func<string, string, string, string?, string> getTrackedAgentMessageText,
        Func<TurnOperationState, Task> ensureAgentMessageStartedAsync,
        Func<TurnOperationState, Task> completePlanItemAsync,
        Func<string, string, TurnRequestContext, Activity?> startTurnActivity,
        Func<string, CancellationToken, Task<string>> captureThreadGitDiffAsync,
        Action<string> deactivateCodeModeTurn,
        Func<string, Task> disposeJsReplManagerAsync,
        Func<IReadOnlyList<KernelTurnInputItem>?, string, CancellationToken, Task<string?>> buildExplicitPluginInstructionsAsync,
        Func<TurnRequestContext, string, CancellationToken, Task<List<KernelSkillDescriptor>>> resolveMentionedSkillsAsync,
        Func<IReadOnlyList<KernelSkillDescriptor>, List<string>> buildSkillInjectionMessages,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillEnvironmentDependenciesAsync,
        Func<TurnOperationState, TurnRequestContext, IReadOnlyList<KernelSkillDescriptor>, CancellationToken, Task> resolveSkillMcpDependenciesAsync,
        Func<string?, ContextBudgetProfile> resolveContextBudgetProfile,
        Func<string, TurnRequestContext, CancellationToken, Task<IReadOnlyList<ContextSegment>>> resolveContextOverlaySegmentsAsync,
        Func<string, string, string, string, TurnRequestContext, CancellationToken, Task> maybeExtractMemoryFromCompletedTurnAsync,
        Func<string, object, string, CancellationToken, TimeSpan?, Task<JsonElement>> sendServerRequestAsync,
        Func<string, string, KernelReadinessFlag, string, JsonElement, TurnRequestContext, CancellationToken, Task<string>> executeInlineToolCallAsync,
        Func<string?, KernelProposedPlanExtraction> extractProposedPlanText,
        Func<string, string, TurnRequestContext, CancellationToken, Task> maybeRunPreSamplingAutoCompactAsync,
        Func<string?, string> buildRealtimeStartDeveloperInstruction,
        Func<TurnRequestContext, CancellationToken, Task<KernelResponsesNativeToolOptions>> resolveResponsesNativeToolOptionsAsync,
        Func<TurnOperationState, IEnumerable<JsonElement>, IEnumerable<JsonElement>, CancellationToken, Task> emitWebSearchOutputItemNotificationsAsync,
        Func<TurnOperationState, IEnumerable<JsonElement>, string?, CancellationToken, Task> emitImageGenerationOutputItemNotificationsAsync,
        Func<string, string, IReadOnlyList<object>, IReadOnlyList<object>, TurnRequestContext, CancellationToken, Task<List<object>?>> maybeBuildMidTurnAutoCompactedFollowUpInputAsync,
        Func<ModelFunctionCall, bool, KernelAsyncReadWriteLock, TurnOperationState, TurnRequestContext, CancellationToken, Task<object>> executeModelFunctionCallWithParallelLockAsync,
        IDiagnosticEventSink diagnosticEventSink,
        IDiagnosticOperationScopeFactory diagnosticOperationScopeFactory,
        IDiagnosticArtifactWriter? providerRequestPayloadArtifactWriter,
        HttpClient providerHttpClient,
        JsonSerializerOptions jsonOptions,
        string defaultModel,
        int responsesStreamMaxRetries,
        TimeSpan responsesStreamIdleTimeout,
        TimeSpan responsesStreamRetryBaseDelay,
        TimeSpan maxResponsesStreamRetryDelay,
        Action<HttpRequestMessage> applyW3cTraceContext,
        KernelToolRegistry toolRegistry)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.normalize = normalize;
        this.isPlanCollaborationMode = isPlanCollaborationMode;
        this.writeNotificationAsync = writeNotificationAsync;
        this.persistTurnLogAsync = persistTurnLogAsync;
        this.persistRuntimeThreadSessionSnapshotAsync = persistRuntimeThreadSessionSnapshotAsync;
        this.resolvePendingInteractiveRequestsForThreadLifecycleAsync = resolvePendingInteractiveRequestsForThreadLifecycleAsync;
        this.ensureAgentMessageStartedAsync = ensureAgentMessageStartedAsync;
        this.startTurnActivity = startTurnActivity;
        this.resolveContextBudgetProfile = resolveContextBudgetProfile;
        this.resolveContextOverlaySegmentsAsync = resolveContextOverlaySegmentsAsync;
        this.maybeExtractMemoryFromCompletedTurnAsync = maybeExtractMemoryFromCompletedTurnAsync;
        this.maybeRunPreSamplingAutoCompactAsync = maybeRunPreSamplingAutoCompactAsync;
        this.buildRealtimeStartDeveloperInstruction = buildRealtimeStartDeveloperInstruction;
        this.emitWebSearchOutputItemNotificationsAsync = emitWebSearchOutputItemNotificationsAsync;
        this.emitImageGenerationOutputItemNotificationsAsync = emitImageGenerationOutputItemNotificationsAsync;
        this.diagnosticEventSink = diagnosticEventSink;
        this.diagnosticOperationScopeFactory = diagnosticOperationScopeFactory;
        this.jsonOptions = jsonOptions;
        this.defaultModel = defaultModel;
        this.responsesStreamRetryBaseDelay = responsesStreamRetryBaseDelay;
        this.maxResponsesStreamRetryDelay = maxResponsesStreamRetryDelay;
        backgroundTurnSchedulerRuntime = new KernelTurnBackgroundSchedulerRuntime(
            runningTurns,
            runningTurnTasks);
        turnInterruptRuntime = new KernelTurnInterruptRuntime(
            threadStore,
            backgroundTurnSchedulerRuntime,
            writeErrorAsync,
            writeResultAsync,
            normalize,
            registerPendingTurnInterrupt,
            registerPendingTurnInterruptResponse,
            clearPendingTurnInterrupt,
            clearPendingTurnInterruptResponses);
        finalizationRuntime = new KernelTurnFinalizationRuntime(
            threadManager,
            backgroundTurnSchedulerRuntime,
            steerInputsByTurn,
            grantedPermissionTurnByTurn,
            captureThreadGitDiffAsync,
            writeNotificationAsync,
            deactivateCodeModeTurn,
            disposeJsReplManagerAsync);
        stageNotificationRuntime = new KernelTurnStageNotificationRuntime(
            normalize,
            writeNotificationAsync,
            getTrackedAgentMessageText,
            ensureAgentMessageStartedAsync,
            completePlanItemAsync);
        responsesStreamNotificationRuntime = new KernelResponsesStreamNotificationRuntime(
            ensureAgentMessageStartedAsync,
            writeNotificationAsync);
        responsesStreamFailureRuntime = new KernelResponsesStreamFailureRuntime();
        responsesStreamProcessingRuntime = new KernelResponsesStreamProcessingRuntime(
            responsesStreamNotificationRuntime,
            responsesStreamFailureRuntime);
        responsesAssistantCompletionRuntime = new KernelResponsesAssistantCompletionRuntime(
            normalize,
            extractProposedPlanText,
            createResponsesMessage,
            persistTurnLogAsync);
        responsesRequestCompositionRuntime = new KernelResponsesRequestCompositionRuntime(
            toolRegistry,
            resolveResponsesNativeToolOptionsAsync,
            persistTurnLogAsync,
            jsonOptions,
            responsesStreamMaxRetries,
            responsesStreamIdleTimeout);
        responsesToolContinuationRuntime = new KernelResponsesToolContinuationRuntime(
            toolRegistry,
            executeModelFunctionCallWithParallelLockAsync);
        activeSnapshotRuntime = new KernelTurnActiveSnapshotRuntime(
            threadStore,
            threadManager,
            buildTrackedActiveTurnSnapshot,
            isEphemeralThreadAsync);
        turnStartStateRuntime = new KernelTurnStartStateRuntime(
            threadStore,
            normalize,
            persistTurnLogAsync,
            writeThreadStatusChangedAsync);
        turnLaunchRuntime = new KernelTurnLaunchRuntime(
            threadStore,
            threadManager,
            writeErrorAsync,
            writeResultAsync,
            nextTurnId,
            countTextChars,
            countTurnInputTextChars,
            maxUserInputTextChars,
            buildDefaultThreadSession,
            applyTurnOverrides,
            buildTurnRequestContext,
            updateMcpSandboxStateAsync,
            extractUserText,
            seedTrackedTurnUserMessage,
            backgroundTurnSchedulerRuntime,
            turnStartStateRuntime,
            (threadId, turnId, userText, turnContext, persistExtendedHistory, cts) =>
                turnRunnerRuntime!.RunAsync(
                    threadId,
                    turnId,
                    userText,
                    turnContext,
                    persistExtendedHistory,
                    cts.Token));
        terminalStateRuntime = new KernelTurnTerminalStateRuntime(
            threadStore,
            normalize,
            persistTurnLogAsync,
            persistRolloutAsync,
            writeThreadStatusChangedAsync,
            persistTurnSessionBeforeTerminalAsync,
            resolvePendingInteractiveRequestsForThreadLifecycleAsync,
            flushPendingTurnInterruptResponsesAsync,
            writeNotificationAsync);
        terminalTurnProjectionCommitRuntime = new KernelTerminalTurnProjectionCommitRuntime(
            threadStore,
            normalize,
            persistTurnLogAsync);
        dependencyResolutionRuntime = new KernelTurnDependencyResolutionRuntime(
            buildExplicitPluginInstructionsAsync,
            resolveMentionedSkillsAsync,
            buildSkillInjectionMessages,
            resolveSkillEnvironmentDependenciesAsync,
            resolveSkillMcpDependenciesAsync);
        assistantOutputStreamingRuntime = new KernelTurnAssistantOutputStreamingRuntime(responsesStreamNotificationRuntime);
        steerInputRuntime = new KernelTurnSteerInputRuntime(
            drainSteerInputs,
            createResponsesMessage,
            writeNotificationAsync,
            nextUserMessageItemSequence,
            normalize);
        inputResolutionRuntime = new KernelTurnInputResolutionRuntime(
            steerInputRuntime,
            normalize,
            sendServerRequestAsync);
        responsesFollowUpInputRuntime = new KernelResponsesFollowUpInputRuntime(
            maybeBuildMidTurnAutoCompactedFollowUpInputAsync,
            steerInputRuntime);
        contextSlicingDiagnosticsRuntime = new KernelContextSlicingDiagnosticsRuntime(
            diagnosticEventSink,
            diagnosticOperationScopeFactory,
            jsonOptions);
        providerRequestDiagnosticsRuntime = new KernelProviderRequestDiagnosticsRuntime(
            diagnosticEventSink,
            diagnosticOperationScopeFactory,
            providerRequestPayloadArtifactWriter,
            jsonOptions);
        responsesHttpStreamTransportRuntime = new KernelResponsesHttpStreamTransportRuntime(
            providerHttpClient,
            jsonOptions,
            applyW3cTraceContext,
            providerRequestDiagnosticsRuntime,
            responsesStreamFailureRuntime,
            writeNotificationAsync,
            responsesStreamProcessingRuntime.ProcessAsync,
            responsesStreamRetryBaseDelay,
            maxResponsesStreamRetryDelay);
        responsesWebSocketStreamTransportRuntime = new KernelResponsesWebSocketStreamTransportRuntime(
            jsonOptions,
            providerRequestDiagnosticsRuntime,
            responsesStreamFailureRuntime,
            responsesHttpStreamTransportRuntime,
            persistRuntimeThreadSessionSnapshotAsync,
            writeNotificationAsync,
            responsesStreamProcessingRuntime.ProcessAsync,
            responsesStreamRetryBaseDelay,
            maxResponsesStreamRetryDelay);
        providerAssistantRuntime = new KernelTurnProviderAssistantRuntime(
            threadStore,
            threadManager,
            normalize,
            defaultModel,
            maybeRunPreSamplingAutoCompactAsync,
            buildRealtimeStartDeveloperInstruction,
            resolveContextBudgetProfile,
            resolveContextOverlaySegmentsAsync,
            emitWebSearchOutputItemNotificationsAsync,
            emitImageGenerationOutputItemNotificationsAsync,
            contextSlicingDiagnosticsRuntime,
            responsesAssistantCompletionRuntime,
            responsesRequestCompositionRuntime,
            responsesToolContinuationRuntime,
            responsesFollowUpInputRuntime,
            responsesHttpStreamTransportRuntime,
            responsesWebSocketStreamTransportRuntime);
        assistantExecutionRuntime = new KernelTurnAssistantExecutionRuntime(
            executeInlineToolCallAsync,
            providerAssistantRuntime.ExecuteAsync,
            extractProposedPlanText);
        operationChainRuntime = new KernelTurnOperationChainRuntime(
            inputResolutionRuntime,
            dependencyResolutionRuntime,
            assistantExecutionRuntime,
            assistantOutputStreamingRuntime,
            steerInputRuntime);
        modelStageRuntime = new KernelTurnModelStageRuntime(
            isPlanCollaborationMode,
            normalize,
            resolvePendingInteractiveRequestsForThreadLifecycleAsync,
            ensureAgentMessageStartedAsync,
            startTurnActivity,
            maybeExtractMemoryFromCompletedTurnAsync,
            activeSnapshotRuntime,
            terminalStateRuntime,
            finalizationRuntime,
            stageNotificationRuntime,
            operationChainRuntime);
        turnRunnerRuntime = new KernelTurnRunnerRuntime(modelStageRuntime.ExecuteAsync);
    }
}
