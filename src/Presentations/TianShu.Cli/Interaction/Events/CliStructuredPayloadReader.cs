using System.Globalization;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Interaction.Events;

internal static class CliStructuredPayloadReader
{
    public static string? ReadToolPayloadString(ControlPlaneConversationStreamEvent streamEvent, string propertyName)
    {
        if (streamEvent.PayloadKind != ControlPlaneConversationStreamPayloadKind.ToolCall
            || streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, propertyName, out var propertyValue))
        {
            return null;
        }

        return ReadStructuredValueString(propertyValue);
    }

    public static string? ReadItemPayloadString(ControlPlaneConversationStreamEvent streamEvent, string propertyName)
    {
        if (streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, propertyName, out var propertyValue))
        {
            return null;
        }

        return ReadStructuredValueString(propertyValue);
    }

    public static string? ReadNestedItemPayloadString(
        ControlPlaneConversationStreamEvent streamEvent,
        string objectName,
        string propertyName)
    {
        if (streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, objectName, out var objectValue)
            || objectValue.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(objectValue, propertyName, out var propertyValue))
        {
            return null;
        }

        return ReadStructuredValueString(propertyValue);
    }

    public static string? ReadFirstFileChangePath(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.Payload is null
            || streamEvent.Payload.Kind != StructuredValueKind.Object
            || !TryReadObjectPropertyIgnoreCase(streamEvent.Payload, "changes", out var changes)
            || changes.Kind != StructuredValueKind.Array)
        {
            return null;
        }

        foreach (var change in changes.Items)
        {
            if (change.Kind == StructuredValueKind.Object
                && TryReadObjectPropertyIgnoreCase(change, "path", out var path)
                && path.Kind == StructuredValueKind.String)
            {
                return Normalize(path.StringValue);
            }
        }

        return null;
    }

    public static ToolPresentationKind? ReadToolPresentationKind(ControlPlaneConversationStreamEvent streamEvent)
    {
        if (streamEvent.Payload is null || streamEvent.Payload.Kind != StructuredValueKind.Object)
        {
            return null;
        }

        return ReadPresentationKind(streamEvent.Payload);
    }

    public static bool TryReadObjectPropertyIgnoreCase(StructuredValue value, string propertyName, out StructuredValue propertyValue)
    {
        if (value.Kind == StructuredValueKind.Object)
        {
            foreach (var pair in value.Properties)
            {
                if (string.Equals(pair.Key, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyValue = pair.Value;
                    return true;
                }
            }
        }

        propertyValue = StructuredValue.Null;
        return false;
    }

    private static string? ReadStructuredValueString(StructuredValue value)
        => value.Kind switch
        {
            StructuredValueKind.String => value.StringValue,
            StructuredValueKind.Number => value.NumberValue,
            StructuredValueKind.Boolean => value.BooleanValue?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            StructuredValueKind.Null => null,
            _ => Convert.ToString(value.ToPlainObject(), CultureInfo.InvariantCulture),
        };

    private static ToolPresentationKind? ReadPresentationKind(StructuredValue payload)
    {
        foreach (var name in new[] { "presentationKind", "displayKind", "canonicalKind", "toolKind", "category", "operation", "kind", "type" })
        {
            if (TryReadObjectPropertyIgnoreCase(payload, name, out var value)
                && ToolPresentationKindResolver.ResolveFromMetadata(ReadStructuredValueString(value)) is { } kind)
            {
                return kind;
            }
        }

        foreach (var metadataName in new[] { "metadata", "appMetadata" })
        {
            if (TryReadObjectPropertyIgnoreCase(payload, metadataName, out var metadata)
                && metadata.Kind == StructuredValueKind.Object
                && ReadPresentationKind(metadata) is { } kind)
            {
                return kind;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
