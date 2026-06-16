using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Events;

internal static class CliToolInvocationEventNormalizer
{
    public static bool TryBuildItemToolInvocationEvent(
        ControlPlaneConversationStreamEvent streamEvent,
        ToolInvocationPhase invocationPhase,
        out CliInteractionEvent interactionEvent)
    {
        interactionEvent = new PassthroughInteractionEvent(
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            ResolveTimestamp(streamEvent),
            streamEvent.Kind.ToString());

        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.Item
            || streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object)
        {
            return false;
        }

        var itemType = Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "itemType"))
                       ?? Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "type"));
        if (!ToolInvocationItemPresentationPolicy.TryResolve(itemType, out var descriptor))
        {
            return false;
        }

        var toolName = Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "toolName"))
                       ?? Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "name"))
                       ?? Normalize(streamEvent.ToolName)
                       ?? descriptor.CanonicalToolName
                       ?? itemType
                       ?? "tool";
        var inputText = Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "arguments"))
                        ?? Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "inputText"))
                        ?? Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "input"))
                        ?? Normalize(ToolInvocationItemPresentationPolicy.ReadSubjectFallback(streamEvent, itemType));
        var outputText = Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "outputText"))
                         ?? Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "output"));
        var eventInputText = invocationPhase == ToolInvocationPhase.Started ? inputText ?? Normalize(streamEvent.Text) : inputText;
        var eventOutputText = invocationPhase == ToolInvocationPhase.Completed ? outputText : null;
        var eventStatus = Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "status")) ?? Normalize(streamEvent.Status);
        interactionEvent = new ToolInvocationEvent(
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            ResolveTimestamp(streamEvent),
            toolName,
            Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "callId")) ?? streamEvent.CallId?.Value,
            Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "itemId")) ?? Normalize(streamEvent.ItemId),
            eventInputText,
            eventOutputText,
            eventStatus,
            Normalize(CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "phase")) ?? Normalize(streamEvent.Phase),
            invocationPhase,
            ToolInvocationPayload.Create(
                toolName,
                CliStructuredPayloadReader.ReadToolPresentationKind(streamEvent) ?? descriptor.Kind,
                eventInputText,
                eventOutputText,
                eventStatus));
        return true;
    }

    public static ToolInvocationEvent BuildToolInvocationEvent(
        ControlPlaneConversationStreamEvent streamEvent,
        ToolInvocationPhase invocationPhase)
    {
        var toolName = Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "toolName"))
                       ?? Normalize(streamEvent.ToolName)
                       ?? "tool";
        var eventInputText = Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "inputText")) ?? (invocationPhase == ToolInvocationPhase.Started ? Normalize(streamEvent.Text) : null);
        var outputText = Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "outputText"))
                         ?? Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "output"));
        var eventOutputText = invocationPhase == ToolInvocationPhase.Completed ? outputText : null;
        var eventStatus = Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "status")) ?? Normalize(streamEvent.Status);
        return new ToolInvocationEvent(
            streamEvent.ThreadId?.Value,
            streamEvent.TurnId?.Value,
            ResolveTimestamp(streamEvent),
            toolName,
            Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "callId")) ?? streamEvent.CallId?.Value,
            Normalize(streamEvent.ItemId),
            eventInputText,
            eventOutputText,
            eventStatus,
            Normalize(CliStructuredPayloadReader.ReadToolPayloadString(streamEvent, "phase")) ?? Normalize(streamEvent.Phase),
            invocationPhase,
            ToolInvocationPayload.Create(
                toolName,
                CliStructuredPayloadReader.ReadToolPresentationKind(streamEvent),
                eventInputText,
                eventOutputText,
                eventStatus));
    }

    private static DateTimeOffset ResolveTimestamp(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Timestamp == default ? DateTimeOffset.Now : streamEvent.Timestamp;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
