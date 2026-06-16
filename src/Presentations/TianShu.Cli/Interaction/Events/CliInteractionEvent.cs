using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Events;

internal abstract record CliInteractionEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp);

internal sealed record AssistantTextDeltaEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string Text) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record AssistantTextCompletedEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record ToolInvocationEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string ToolName,
    string? CallId,
    string? ItemId,
    string? InputText,
    string? OutputText,
    string? Status,
    string? Phase,
    ToolInvocationPhase InvocationPhase,
    ToolInvocationPayload? Payload = null) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record PlanUpdatedInteractionEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string? Explanation,
    StructuredValue? Payload) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record ErrorInteractionEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string Message) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record TurnCompletedInteractionEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string? Status) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal sealed record PassthroughInteractionEvent(
    string? ThreadId,
    string? TurnId,
    DateTimeOffset Timestamp,
    string Kind) : CliInteractionEvent(ThreadId, TurnId, Timestamp);

internal enum ToolInvocationPhase
{
    Started = 0,
    Completed = 1,
}
