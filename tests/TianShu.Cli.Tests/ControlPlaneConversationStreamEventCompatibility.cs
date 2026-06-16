using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime.Events;

namespace TianShu.Cli.Tests;

internal static class ControlPlaneConversationStreamEventCompatibility
{
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web);

    internal static ControlPlaneConversationStreamEvent ToControlPlaneConversationStreamEvent(AgentStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);

        return new ControlPlaneConversationStreamEvent
        {
            Kind = (ControlPlaneConversationStreamEventKind)streamEvent.Kind,
            Timestamp = streamEvent.Timestamp,
            ThreadId = string.IsNullOrWhiteSpace(streamEvent.ThreadId) ? default(ThreadId?) : new ThreadId(streamEvent.ThreadId),
            TurnId = string.IsNullOrWhiteSpace(streamEvent.TurnId) ? default(TurnId?) : new TurnId(streamEvent.TurnId),
            ItemId = streamEvent.ItemId,
            CallId = string.IsNullOrWhiteSpace(streamEvent.CallId) ? default(CallId?) : new CallId(streamEvent.CallId),
            ToolName = streamEvent.ToolName,
            ServerName = streamEvent.ServerName,
            Text = streamEvent.Text,
            Status = streamEvent.Status,
            Phase = streamEvent.Phase,
            Message = streamEvent.Message,
            WillRetry = streamEvent.WillRetry,
            RequiresApproval = streamEvent.RequiresApproval,
            ApprovalKind = streamEvent.ApprovalKind,
            AvailableDecisions = streamEvent.AvailableDecisions?.ToArray(),
            AvailableDecisionOptions = streamEvent.AvailableDecisionOptions?.Select(ToControlPlaneApprovalDecisionOption).ToArray(),
            SourceMethod = streamEvent.SourceMethod,
            TaskType = streamEvent.TaskType,
            OperationName = streamEvent.OperationName,
            Source = streamEvent.Source,
            PayloadKind = ResolvePayloadKind(streamEvent),
            Payload = ResolvePayload(streamEvent),
            TurnError = streamEvent.TurnError,
            Diagnostics = ToDiagnostics(streamEvent.DataJson, streamEvent.MetadataJson, streamEvent.RawJson),
        };
    }

    private static ControlPlaneApprovalDecisionOption ToControlPlaneApprovalDecisionOption(ApprovalDecisionOptionPayload payload)
        => new(
            payload.Type,
            payload.ExecPolicyAmendment is null ? null : new ControlPlaneExecPolicyAmendment(payload.ExecPolicyAmendment.CommandPrefix),
            payload.NetworkPolicyAmendment is null ? null : new ControlPlaneNetworkPolicyAmendment(
                payload.NetworkPolicyAmendment.Host,
                payload.NetworkPolicyAmendment.Action));

    private static ControlPlaneConversationStreamDiagnostics? ToDiagnostics(string? dataJson, string? metadataJson, string? rawJson)
        => string.IsNullOrWhiteSpace(dataJson)
           && string.IsNullOrWhiteSpace(metadataJson)
           && string.IsNullOrWhiteSpace(rawJson)
            ? null
            : new ControlPlaneConversationStreamDiagnostics
            {
                DataJson = dataJson,
                MetadataJson = metadataJson,
                RawJson = rawJson,
            };

    private static ControlPlaneConversationStreamPayloadKind? ResolvePayloadKind(AgentStreamEvent streamEvent)
        => streamEvent.Kind switch
        {
            AgentStreamEventKind.PlanUpdated when streamEvent.Plan is not null => ControlPlaneConversationStreamPayloadKind.Plan,
            AgentStreamEventKind.ToolCallStarted or AgentStreamEventKind.ToolCallOutputDelta or AgentStreamEventKind.ToolCallCompleted
                when streamEvent.ToolCall is not null => ControlPlaneConversationStreamPayloadKind.ToolCall,
            AgentStreamEventKind.ApprovalRequested when streamEvent.ApprovalRequest is not null => ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            AgentStreamEventKind.PermissionRequested when streamEvent.PermissionRequest is not null => ControlPlaneConversationStreamPayloadKind.PermissionRequest,
            AgentStreamEventKind.UserInputRequested when streamEvent.UserInputRequest is not null => ControlPlaneConversationStreamPayloadKind.UserInputRequest,
            AgentStreamEventKind.ServerRequestResolved when streamEvent.ServerRequestResolved is not null => ControlPlaneConversationStreamPayloadKind.ServerRequestResolved,
            AgentStreamEventKind.TaskStarted or AgentStreamEventKind.TaskCompleted when streamEvent.Task is not null => ControlPlaneConversationStreamPayloadKind.Task,
            AgentStreamEventKind.OperationReported when streamEvent.Operation is not null => ControlPlaneConversationStreamPayloadKind.Operation,
            AgentStreamEventKind.HookStarted or AgentStreamEventKind.HookCompleted when streamEvent.HookRun is not null => ControlPlaneConversationStreamPayloadKind.HookRun,
            AgentStreamEventKind.ReasoningDelta when streamEvent.Reasoning is not null => ControlPlaneConversationStreamPayloadKind.Reasoning,
            AgentStreamEventKind.ModelRerouted when streamEvent.ModelRerouted is not null => ControlPlaneConversationStreamPayloadKind.ModelRerouted,
            AgentStreamEventKind.McpServerStatusUpdated when streamEvent.McpServerStatus is not null => ControlPlaneConversationStreamPayloadKind.McpServerStatus,
            AgentStreamEventKind.ItemStarted or AgentStreamEventKind.ItemCompleted when streamEvent.Item is not null => ControlPlaneConversationStreamPayloadKind.Item,
            AgentStreamEventKind.UserMessageCommitted when streamEvent.CommittedUserMessage is not null => ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
            AgentStreamEventKind.PendingFollowUpUpdated when streamEvent.PendingInputState is not null => ControlPlaneConversationStreamPayloadKind.PendingInputState,
            AgentStreamEventKind.PendingFollowUpUpdated when streamEvent.PendingFollowUp is not null => ControlPlaneConversationStreamPayloadKind.PendingFollowUp,
            AgentStreamEventKind.AgentJobProgress when streamEvent.AgentJobProgress is not null => ControlPlaneConversationStreamPayloadKind.AgentJobProgress,
            AgentStreamEventKind.DeprecationNotice when streamEvent.DeprecationNotice is not null => ControlPlaneConversationStreamPayloadKind.DeprecationNotice,
            AgentStreamEventKind.ConfigWarning when streamEvent.ConfigWarning is not null => ControlPlaneConversationStreamPayloadKind.ConfigWarning,
            AgentStreamEventKind.ThreadStatusChanged when streamEvent.ThreadStatusChanged is not null => ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged,
            AgentStreamEventKind.ThreadNameUpdated when streamEvent.ThreadNameUpdated is not null => ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated,
            AgentStreamEventKind.ThreadTokenUsageUpdated when streamEvent.ThreadTokenUsage is not null => ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage,
            AgentStreamEventKind.CommandExecOutputDelta when streamEvent.CommandExecOutputDelta is not null => ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta,
            AgentStreamEventKind.AppListUpdated when streamEvent.AppListUpdated is not null => ControlPlaneConversationStreamPayloadKind.AppListUpdated,
            AgentStreamEventKind.ThreadRealtimeItemAdded when streamEvent.ThreadRealtimeItemAdded is not null => ControlPlaneConversationStreamPayloadKind.ThreadRealtimeItemAdded,
            AgentStreamEventKind.ThreadRealtimeOutputAudioDelta when streamEvent.ThreadRealtimeOutputAudioDelta is not null => ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta,
            AgentStreamEventKind.ThreadRealtimeError when streamEvent.ThreadRealtimeError is not null => ControlPlaneConversationStreamPayloadKind.ThreadRealtimeError,
            AgentStreamEventKind.ThreadRealtimeClosed when streamEvent.ThreadRealtimeClosed is not null => ControlPlaneConversationStreamPayloadKind.ThreadRealtimeClosed,
            AgentStreamEventKind.Info when streamEvent.WindowsSandboxSetup is not null => ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup,
            AgentStreamEventKind.Info when streamEvent.McpServerOauthLogin is not null => ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin,
            AgentStreamEventKind.Info when streamEvent.RealtimeSession is not null => ControlPlaneConversationStreamPayloadKind.RealtimeSession,
            AgentStreamEventKind.Info when streamEvent.FuzzyFileSearchSession is not null => ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession,
            _ => null,
        };

    private static StructuredValue? ResolvePayload(AgentStreamEvent streamEvent)
        => ResolvePayloadKind(streamEvent) switch
        {
            ControlPlaneConversationStreamPayloadKind.Plan => ToStructuredValue(streamEvent.Plan),
            ControlPlaneConversationStreamPayloadKind.ToolCall => ToStructuredValue(streamEvent.ToolCall),
            ControlPlaneConversationStreamPayloadKind.ApprovalRequest => ToStructuredValue(streamEvent.ApprovalRequest),
            ControlPlaneConversationStreamPayloadKind.PermissionRequest => ToStructuredValue(streamEvent.PermissionRequest),
            ControlPlaneConversationStreamPayloadKind.UserInputRequest => ToStructuredValue(streamEvent.UserInputRequest),
            ControlPlaneConversationStreamPayloadKind.ServerRequestResolved => ToStructuredValue(streamEvent.ServerRequestResolved),
            ControlPlaneConversationStreamPayloadKind.Task => ToStructuredValue(streamEvent.Task),
            ControlPlaneConversationStreamPayloadKind.Operation => ToStructuredValue(streamEvent.Operation),
            ControlPlaneConversationStreamPayloadKind.HookRun => ToStructuredValue(streamEvent.HookRun),
            ControlPlaneConversationStreamPayloadKind.Reasoning => ToStructuredValue(streamEvent.Reasoning),
            ControlPlaneConversationStreamPayloadKind.ModelRerouted => ToStructuredValue(streamEvent.ModelRerouted),
            ControlPlaneConversationStreamPayloadKind.McpServerStatus => ToStructuredValue(streamEvent.McpServerStatus),
            ControlPlaneConversationStreamPayloadKind.Item => ToStructuredValue(streamEvent.Item),
            ControlPlaneConversationStreamPayloadKind.CommittedUserMessage => ToStructuredValue(streamEvent.CommittedUserMessage),
            ControlPlaneConversationStreamPayloadKind.PendingFollowUp => ToStructuredValue(streamEvent.PendingFollowUp),
            ControlPlaneConversationStreamPayloadKind.PendingInputState => ToStructuredValue(streamEvent.PendingInputState),
            ControlPlaneConversationStreamPayloadKind.AgentJobProgress => ToStructuredValue(streamEvent.AgentJobProgress),
            ControlPlaneConversationStreamPayloadKind.DeprecationNotice => ToStructuredValue(streamEvent.DeprecationNotice),
            ControlPlaneConversationStreamPayloadKind.ConfigWarning => ToStructuredValue(streamEvent.ConfigWarning),
            ControlPlaneConversationStreamPayloadKind.ThreadStatusChanged => ToStructuredValue(streamEvent.ThreadStatusChanged),
            ControlPlaneConversationStreamPayloadKind.ThreadNameUpdated => ToStructuredValue(streamEvent.ThreadNameUpdated),
            ControlPlaneConversationStreamPayloadKind.ThreadTokenUsage => ToStructuredValue(streamEvent.ThreadTokenUsage),
            ControlPlaneConversationStreamPayloadKind.CommandExecOutputDelta => ToStructuredValue(streamEvent.CommandExecOutputDelta),
            ControlPlaneConversationStreamPayloadKind.AppListUpdated => ToStructuredValue(streamEvent.AppListUpdated),
            ControlPlaneConversationStreamPayloadKind.WindowsSandboxSetup => ToStructuredValue(streamEvent.WindowsSandboxSetup),
            ControlPlaneConversationStreamPayloadKind.McpServerOauthLogin => ToStructuredValue(streamEvent.McpServerOauthLogin),
            ControlPlaneConversationStreamPayloadKind.RealtimeSession => ToStructuredValue(streamEvent.RealtimeSession),
            ControlPlaneConversationStreamPayloadKind.FuzzyFileSearchSession => ToStructuredValue(streamEvent.FuzzyFileSearchSession),
            ControlPlaneConversationStreamPayloadKind.ThreadRealtimeItemAdded => ToStructuredValue(streamEvent.ThreadRealtimeItemAdded),
            ControlPlaneConversationStreamPayloadKind.ThreadRealtimeOutputAudioDelta => ToStructuredValue(streamEvent.ThreadRealtimeOutputAudioDelta),
            ControlPlaneConversationStreamPayloadKind.ThreadRealtimeError => ToStructuredValue(streamEvent.ThreadRealtimeError),
            ControlPlaneConversationStreamPayloadKind.ThreadRealtimeClosed => ToStructuredValue(streamEvent.ThreadRealtimeClosed),
            _ => null,
        };

    private static StructuredValue? ToStructuredValue(object? value)
        => value is null
            ? null
            : StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(value, StructuredJsonOptions));
}
