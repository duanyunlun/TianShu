using TianShu.Cli.Interaction;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Interaction.Events;

internal static class ToolInvocationItemPresentationPolicy
{
    private static readonly IReadOnlyDictionary<string, ToolInvocationItemPresentationDescriptor> Descriptors =
        new Dictionary<string, ToolInvocationItemPresentationDescriptor>(StringComparer.Ordinal)
        {
            ["dynamictoolcall"] = new(null, null),
            ["mcptoolcall"] = new(null, null),
            ["collabagenttoolcall"] = new(null, null),
            ["commandexecution"] = new("commandExecution", ToolPresentationKind.Command),
            ["localshellcall"] = new("local_shell_call", ToolPresentationKind.Command),
            ["filechange"] = new("fileChange", ToolPresentationKind.FileChange),
            ["websearch"] = new("webSearch", ToolPresentationKind.WebSearch),
            ["websearchcall"] = new("webSearch", ToolPresentationKind.WebSearch),
            ["imagegeneration"] = new("imageGeneration", ToolPresentationKind.ImageGeneration),
            ["imagegenerationcall"] = new("imageGeneration", ToolPresentationKind.ImageGeneration),
            ["imageview"] = new("imageView", ToolPresentationKind.ImageView),
            ["toolsearchcall"] = new("toolSearch", ToolPresentationKind.Search),
        };

    public static bool TryResolve(string? itemType, out ToolInvocationItemPresentationDescriptor descriptor)
    {
        descriptor = default;
        var normalized = Normalize(itemType);
        return normalized is not null && Descriptors.TryGetValue(normalized, out descriptor);
    }

    public static string? ReadSubjectFallback(ControlPlaneConversationStreamEvent streamEvent, string? itemType)
    {
        var normalized = Normalize(itemType);
        return normalized switch
        {
            "filechange" => CliStructuredPayloadReader.ReadFirstFileChangePath(streamEvent),
            "websearch" or "websearchcall" => CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "query")
                                               ?? CliStructuredPayloadReader.ReadNestedItemPayloadString(streamEvent, "action", "query"),
            "imagegeneration" or "imagegenerationcall" => CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "savedPath")
                                                          ?? CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "revisedPrompt")
                                                          ?? CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "revised_prompt"),
            "imageview" => CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "path"),
            "commandexecution" => CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "command"),
            "localshellcall" => CliStructuredPayloadReader.ReadNestedItemPayloadString(streamEvent, "action", "command")
                                ?? CliStructuredPayloadReader.ReadNestedItemPayloadString(streamEvent, "action", "input"),
            "toolsearchcall" => CliStructuredPayloadReader.ReadItemPayloadString(streamEvent, "query"),
            _ => null,
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
}

internal readonly record struct ToolInvocationItemPresentationDescriptor(
    string? CanonicalToolName,
    ToolPresentationKind? Kind);
