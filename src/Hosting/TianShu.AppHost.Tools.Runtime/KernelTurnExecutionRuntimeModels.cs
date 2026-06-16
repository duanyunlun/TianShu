using System.Text;
using System.Text.Json;
using System.Net.WebSockets;
using TianShu.AppHost.Configuration;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Interactions;
using TianShu.Execution.Runtime;
using TianShuPromptConfiguration = TianShu.Configuration.TianShuPromptConfiguration;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record TurnRequestContext(
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
    bool IsReview = false,
    string? ReviewDisplayText = null,
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools = null,
    string? BaseInstructions = null,
    string? DeveloperInstructions = null,
    string? UserInstructions = null,
    string? ReasoningSummary = null,
    string? Verbosity = null,
    KernelJsonSchemaPayload? OutputSchema = null,
    IReadOnlyList<KernelTurnInputItem>? InputItems = null,
    string? ExplicitPluginInstructions = null,
    List<string>? ExplicitSkillInjections = null,
    Dictionary<string, string>? DependencyEnvironment = null,
    KernelCollaborationModeState? CollaborationMode = null,
    TianShuPromptConfiguration? PromptConfiguration = null,
    bool DefaultModeRequestUserInputEnabled = false,
    KernelSessionSource? SessionSource = null,
    string? EnvironmentContextSubagents = null,
    string? RealtimeDeveloperInstructions = null,
    bool EnableAgentJobWorkerTools = false,
    KernelWindowsSandboxLevel WindowsSandboxLevel = KernelWindowsSandboxLevel.Disabled,
    InteractionEnvelopeRef? InteractionEnvelope = null,
    string? ModelRouteSetId = null,
    string? ModelRouteKind = null,
    string? ModelRouteDiagnosticsCorrelationId = null,
    string? StageId = null,
    string? StageDecisionId = null,
    string? ContextPackageId = null,
    string? ExecutionRequestId = null,
    string? DispatchBinding = null,
    string? DispatchImplementationId = null,
    string? DispatchKind = null,
    DateTimeOffset? StageStartedAt = null,
    TurnExecutionDispatchContext? ExecutionDispatchContext = null);

internal static class TurnRequestContextExecutionDispatchProjection
{
    public static TurnRequestContext Project(
        TurnRequestContext routedContext,
        TurnExecutionDispatchContext dispatchContext)
    {
        ArgumentNullException.ThrowIfNull(routedContext);
        ArgumentNullException.ThrowIfNull(dispatchContext);

        return routedContext with
        {
            StageId = dispatchContext.StageId,
            StageDecisionId = dispatchContext.DecisionId,
            ContextPackageId = dispatchContext.ContextPackageId,
            ExecutionRequestId = dispatchContext.ExecutionId,
            DispatchBinding = dispatchContext.Binding,
            DispatchImplementationId = dispatchContext.ImplementationId,
            DispatchKind = dispatchContext.DispatchKind,
            StageStartedAt = dispatchContext.StartedAt,
            ExecutionDispatchContext = dispatchContext,
        };
    }
}

internal enum TurnOperationKind
{
    ResolveInput,
    ResolveDependencies,
    ExecuteAssistant,
    StreamAssistantOutput,
}

internal sealed class TurnOperationState
{
    public TurnOperationState(
        string threadId,
        string turnId,
        string itemId,
        string reasoningItemId,
        string userText)
    {
        ThreadId = threadId;
        TurnId = turnId;
        ItemId = itemId;
        ReasoningItemId = reasoningItemId;
        OriginalUserText = userText;
        EffectiveUserText = userText;
        ToolCallGate = new KernelReadinessFlag();
    }

    public string ThreadId { get; }

    public string TurnId { get; }

    public string ItemId { get; }

    public string ReasoningItemId { get; }

    public KernelReadinessFlag ToolCallGate { get; }

    public string OriginalUserText { get; set; }

    public string EffectiveUserText { get; set; }

    public string? StickyTurnState { get; set; }

    public string AssistantText { get; set; } = string.Empty;

    public StringBuilder ProviderReasoningContent { get; } = new();

    public bool AssistantTextStreamed { get; set; }

    public bool IsPlanMode { get; set; }

    public bool AgentMessageStarted { get; set; }

    public bool PlanItemStarted { get; set; }

    public bool PlanItemCompleted { get; set; }

    public string PlanText { get; set; } = string.Empty;

    public KernelProposedPlanStreamParser? ProposedPlanParser { get; set; }

    public string PlanItemId => $"{TurnId}-plan";

    public string PendingLeadingAgentWhitespace { get; set; } = string.Empty;
}

internal sealed record KernelTurnTerminalStateCommit(
    string ThreadId,
    string TurnId,
    TurnRequestContext TurnContext,
    string ReviewExitItemId,
    string ReviewOutputText,
    string ReviewFailureMessage,
    string EffectiveUserText,
    string? FinalAssistantText,
    string FinalTurnStatus,
    KernelTurnErrorRecord? FinalTurnError,
    bool PersistExtendedHistory);

internal sealed record ModelFunctionCall(
    string Name,
    string CallId,
    string? Arguments,
    string? Input,
    bool IsCustom,
    string? Namespace = null,
    bool IsToolSearch = false);

internal sealed record ResponsesStreamResult(
    string ResponseId,
    List<JsonElement> OutputItemsAdded,
    List<JsonElement> OutputItemsDone,
    string OutputTextDeltas);

internal sealed class KernelResponsesStreamException : Exception
{
    public KernelResponsesStreamException(string message, bool isRetryable)
        : base(message)
    {
        IsRetryable = isRetryable;
    }

    public bool IsRetryable { get; }
}

internal sealed record ResponsesTransportSettings(
    int RequestMaxRetries,
    int StreamMaxRetries,
    TimeSpan StreamIdleTimeout,
    TimeSpan WebsocketConnectTimeout,
    bool SupportsWebsockets);

internal sealed class ResponsesWebSocketTurnSession : IAsyncDisposable
{
    public ClientWebSocket? Socket { get; private set; }

    public IReadOnlyList<JsonElement>? LastRequestInput { get; set; }

    public string? LastRequestSignature { get; set; }

    public IReadOnlyList<JsonElement> LastResponseItems { get; set; } = Array.Empty<JsonElement>();

    public string? LastResponseId { get; set; }

    public bool IsConnected => Socket is { State: WebSocketState.Open };

    public void Attach(ClientWebSocket socket)
    {
        Socket = socket;
    }

    public async ValueTask ResetConnectionAsync()
    {
        if (Socket is null)
        {
            return;
        }

        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "reset", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // ignore transport shutdown failures during retry/reset
        }
        finally
        {
            Socket.Dispose();
            Socket = null;
            LastRequestInput = null;
            LastRequestSignature = null;
            LastResponseItems = Array.Empty<JsonElement>();
            LastResponseId = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetConnectionAsync().ConfigureAwait(false);
    }
}
