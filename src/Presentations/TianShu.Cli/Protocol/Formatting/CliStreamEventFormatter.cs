using System.Linq;
using System.Text;
using TianShu.Contracts.Conversations;
using TianShu.Execution.Runtime.Events;

namespace TianShu.Cli;

internal static class CliStreamEventFormatter
{
    public static string Format(ProbeEventRecord record)
        => FormatCore(
            record.Kind,
            record.ToolName,
            record.CallId,
            record.Message,
            record.Text,
            record.Status,
            null,
            record.Operation?.OperationName,
            record.Task?.TaskType,
            record.Reasoning,
            record.Plan,
            record.ToolCall,
            record.ApprovalRequest,
            record.PermissionRequest,
            record.UserInputRequest,
            record.McpServerStatus,
            record.CommittedUserMessage,
            record.PendingFollowUp,
            record.PendingInputState,
            record.AgentJobProgress);

    private static string FormatCore(
        string kind,
        string? toolName,
        string? callId,
        string? message,
        string? text,
        string? status,
        string? source,
        string? operationName,
        string? taskType,
        ReasoningEventPayload? reasoning,
        PlanEventPayload? plan,
        ToolCallEventPayload? toolCall,
        CliApprovalRequestPayload? approvalRequest,
        CliPermissionRequestPayload? permissionRequest,
        CliUserInputRequestPayload? userInputRequest,
        McpServerStatusPayload? mcpServerStatus,
        CommittedUserMessagePayload? committedUserMessage,
        CliPendingFollowUpPayload? pendingFollowUp,
        CliPendingInputStatePayload? pendingInputState,
        CliJobProgressPayload? agentJobProgress)
    {
        return kind switch
        {
            nameof(ControlPlaneConversationStreamEventKind.ToolCallStarted) => $"ToolCallStarted: tool={toolCall?.ToolName ?? toolName ?? "<unknown>"}, callId={toolCall?.CallId ?? callId ?? "<none>"}, input={ResolveToolInput(toolCall, text, message)}",
            nameof(ControlPlaneConversationStreamEventKind.ToolCallCompleted) => BuildToolCompletionText(toolCall, toolName, callId, status, text, message),
            nameof(ControlPlaneConversationStreamEventKind.ToolCallOutputDelta) => $"ToolCallOutputDelta: tool={toolCall?.ToolName ?? toolName ?? "<unknown>"}, callId={toolCall?.CallId ?? callId ?? "<none>"}, text={Normalize(toolCall?.OutputText) ?? Normalize(text) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.ItemStarted) => BuildItemText("ItemStarted", recordStatus: status, text: text, message: message),
            nameof(ControlPlaneConversationStreamEventKind.ItemCompleted) => BuildItemText("ItemCompleted", recordStatus: status, text: text, message: message),
            nameof(ControlPlaneConversationStreamEventKind.UserMessageCommitted) => BuildCommittedUserMessageText(text, message, committedUserMessage),
            nameof(ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated) => BuildPendingFollowUpText(text, message, status, pendingFollowUp, pendingInputState),
            nameof(ControlPlaneConversationStreamEventKind.PlanUpdated) => $"PlanUpdated: {BuildPlanText(plan, text, message)}",
            nameof(ControlPlaneConversationStreamEventKind.OperationReported) => $"OperationReported: {Normalize(operationName) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.TaskStarted) => $"TaskStarted: {Normalize(taskType) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.TaskCompleted) => $"TaskCompleted: {Normalize(taskType) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.ReasoningDelta) => $"ReasoningDelta: {Normalize(reasoning?.Text) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.ApprovalRequested) => $"ApprovalRequested: tool={approvalRequest?.ToolName ?? toolName ?? "<unknown>"}, callId={callId ?? "<none>"}, summary={Normalize(approvalRequest?.Summary) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.PermissionRequested) => $"PermissionRequested: callId={callId ?? "<none>"}, summary={Normalize(permissionRequest?.Summary) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.UserInputRequested) => $"UserInputRequested: callId={callId ?? "<none>"}, summary={Normalize(userInputRequest?.Summary) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            nameof(ControlPlaneConversationStreamEventKind.McpServerStatusUpdated) => $"McpServerStatusUpdated: {BuildMcpServerStatusText(mcpServerStatus, text, message)}",
            nameof(ControlPlaneConversationStreamEventKind.AgentJobProgress) => BuildAgentJobProgressText(agentJobProgress, text, message),
            nameof(ControlPlaneConversationStreamEventKind.TurnSteered) => $"TurnSteered: {Normalize(source) ?? Normalize(text) ?? Normalize(message) ?? string.Empty}",
            _ => $"{kind}: {Normalize(text) ?? Normalize(message) ?? Normalize(status) ?? string.Empty}",
        };
    }

    private static string BuildItemText(string prefix, string? recordStatus, string? text, string? message)
    {
        var parts = new List<string>();
        var normalizedText = Normalize(text);
        var normalizedMessage = Normalize(message);
        var normalizedStatus = Normalize(recordStatus);

        if (!string.IsNullOrWhiteSpace(normalizedStatus))
        {
            parts.Add($"status={normalizedStatus}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedText))
        {
            parts.Add($"text={normalizedText}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedMessage) && !string.Equals(normalizedMessage, normalizedText, StringComparison.Ordinal))
        {
            parts.Add($"message={normalizedMessage}");
        }

        return parts.Count == 0
            ? prefix
            : $"{prefix}: {string.Join(", ", parts)}";
    }

    private static string BuildCommittedUserMessageText(string? text, string? message, CommittedUserMessagePayload? payload)
    {
        var parts = new List<string>();
        var committedText = Normalize(payload?.Text) ?? Normalize(text) ?? Normalize(message);
        if (!string.IsNullOrWhiteSpace(committedText))
        {
            parts.Add($"text={committedText}");
        }

        if (payload is not null)
        {
            if (payload.ImageCount > 0)
            {
                parts.Add($"imageCount={payload.ImageCount}");
            }

            var correlationId = Normalize(payload.CorrelationId);
            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                parts.Add($"correlationId={correlationId}");
            }
        }

        return parts.Count == 0
            ? "UserMessageCommitted"
            : $"UserMessageCommitted: {string.Join(", ", parts)}";
    }

    private static string BuildPendingFollowUpText(
        string? text,
        string? message,
        string? status,
        CliPendingFollowUpPayload? payload,
        CliPendingInputStatePayload? pendingInputState)
    {
        if (payload is null)
        {
            return $"PendingFollowUpUpdated: {Normalize(text) ?? Normalize(message) ?? Normalize(status) ?? string.Empty}".TrimEnd();
        }

        var parts = new List<string>
        {
            $"correlationId={payload.CorrelationId}",
            $"requested={payload.RequestedMode}",
            $"effective={payload.EffectiveMode}",
            $"state={payload.LifecycleState}",
        };

        var expectedTurnId = Normalize(payload.ExpectedTurnId);
        if (!string.IsNullOrWhiteSpace(expectedTurnId))
        {
            parts.Add($"expectedTurnId={expectedTurnId}");
        }

        var turnId = Normalize(payload.TurnId);
        if (!string.IsNullOrWhiteSpace(turnId))
        {
            parts.Add($"turnId={turnId}");
        }

        var compareMessage = Normalize(payload.CompareKey?.Message);
        if (!string.IsNullOrWhiteSpace(compareMessage))
        {
            parts.Add($"compareMessage={compareMessage}");
        }

        if (payload.CompareKey is not null && payload.CompareKey.ImageCount > 0)
        {
            parts.Add($"imageCount={payload.CompareKey.ImageCount}");
        }

        if (pendingInputState is not null)
        {
            parts.Add($"supplementalEntries={pendingInputState.Entries.Count}");
            parts.Add($"queuedUserMessages={(pendingInputState.QueuedUserMessages?.Count ?? 0)}");
            parts.Add($"pendingSteers={(pendingInputState.PendingSteers?.Count ?? 0)}");
            if (pendingInputState.InterruptRequestPending)
            {
                parts.Add("interruptPending=true");
            }
        }

        return $"PendingFollowUpUpdated: {string.Join(", ", parts)}";
    }

    private static string BuildAgentJobProgressText(CliJobProgressPayload? payload, string? text, string? message)
    {
        if (payload is null)
        {
            return $"AgentJobProgress: {Normalize(text) ?? Normalize(message) ?? string.Empty}".TrimEnd();
        }

        var parts = new List<string>
        {
            $"jobId={payload.JobId}",
            $"completed={payload.CompletedItems}/{payload.TotalItems}",
            $"pending={payload.PendingItems}",
            $"running={payload.RunningItems}",
            $"failed={payload.FailedItems}",
        };

        if (payload.EtaSeconds.HasValue)
        {
            parts.Add($"etaSeconds={payload.EtaSeconds.Value}");
        }

        return $"AgentJobProgress: {string.Join(", ", parts)}";
    }

    private static string BuildToolCompletionText(
        ToolCallEventPayload? toolCall,
        string? toolName,
        string? callId,
        string? status,
        string? text,
        string? message)
    {
        var builder = new StringBuilder();
        builder.Append("ToolCallCompleted: tool=")
            .Append(toolCall?.ToolName ?? toolName ?? "<unknown>")
            .Append(", callId=")
            .Append(toolCall?.CallId ?? callId ?? "<none>")
            .Append(", status=")
            .Append(toolCall?.Status ?? status ?? "completed");

        var output = Normalize(toolCall?.OutputText) ?? Normalize(text) ?? Normalize(message);
        if (!string.IsNullOrWhiteSpace(output))
        {
            builder.Append(", output=").Append(output);
        }

        return builder.ToString();
    }

    private static string BuildPlanText(PlanEventPayload? plan, string? text, string? message)
    {
        if (plan is null)
        {
            return Normalize(text) ?? Normalize(message) ?? string.Empty;
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(plan.Explanation))
        {
            builder.Append(plan.Explanation);
        }

        if (plan.Steps.Count > 0)
        {
            var stepsText = string.Join(" | ", plan.Steps.Select(static step => $"{step.Sequence}.{step.Step}[{step.Status}]"));
            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(stepsText);
        }

        return builder.Length == 0
            ? Normalize(text) ?? Normalize(message) ?? string.Empty
            : builder.ToString();
    }

    private static string BuildMcpServerStatusText(McpServerStatusPayload? payload, string? text, string? message)
    {
        if (payload is null)
        {
            return Normalize(text) ?? Normalize(message) ?? string.Empty;
        }

        var builder = new StringBuilder();
        if (payload.Count is int count)
        {
            builder.Append("count=").Append(count);
        }

        if (payload.Servers.Count > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append("servers=")
                .Append(string.Join(
                    " | ",
                    payload.Servers.Select(static server =>
                    {
                        var authStatus = Normalize(server.AuthStatus);
                        return string.IsNullOrWhiteSpace(authStatus)
                            ? $"{server.Name}(tools={server.ToolCount}, resources={server.ResourceCount}, templates={server.ResourceTemplateCount})"
                            : $"{server.Name}(auth={authStatus}, tools={server.ToolCount}, resources={server.ResourceCount}, templates={server.ResourceTemplateCount})";
                    })));
        }

        return builder.Length == 0
            ? Normalize(text) ?? Normalize(message) ?? string.Empty
            : builder.ToString();
    }

    private static string ResolveToolInput(ToolCallEventPayload? toolCall, string? text, string? message)
        => Normalize(toolCall?.InputText) ?? Normalize(text) ?? Normalize(message) ?? string.Empty;

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
