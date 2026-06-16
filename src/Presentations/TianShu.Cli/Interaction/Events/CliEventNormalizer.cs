using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Events;

internal static class CliEventNormalizer
{
    public static CliInteractionEvent Normalize(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Kind switch
        {
            ControlPlaneConversationStreamEventKind.AssistantTextDelta => new AssistantTextDeltaEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent),
                streamEvent.Text ?? string.Empty),

            ControlPlaneConversationStreamEventKind.AssistantTextCompleted => new AssistantTextCompletedEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent)),

            ControlPlaneConversationStreamEventKind.ToolCallStarted => CliToolInvocationEventNormalizer.BuildToolInvocationEvent(streamEvent, ToolInvocationPhase.Started),
            ControlPlaneConversationStreamEventKind.ToolCallCompleted => CliToolInvocationEventNormalizer.BuildToolInvocationEvent(streamEvent, ToolInvocationPhase.Completed),
            ControlPlaneConversationStreamEventKind.ItemStarted when CliToolInvocationEventNormalizer.TryBuildItemToolInvocationEvent(streamEvent, ToolInvocationPhase.Started, out var itemStarted)
                => itemStarted,
            ControlPlaneConversationStreamEventKind.ItemCompleted when CliToolInvocationEventNormalizer.TryBuildItemToolInvocationEvent(streamEvent, ToolInvocationPhase.Completed, out var itemCompleted)
                => itemCompleted,

            ControlPlaneConversationStreamEventKind.PlanUpdated => new PlanUpdatedInteractionEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent),
                Normalize(streamEvent.Text ?? streamEvent.Message),
                streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.Plan ? streamEvent.Payload : null),

            ControlPlaneConversationStreamEventKind.Error => new ErrorInteractionEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent),
                Normalize(streamEvent.Message ?? streamEvent.Text) ?? "收到错误事件。"),

            ControlPlaneConversationStreamEventKind.TurnCompleted => new TurnCompletedInteractionEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent),
                Normalize(streamEvent.Status)),

            _ => new PassthroughInteractionEvent(
                streamEvent.ThreadId?.Value,
                streamEvent.TurnId?.Value,
                ResolveTimestamp(streamEvent),
                streamEvent.Kind.ToString()),
        };

    private static DateTimeOffset ResolveTimestamp(ControlPlaneConversationStreamEvent streamEvent)
        => streamEvent.Timestamp == default ? DateTimeOffset.Now : streamEvent.Timestamp;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
