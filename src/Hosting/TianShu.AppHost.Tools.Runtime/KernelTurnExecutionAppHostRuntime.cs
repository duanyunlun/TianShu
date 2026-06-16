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

internal sealed class KernelTurnExecutionAppHostRuntime
{
    private readonly KernelTurnExecutionRuntimeComposition composition;

    public KernelTurnExecutionAppHostRuntime(
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
        composition = new KernelTurnExecutionRuntimeComposition(
            threadStore,
            threadManager,
            runningTurns,
            runningTurnTasks,
            steerInputsByTurn,
            grantedPermissionTurnByTurn,
            writeErrorAsync,
            writeResultAsync,
            nextTurnId,
            nextUserMessageItemSequence,
            countTextChars,
            countTurnInputTextChars,
            maxUserInputTextChars,
            normalize,
            isPlanCollaborationMode,
            buildDefaultThreadSession,
            applyTurnOverrides,
            buildTurnRequestContext,
            updateMcpSandboxStateAsync,
            extractUserText,
            seedTrackedTurnUserMessage,
            drainSteerInputs,
            buildTrackedActiveTurnSnapshot,
            createResponsesMessage,
            isEphemeralThreadAsync,
            writeNotificationAsync,
            persistTurnLogAsync,
            persistRolloutAsync,
            persistRuntimeThreadSessionSnapshotAsync,
            writeThreadStatusChangedAsync,
            resolvePendingInteractiveRequestsForThreadLifecycleAsync,
            flushPendingTurnInterruptResponsesAsync,
            registerPendingTurnInterrupt,
            registerPendingTurnInterruptResponse,
            clearPendingTurnInterrupt,
            clearPendingTurnInterruptResponses,
            persistTurnSessionBeforeTerminalAsync,
            getTrackedAgentMessageText,
            ensureAgentMessageStartedAsync,
            completePlanItemAsync,
            startTurnActivity,
            captureThreadGitDiffAsync,
            deactivateCodeModeTurn,
            disposeJsReplManagerAsync,
            buildExplicitPluginInstructionsAsync,
            resolveMentionedSkillsAsync,
            buildSkillInjectionMessages,
            resolveSkillEnvironmentDependenciesAsync,
            resolveSkillMcpDependenciesAsync,
            resolveContextBudgetProfile,
            resolveContextOverlaySegmentsAsync,
            maybeExtractMemoryFromCompletedTurnAsync,
            sendServerRequestAsync,
            executeInlineToolCallAsync,
            extractProposedPlanText,
            maybeRunPreSamplingAutoCompactAsync,
            buildRealtimeStartDeveloperInstruction,
            resolveResponsesNativeToolOptionsAsync,
            emitWebSearchOutputItemNotificationsAsync,
            emitImageGenerationOutputItemNotificationsAsync,
            maybeBuildMidTurnAutoCompactedFollowUpInputAsync,
            executeModelFunctionCallWithParallelLockAsync,
            diagnosticEventSink,
            diagnosticOperationScopeFactory,
            providerRequestPayloadArtifactWriter,
            providerHttpClient,
            jsonOptions,
            defaultModel,
            responsesStreamMaxRetries,
            responsesStreamIdleTimeout,
            responsesStreamRetryBaseDelay,
            maxResponsesStreamRetryDelay,
            applyW3cTraceContext,
            toolRegistry);
    }

    public async Task HandleTurnStartAsync(
        JsonElement id,
        KernelTurnStartRequest request,
        CancellationToken cancellationToken)
        => await composition.TurnLaunchRuntime.HandleTurnStartAsync(id, request, cancellationToken).ConfigureAwait(false);

    public async Task HandleTurnInterruptAsync(
        JsonElement id,
        string threadId,
        string turnId,
        CancellationToken cancellationToken)
        => await composition.TurnInterruptRuntime.HandleTurnInterruptAsync(id, threadId, turnId, cancellationToken).ConfigureAwait(false);

    public async Task<string> StartBackgroundTurnAsync(
        KernelThreadRecord record,
        KernelRuntimeThread runtimeThread,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        CancellationToken cancellationToken)
        => await composition.TurnLaunchRuntime
            .StartBackgroundTurnAsync(
                record,
                runtimeThread,
                userText,
                turnContext,
                persistExtendedHistory,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task RunTurnAsync(
        string threadId,
        string turnId,
        string userText,
        TurnRequestContext turnContext,
        bool persistExtendedHistory,
        CancellationTokenSource cts)
        => await composition.TurnRunnerRuntime
            .RunAsync(
                threadId,
                turnId,
                userText,
                turnContext,
                persistExtendedHistory,
                cts.Token)
            .ConfigureAwait(false);

    public async Task TryCommitTerminalTurnProjectionAsync(
        string threadId,
        string turnId,
        TurnRequestContext turnContext,
        string? reviewOutputText,
        string? reviewFailureMessage,
        string? effectiveUserText,
        string? finalAssistantText,
        string finalTurnStatus,
        KernelTurnErrorRecord? finalTurnError,
        DateTimeOffset? turnStartedAt,
        DateTimeOffset? turnCompletedAt)
        => await composition.TerminalTurnProjectionCommitRuntime.TryCommitTerminalTurnProjectionAsync(
            threadId,
            turnId,
            turnContext,
            reviewOutputText,
            reviewFailureMessage,
            effectiveUserText,
            finalAssistantText,
            finalTurnStatus,
            finalTurnError,
            turnCompletedAt).ConfigureAwait(false);
}
