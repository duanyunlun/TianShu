using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Turn provider assistant 运行时，负责 provider-backed assistant tool loop 的编排。
/// Runtime that orchestrates the provider-backed assistant tool loop for a turn.
/// </summary>
internal sealed class KernelTurnProviderAssistantRuntime
{
    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<string?, string?> normalize;
    private readonly string defaultModel;
    private readonly Func<string, string, TurnRequestContext, CancellationToken, Task> maybeRunPreSamplingAutoCompactAsync;
    private readonly Func<string?, string> buildRealtimeStartDeveloperInstruction;
    private readonly Func<string?, ContextBudgetProfile> resolveContextBudgetProfile;
    private readonly Func<string, TurnRequestContext, CancellationToken, Task<IReadOnlyList<ContextSegment>>> resolveContextOverlaySegmentsAsync;
    private readonly Func<TurnOperationState, IEnumerable<JsonElement>, IEnumerable<JsonElement>, CancellationToken, Task> emitWebSearchOutputItemNotificationsAsync;
    private readonly Func<TurnOperationState, IEnumerable<JsonElement>, string?, CancellationToken, Task> emitImageGenerationOutputItemNotificationsAsync;
    private readonly KernelContextSlicingDiagnosticsRuntime contextSlicingDiagnosticsRuntime;
    private readonly KernelResponsesAssistantCompletionRuntime responsesAssistantCompletionRuntime;
    private readonly KernelResponsesRequestCompositionRuntime responsesRequestCompositionRuntime;
    private readonly KernelResponsesToolContinuationRuntime responsesToolContinuationRuntime;
    private readonly KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime;
    private readonly KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime;
    private readonly KernelResponsesWebSocketStreamTransportRuntime responsesWebSocketStreamTransportRuntime;

    public KernelTurnProviderAssistantRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<string?, string?> normalize,
        string defaultModel,
        Func<string, string, TurnRequestContext, CancellationToken, Task> maybeRunPreSamplingAutoCompactAsync,
        Func<string?, string> buildRealtimeStartDeveloperInstruction,
        Func<string?, ContextBudgetProfile> resolveContextBudgetProfile,
        Func<string, TurnRequestContext, CancellationToken, Task<IReadOnlyList<ContextSegment>>> resolveContextOverlaySegmentsAsync,
        Func<TurnOperationState, IEnumerable<JsonElement>, IEnumerable<JsonElement>, CancellationToken, Task> emitWebSearchOutputItemNotificationsAsync,
        Func<TurnOperationState, IEnumerable<JsonElement>, string?, CancellationToken, Task> emitImageGenerationOutputItemNotificationsAsync,
        KernelContextSlicingDiagnosticsRuntime contextSlicingDiagnosticsRuntime,
        KernelResponsesAssistantCompletionRuntime responsesAssistantCompletionRuntime,
        KernelResponsesRequestCompositionRuntime responsesRequestCompositionRuntime,
        KernelResponsesToolContinuationRuntime responsesToolContinuationRuntime,
        KernelResponsesFollowUpInputRuntime responsesFollowUpInputRuntime,
        KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime,
        KernelResponsesWebSocketStreamTransportRuntime responsesWebSocketStreamTransportRuntime)
    {
        this.threadStore = threadStore ?? throw new ArgumentNullException(nameof(threadStore));
        this.threadManager = threadManager ?? throw new ArgumentNullException(nameof(threadManager));
        this.normalize = normalize ?? throw new ArgumentNullException(nameof(normalize));
        this.defaultModel = defaultModel ?? throw new ArgumentNullException(nameof(defaultModel));
        this.maybeRunPreSamplingAutoCompactAsync = maybeRunPreSamplingAutoCompactAsync ?? throw new ArgumentNullException(nameof(maybeRunPreSamplingAutoCompactAsync));
        this.buildRealtimeStartDeveloperInstruction = buildRealtimeStartDeveloperInstruction ?? throw new ArgumentNullException(nameof(buildRealtimeStartDeveloperInstruction));
        this.resolveContextBudgetProfile = resolveContextBudgetProfile ?? throw new ArgumentNullException(nameof(resolveContextBudgetProfile));
        this.resolveContextOverlaySegmentsAsync = resolveContextOverlaySegmentsAsync ?? throw new ArgumentNullException(nameof(resolveContextOverlaySegmentsAsync));
        this.emitWebSearchOutputItemNotificationsAsync = emitWebSearchOutputItemNotificationsAsync ?? throw new ArgumentNullException(nameof(emitWebSearchOutputItemNotificationsAsync));
        this.emitImageGenerationOutputItemNotificationsAsync = emitImageGenerationOutputItemNotificationsAsync ?? throw new ArgumentNullException(nameof(emitImageGenerationOutputItemNotificationsAsync));
        this.contextSlicingDiagnosticsRuntime = contextSlicingDiagnosticsRuntime ?? throw new ArgumentNullException(nameof(contextSlicingDiagnosticsRuntime));
        this.responsesAssistantCompletionRuntime = responsesAssistantCompletionRuntime ?? throw new ArgumentNullException(nameof(responsesAssistantCompletionRuntime));
        this.responsesRequestCompositionRuntime = responsesRequestCompositionRuntime ?? throw new ArgumentNullException(nameof(responsesRequestCompositionRuntime));
        this.responsesToolContinuationRuntime = responsesToolContinuationRuntime ?? throw new ArgumentNullException(nameof(responsesToolContinuationRuntime));
        this.responsesFollowUpInputRuntime = responsesFollowUpInputRuntime ?? throw new ArgumentNullException(nameof(responsesFollowUpInputRuntime));
        this.responsesHttpStreamTransportRuntime = responsesHttpStreamTransportRuntime ?? throw new ArgumentNullException(nameof(responsesHttpStreamTransportRuntime));
        this.responsesWebSocketStreamTransportRuntime = responsesWebSocketStreamTransportRuntime ?? throw new ArgumentNullException(nameof(responsesWebSocketStreamTransportRuntime));
    }

    public async Task<(string AssistantText, bool Streamed)> ExecuteAsync(
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        _ = ProviderWireApi.NormalizeOrThrow(context.ProviderWireApi, "turn context providerWireApi");
        await maybeRunPreSamplingAutoCompactAsync(
            state.ThreadId,
            state.EffectiveUserText,
            context,
            cancellationToken).ConfigureAwait(false);

        var assistantText = await StreamResponsesToolLoopAsync(state, context, cancellationToken).ConfigureAwait(false);
        return (assistantText, true);
    }

    public async Task<string> StreamResponsesToolLoopAsync(
        TurnOperationState state,
        TurnRequestContext context,
        CancellationToken cancellationToken)
    {
        var apiKeyEnv = Normalize(context.ProviderApiKeyEnvironmentVariable) ?? "OPENAI_API_KEY";
        var apiKey = Normalize(Environment.GetEnvironmentVariable(apiKeyEnv));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"缺少模型访问凭据：环境变量 {apiKeyEnv} 未设置。");
        }

        var model = Normalize(context.Model) ?? defaultModel;
        var baseUrl = Normalize(context.ProviderBaseUrl) ?? "https://api.openai.com/v1";
        var thread = await threadStore.GetThreadAsync(state.ThreadId, cancellationToken).ConfigureAwait(false);
        var developerMessage = ResolveTurnDeveloperMessage(context, includeBaseInstructions: false);
        var contextualUserMessages = ResolveContextualUserMessages(context);
        var budgetProfile = resolveContextBudgetProfile(context.Cwd);
        var overlaySegments = await resolveContextOverlaySegmentsAsync(state.ThreadId, context, cancellationToken).ConfigureAwait(false);
        var slicedRequestInput = BuildResponsesConversationInput(
            thread,
            Normalize(state.EffectiveUserText) ?? state.EffectiveUserText,
            developerMessage,
            contextualUserMessages,
            context.InputItems,
            includeProviderReplayArtifacts: string.Equals(
                context.ProviderWireApi,
                ProviderWireApi.OpenAiChatCompletions,
                StringComparison.Ordinal),
            budgetProfile,
            overlaySegments,
            state.TurnId,
            model,
            context.ModelProvider);
        var requestInput = slicedRequestInput.Input;
        var requestSlicingReport = slicedRequestInput.Report;
        var parallelExecutionLock = new KernelAsyncReadWriteLock();
        var loopContext = context;
        var requestSequence = 0;
        var emptyAssistantRepairAttempts = 0;
        _ = threadManager.TryGetThread(state.ThreadId, out var runtimeThread);
        await using var websocketTurnSession = new ResponsesWebSocketTurnSession();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestSequence++;

            loopContext = RefreshLoopTurnContext(state.ThreadId, loopContext);
            if (requestSlicingReport is not null)
            {
                await contextSlicingDiagnosticsRuntime.EmitReportAsync(
                    state,
                    loopContext,
                    requestSlicingReport,
                    requestSequence,
                    model,
                    cancellationToken).ConfigureAwait(false);
                requestSlicingReport = null;
            }

            var providerRequest = await responsesRequestCompositionRuntime.ComposeAsync(
                state,
                loopContext,
                requestInput,
                model,
                baseUrl,
                apiKey,
                cancellationToken).ConfigureAwait(false);
            await responsesRequestCompositionRuntime.PersistRequestLogAsync(
                state,
                requestSequence,
                providerRequest,
                cancellationToken).ConfigureAwait(false);

            ResponsesStreamResult streamResult;
            var useResponsesWebSocket = runtimeThread is not null
                                        && KernelResponsesWebSocketStreamTransportRuntime.CanUseTransport(
                                            runtimeThread,
                                            loopContext,
                                            providerRequest.TransportProtocolBinding);
            if (useResponsesWebSocket && requestSequence == 1)
            {
                await responsesWebSocketStreamTransportRuntime.PrewarmSessionAsync(
                    runtimeThread!,
                    providerRequest.BaseUrl,
                    providerRequest.ApiKey,
                    providerRequest.Model,
                    loopContext.ModelProvider,
                    requestSequence,
                    providerRequest.RequestComposition,
                    state,
                    providerRequest.TurnMetadataHeader,
                    providerRequest.TransportSettings,
                    providerRequest.TransportProtocolBinding,
                    providerRequest.TransportRetryStrategy,
                    websocketTurnSession,
                    cancellationToken).ConfigureAwait(false);
                useResponsesWebSocket = KernelResponsesWebSocketStreamTransportRuntime.CanUseTransport(
                    runtimeThread!,
                    loopContext,
                    providerRequest.TransportProtocolBinding);
            }

            if (useResponsesWebSocket)
            {
                streamResult = await responsesWebSocketStreamTransportRuntime.StreamWithFallbackAsync(
                    runtimeThread!,
                    providerRequest.BaseUrl,
                    providerRequest.ApiKey,
                    providerRequest.Model,
                    loopContext.ModelProvider,
                    requestSequence,
                    providerRequest.RequestComposition,
                    state,
                    providerRequest.TurnMetadataHeader,
                    providerRequest.TransportSettings,
                    providerRequest.TransportProtocolBinding,
                    providerRequest.TransportRetryStrategy,
                    websocketTurnSession,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                streamResult = await responsesHttpStreamTransportRuntime.StreamWithRetryAsync(
                    providerRequest.BaseUrl,
                    providerRequest.ApiKey,
                    providerRequest.Model,
                    loopContext.ModelProvider,
                    requestSequence,
                    providerRequest.RequestComposition,
                    state,
                    providerRequest.TurnMetadataHeader,
                    providerRequest.TransportSettings,
                    providerRequest.TransportProtocolBinding,
                    providerRequest.TransportRetryStrategy,
                    cancellationToken).ConfigureAwait(false);
            }

            await emitWebSearchOutputItemNotificationsAsync(
                state,
                streamResult.OutputItemsAdded,
                streamResult.OutputItemsDone,
                cancellationToken).ConfigureAwait(false);

            await emitImageGenerationOutputItemNotificationsAsync(
                state,
                streamResult.OutputItemsDone,
                context.Cwd,
                cancellationToken).ConfigureAwait(false);

            var responseItems = responsesToolContinuationRuntime.BuildFollowUpResponseItems(streamResult.OutputItemsAdded, streamResult.OutputItemsDone);
            var functionCalls = responsesToolContinuationRuntime.ExtractFunctionCalls(streamResult.OutputItemsDone);
            if (functionCalls.Count == 0)
            {
                var completionDecision = await responsesAssistantCompletionRuntime.EvaluateNoToolCallAsync(
                    state,
                    loopContext,
                    streamResult.OutputItemsDone,
                    streamResult.OutputTextDeltas,
                    requestInput,
                    requestSequence,
                    model,
                    emptyAssistantRepairAttempts,
                    cancellationToken).ConfigureAwait(false);
                emptyAssistantRepairAttempts = completionDecision.EmptyAssistantRepairAttempts;
                if (completionDecision.Kind == KernelResponsesAssistantCompletionDecisionKind.Repair)
                {
                    requestInput = completionDecision.RepairRequestInput?.ToList()
                                   ?? throw new InvalidOperationException("Responses assistant repair requested without repair input.");
                    continue;
                }

                return completionDecision.AssistantText;
            }

            var nextInput = await responsesToolContinuationRuntime.ExecuteFunctionCallsAsync(
                functionCalls,
                parallelExecutionLock,
                state,
                loopContext,
                cancellationToken).ConfigureAwait(false);

            var followUpInput = await responsesFollowUpInputRuntime.BuildAsync(
                state,
                requestInput,
                responseItems,
                nextInput,
                loopContext,
                budgetProfile,
                model,
                cancellationToken).ConfigureAwait(false);
            requestInput = followUpInput.Input;
            requestSlicingReport = followUpInput.Report;
        }
    }

    private string? Normalize(string? value)
        => normalize(value);

    private string? ResolveTurnDeveloperMessage(TurnRequestContext context, bool includeBaseInstructions)
        => KernelTurnExecutionRuntimeHelpers.ResolveTurnDeveloperMessage(context, includeBaseInstructions);

    private static IReadOnlyList<string>? ResolveContextualUserMessages(TurnRequestContext context)
        => KernelTurnExecutionRuntimeHelpers.ResolveContextualUserMessages(context);

    private static ContextSlicedResponsesConversationInput BuildResponsesConversationInput(
        KernelThreadRecord? thread,
        string userText,
        string? developerMessage,
        IReadOnlyList<string>? contextualUserMessages,
        IReadOnlyList<KernelTurnInputItem>? currentInputItems,
        bool includeProviderReplayArtifacts,
        ContextBudgetProfile budgetProfile,
        IReadOnlyList<ContextSegment>? overlaySegments,
        string turnId,
        string? modelId,
        string? providerId)
        => KernelTurnExecutionRuntimeHelpers.BuildSlicedResponsesConversationInput(
            thread,
            userText,
            developerMessage,
            contextualUserMessages,
            currentInputItems,
            includeProviderReplayArtifacts,
            budgetProfile,
            overlaySegments: overlaySegments,
            turnId: turnId,
            modelId: modelId,
            providerId: providerId);

    private TurnRequestContext RefreshLoopTurnContext(string threadId, TurnRequestContext current)
        => KernelTurnExecutionRuntimeHelpers.RefreshLoopTurnContext(
            threadId,
            current,
            threadManager,
            buildRealtimeStartDeveloperInstruction);
}
