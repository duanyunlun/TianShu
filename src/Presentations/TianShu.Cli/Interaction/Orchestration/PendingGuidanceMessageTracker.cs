using System.Text.Json;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Orchestration;

internal sealed class PendingGuidanceMessageTracker
{
    private readonly Dictionary<string, string> messagesByCorrelation = new(StringComparer.Ordinal);

    public bool Track(string correlationId, string text)
    {
        var normalizedCorrelationId = Normalize(correlationId);
        var normalizedText = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalizedCorrelationId) || string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        lock (messagesByCorrelation)
        {
            if (messagesByCorrelation.ContainsKey(normalizedCorrelationId!))
            {
                return false;
            }

            messagesByCorrelation[normalizedCorrelationId!] = normalizedText!;
            return true;
        }
    }

    public bool TryConsumeCommittedMessage(ControlPlaneConversationStreamEvent streamEvent, out string message)
    {
        var committed = ReadCommittedUserMessage(streamEvent);
        if (string.IsNullOrWhiteSpace(committed.CorrelationId))
        {
            message = string.Empty;
            return false;
        }

        string? pendingText;
        lock (messagesByCorrelation)
        {
            if (!messagesByCorrelation.Remove(committed.CorrelationId!, out pendingText))
            {
                message = string.Empty;
                return false;
            }
        }

        message = Normalize(committed.Text) ?? pendingText;
        return true;
    }

    public void TrackPendingState(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.PendingInputState
            || streamEvent.Payload is null)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject());
        if (!TryReadProperty(payload, "pendingSteers", out var pendingSteers)
            || pendingSteers.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in pendingSteers.EnumerateArray())
        {
            if (!string.Equals(ReadString(entry, "lifecycleState"), "awaiting_commit", StringComparison.Ordinal)
                || !string.Equals(ReadString(entry, "pendingBucket"), "PendingSteer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var correlationId = ReadString(entry, "correlationId");
            var text = ReadTextInput(entry);
            Track(correlationId ?? string.Empty, text ?? string.Empty);
        }
    }

    public void Clear()
    {
        lock (messagesByCorrelation)
        {
            messagesByCorrelation.Clear();
        }
    }

    private static (string? CorrelationId, string? Text) ReadCommittedUserMessage(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.PayloadKind == ControlPlaneConversationStreamPayloadKind.PendingInputState
            && string.Equals(streamEvent.Status, "committed", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(streamEvent.ItemId))
        {
            return (streamEvent.ItemId, streamEvent.Text);
        }

        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.CommittedUserMessage
            || streamEvent.Payload is null)
        {
            return (null, streamEvent.Text);
        }

        var payload = JsonSerializer.SerializeToElement(streamEvent.Payload.ToPlainObject());
        return (
            ReadString(payload, "correlationId"),
            ReadString(payload, "text") ?? streamEvent.Text);
    }

    private static string? ReadTextInput(JsonElement entry)
    {
        if (!TryReadProperty(entry, "inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var input in inputs.EnumerateArray())
        {
            if (string.Equals(ReadString(input, "type"), "text", StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(input, "text");
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryReadProperty(element, propertyName, out var propertyValue)
            || propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyValue.GetString();
    }

    private static bool TryReadProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            propertyValue = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = property.Value;
                return true;
            }
        }

        propertyValue = default;
        return false;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
