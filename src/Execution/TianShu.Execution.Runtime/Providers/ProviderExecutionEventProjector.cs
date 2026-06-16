using System.Text.Json;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Execution.Runtime.Events;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime.Providers;

/// <summary>
/// 默认 Provider 执行事件投影器。
/// Default projector that maps provider execution events into runtime stream events.
/// </summary>
internal sealed class ProviderExecutionEventProjector : IProviderExecutionEventProjector
{
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public IReadOnlyList<ControlPlaneConversationStreamEvent> Project(ProviderEventProjectionContext context, ProviderStreamEvent providerEvent)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(providerEvent);

        return providerEvent switch
        {
            ProviderTextDeltaEvent textDelta => [BuildTextDeltaEvent(context, textDelta)],
            ProviderReasoningDeltaEvent reasoningDelta => [BuildReasoningDeltaEvent(context, reasoningDelta)],
            ProviderToolDirectiveEvent toolDirective => BuildToolDirectiveEvents(context, toolDirective),
            ProviderToolOutputDeltaEvent toolOutputDelta => [BuildToolOutputDeltaEvent(context, toolOutputDelta)],
            ProviderToolResultEvent toolResult => [BuildToolResultEvent(context, toolResult)],
            ProviderCompletionEvent completion => [BuildCompletionEvent(context, completion)],
            ProviderFailureEvent failure => [BuildFailureEvent(context, failure)],
            _ => Array.Empty<ControlPlaneConversationStreamEvent>(),
        };
    }

    private static ControlPlaneConversationStreamEvent BuildTextDeltaEvent(ProviderEventProjectionContext context, ProviderTextDeltaEvent providerEvent)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            Text = providerEvent.TextDelta,
            Status = context.Status,
            Phase = context.Phase ?? "final_answer",
            SourceMethod = context.SourceMethod,
            Diagnostics = BuildDiagnostics(context),
        };

    private static ControlPlaneConversationStreamEvent BuildReasoningDeltaEvent(ProviderEventProjectionContext context, ProviderReasoningDeltaEvent providerEvent)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ReasoningDelta,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            Text = providerEvent.TextDelta,
            Status = context.Status ?? "reasoning",
            Phase = context.Phase ?? "reasoning",
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.Reasoning,
            Payload = ToPayloadStructuredValue(new ReasoningEventPayload(
                context.ItemId,
                context.Status ?? "reasoning",
                context.Phase ?? "reasoning",
                providerEvent.TextDelta,
                context.SourceMethod,
                context.SummaryIndex,
                context.ContentIndex)),
            Diagnostics = BuildDiagnostics(context),
        };

    private static IReadOnlyList<ControlPlaneConversationStreamEvent> BuildToolDirectiveEvents(ProviderEventProjectionContext context, ProviderToolDirectiveEvent providerEvent)
    {
        var directive = providerEvent.Directive;
        var toolName = context.ToolName ?? directive.ToolKey;
        var inputText = SerializeStructuredValue(directive.Input);
        var phase = ResolveToolDirectivePhase(context);
        var toolCallPayload = new ToolCallEventPayload(
            context.ItemId,
            directive.CallId.Value,
            toolName,
            context.ServerName,
            inputText,
            null,
            context.Status ?? "tool_directive",
            phase,
            directive.RequiresApproval);

        var started = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = directive.CallId,
            ToolName = toolName,
            ServerName = context.ServerName,
            Text = inputText,
            Status = context.Status ?? "tool_directive",
            Phase = phase,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = ToPayloadStructuredValue(toolCallPayload),
            Diagnostics = BuildDiagnostics(context),
        };

        if (!directive.RequiresApproval)
        {
            return [started];
        }

        var approvalRequestPayload = new ApprovalRequestPayload(
            toolName,
            null,
            null,
            inputText,
            Array.Empty<ApprovalMetadataFieldPayload>());
        var approval = new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ApprovalRequested,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = directive.CallId,
            ToolName = toolName,
            ServerName = context.ServerName,
            Text = inputText,
            Status = context.Status ?? "tool_directive",
            Phase = phase,
            RequiresApproval = true,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ApprovalRequest,
            Payload = ToPayloadStructuredValue(approvalRequestPayload),
            Diagnostics = BuildDiagnostics(context),
        };

        return [started, approval];
    }

    private static ControlPlaneConversationStreamEvent BuildToolOutputDeltaEvent(ProviderEventProjectionContext context, ProviderToolOutputDeltaEvent providerEvent)
    {
        var delta = providerEvent.Delta;
        var toolName = context.ToolName ?? delta.ToolKey;
        var inputText = SerializeStructuredValue(delta.Input);
        var outputText = delta.OutputText;
        var toolCallPayload = new ToolCallEventPayload(
            context.ItemId,
            delta.CallId.Value,
            toolName,
            context.ServerName,
            inputText,
            outputText,
            context.Status ?? "delta",
            context.Phase,
            delta.RequiresApproval);

        return new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallOutputDelta,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = delta.CallId,
            ToolName = toolName,
            ServerName = context.ServerName,
            Text = outputText,
            Status = context.Status ?? "delta",
            Phase = context.Phase,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = ToPayloadStructuredValue(toolCallPayload),
            Diagnostics = BuildDiagnostics(context),
        };
    }

    private static ControlPlaneConversationStreamEvent BuildToolResultEvent(ProviderEventProjectionContext context, ProviderToolResultEvent providerEvent)
    {
        var result = providerEvent.Result;
        var toolName = context.ToolName ?? result.ToolKey;
        var inputText = SerializeStructuredValue(result.Input);
        var outputText = result.OutputText ?? SerializeStructuredValue(result.Output);
        var toolCallPayload = new ToolCallEventPayload(
            context.ItemId,
            result.CallId.Value,
            toolName,
            context.ServerName,
            inputText,
            outputText,
            context.Status ?? "completed",
            context.Phase,
            result.RequiresApproval);

        return new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallCompleted,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            CallId = result.CallId,
            ToolName = toolName,
            ServerName = context.ServerName,
            Text = outputText ?? inputText,
            Status = context.Status ?? "completed",
            Phase = context.Phase,
            Message = context.SourceMethod,
            SourceMethod = context.SourceMethod,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = ToPayloadStructuredValue(toolCallPayload),
            Diagnostics = BuildDiagnostics(context),
        };
    }

    private static ControlPlaneConversationStreamEvent BuildCompletionEvent(ProviderEventProjectionContext context, ProviderCompletionEvent providerEvent)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextCompleted,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            Text = providerEvent.Completion.OutputText,
            Status = context.Status ?? "completed",
            Phase = context.Phase ?? "final_answer",
            SourceMethod = context.SourceMethod,
            Diagnostics = BuildDiagnostics(context),
        };

    private static ControlPlaneConversationStreamEvent BuildFailureEvent(ProviderEventProjectionContext context, ProviderFailureEvent providerEvent)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.Error,
            ThreadId = ToThreadId(context.ThreadId),
            TurnId = ToTurnId(context.TurnId),
            ItemId = context.ItemId,
            Message = providerEvent.Failure.Message,
            Status = context.Status ?? "error",
            WillRetry = providerEvent.Failure.IsRetryable,
            SourceMethod = context.SourceMethod,
            TurnError = new ControlPlaneThreadTurnError
            {
                Message = providerEvent.Failure.Message,
                AdditionalDetails = ResolveFailureAdditionalDetails(providerEvent.Failure),
            },
            Diagnostics = BuildDiagnostics(context),
        };

    private static ThreadId? ToThreadId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new ThreadId(value);

    private static TurnId? ToTurnId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new TurnId(value);

    private static ControlPlaneConversationStreamDiagnostics? BuildDiagnostics(ProviderEventProjectionContext context)
        => string.IsNullOrWhiteSpace(context.RawJson)
            ? null
            : new ControlPlaneConversationStreamDiagnostics
            {
                RawJson = context.RawJson,
            };

    private static StructuredValue? ToPayloadStructuredValue(object? value)
        => value is null
            ? null
            : StructuredValue.FromJsonElement(JsonSerializer.SerializeToElement(value, StructuredJsonOptions));

    private static string? SerializeStructuredValue(Contracts.Primitives.StructuredValue? value)
        => value switch
        {
            null => null,
            { Kind: Contracts.Primitives.StructuredValueKind.String } => value.StringValue,
            { Kind: Contracts.Primitives.StructuredValueKind.Number } => value.NumberValue,
            { Kind: Contracts.Primitives.StructuredValueKind.Boolean } => value.BooleanValue?.ToString()?.ToLowerInvariant(),
            { Kind: Contracts.Primitives.StructuredValueKind.Null } => null,
            _ => JsonSerializer.Serialize(value.ToPlainObject()),
        };

    private static string? ResolveToolDirectivePhase(ProviderEventProjectionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Phase))
        {
            return context.Phase;
        }

        if (string.Equals(context.SourceMethod, "item/tool/requestApproval", StringComparison.Ordinal))
        {
            return "request_approval";
        }

        return null;
    }

    private static string? ResolveFailureAdditionalDetails(ProviderFailure failure)
    {
        if (!string.IsNullOrWhiteSpace(failure.AdditionalDetails))
        {
            return failure.AdditionalDetails;
        }

        return string.Equals(failure.Code, "error", StringComparison.Ordinal)
            ? null
            : failure.Code;
    }
}
