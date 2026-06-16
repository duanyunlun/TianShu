using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed record KernelOpenAiAppsToolSnapshot(
    IReadOnlyList<KernelDynamicToolDescriptor>? DynamicTools,
    IReadOnlyList<KernelToolSuggestConnectorInfo> AccessibleConnectors);

internal static class KernelToolDiscoveryToolNames
{
    public const string Search = "tool_search";
    public const string Suggest = "tool_suggest";
}

internal static class KernelToolDiscoveryRuntimeSupport
{
    private const int SearchDefaultLimit = 8;
    private const string ConnectorToolType = "connector";
    private const string PluginToolType = "plugin";
    private const string InstallActionType = "install";
    private const string EnableActionType = "enable";
    private const string ToolSuggestionApprovalKind = "tool_suggestion";
    private const string SearchDescriptionTemplate =
        """
        # Apps (Connectors) tool discovery

        Searches over apps/connectors tool metadata with BM25 and exposes matching tools for the next model call.

        Tools of the apps ({{app_names}}) are hidden until you search for them with this tool (`tool_search`).
        When the request needs one of these connectors and you don't already have the required tools from it, use this tool to load them. For the apps mentioned above, always prefer `tool_search` over `list_mcp_resources` or `list_mcp_resource_templates` for tool discovery.
        """;

    private const string SuggestDescriptionTemplate =
        """
        # Tool suggestion discovery

        Suggests a discoverable connector or plugin when the user clearly wants a capability that is not currently available in the active `tools` list.

        Use this ONLY when:
        - There's no available tool to handle the user's request
        - And tool_search fails to find a good match
        - AND the user's request strongly matches one of the discoverable tools listed below.

        Tool suggestions should only use the discoverable tools listed here. DO NOT explore or recommend tools that are not on this list.

        Discoverable tools:
        {{discoverable_tools}}

        Workflow:

        1. Match the user's request against the discoverable tools list above.
        2. If one tool clearly fits, call `tool_suggest` with:
           - `tool_type`: `connector` or `plugin`
           - `action_type`: `install` or `enable`
           - `tool_id`: exact id from the discoverable tools list above
           - `suggest_reason`: concise one-line user-facing reason this tool can help with the current request
        3. After the suggestion flow completes:
           - if the user finished the install or enable flow, continue by searching again or using the newly available tool
           - if the user did not finish, continue without that tool, and don't suggest that tool again unless the user explicitly asks you to.
        """;

    private static readonly JsonElement SearchInputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search query for apps tools." },
            limit = new { type = "number", description = $"Maximum number of tools to return (defaults to {SearchDefaultLimit})." },
        },
        required = new[] { "query" },
        additionalProperties = false,
    });

    public static ProviderResponsesToolDefinition BuildSearchProviderToolDefinition(IReadOnlyList<string>? connectorNames)
        => new ProviderResponsesHostedToolDefinition(
            toolType: KernelToolDiscoveryToolNames.Search,
            description: BuildSearchDescription(connectorNames),
            inputSchema: SearchInputSchemaElement,
            execution: "client");

    public static ProviderResponsesToolDefinition BuildSuggestProviderToolDefinition(IReadOnlyList<KernelToolSuggestConnectorInfo>? discoverableConnectors)
    {
        var toolIds = (discoverableConnectors ?? Array.Empty<KernelToolSuggestConnectorInfo>())
            .Select(static connector => connector.Id)
            .ToArray();
        var inputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                tool_type = new
                {
                    type = "string",
                    description = "Type of discoverable tool to suggest. Use \"connector\" or \"plugin\".",
                    @enum = new[] { ConnectorToolType, PluginToolType },
                },
                action_type = new
                {
                    type = "string",
                    description = "Suggested action for the tool. Use \"install\" or \"enable\".",
                    @enum = new[] { InstallActionType, EnableActionType },
                },
                tool_id = new
                {
                    type = "string",
                    description = toolIds.Length == 0
                        ? "Connector or plugin id to suggest."
                        : $"Connector or plugin id to suggest. Must be one of: {string.Join(", ", toolIds)}.",
                },
                suggest_reason = new
                {
                    type = "string",
                    description = "Concise one-line user-facing reason why this tool can help with the current request.",
                },
            },
            required = new[] { "tool_type", "action_type", "tool_id", "suggest_reason" },
            additionalProperties = false,
        });

        return new ProviderResponsesFunctionToolDefinition(
            KernelToolDiscoveryToolNames.Suggest,
            BuildSuggestDescription(discoverableConnectors),
            inputSchema,
            strict: false);
    }

    public static McpServerElicitationRequest BuildToolSuggestionElicitationRequest(
        KernelToolSuggestConnectorInfo connector,
        string suggestReason,
        string toolType,
        string actionType)
    {
        var installUrl = KernelToolJsonHelpers.Normalize(connector.InstallUrl)
            ?? throw new InvalidOperationException("tool_suggest installUrl must not be empty.");
        var message =
            $"{connector.Name} could help with this request.{Environment.NewLine}{Environment.NewLine}{suggestReason}{Environment.NewLine}{Environment.NewLine}Open the installation link to {actionType} it, then confirm here if you finish.";
        var meta = JsonSerializer.SerializeToElement(new
        {
            codex_approval_kind = ToolSuggestionApprovalKind,
            tool_type = toolType,
            suggest_type = actionType,
            suggest_reason = suggestReason,
            tool_id = connector.Id,
            tool_name = connector.Name,
            install_url = installUrl,
        });

        return new McpServerElicitationRequest(
            ServerName: OpenAiAppCatalogCompatibilityKeys.CodexAppsMcpServerName,
            Mode: "form",
            Message: message,
            RequestedSchema: EmptyRequestedSchema(),
            Meta: meta);
    }

    private static string BuildSearchDescription(IReadOnlyList<string>? connectorNames)
    {
        var names = connectorNames is { Count: > 0 }
            ? string.Join(", ", connectorNames.Where(static name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal))
            : "available apps/connectors";
        return SearchDescriptionTemplate.Replace("{{app_names}}", names, StringComparison.Ordinal);
    }

    private static string BuildSuggestDescription(IReadOnlyList<KernelToolSuggestConnectorInfo>? discoverableConnectors)
    {
        var formatted = FormatDiscoverableConnectors(discoverableConnectors);
        return SuggestDescriptionTemplate.Replace(
            "{{discoverable_tools}}",
            string.IsNullOrWhiteSpace(formatted) ? "- No discoverable tools available." : formatted,
            StringComparison.Ordinal);
    }

    private static string FormatDiscoverableConnectors(IReadOnlyList<KernelToolSuggestConnectorInfo>? connectors)
    {
        if (connectors is null || connectors.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            connectors
                .OrderBy(static connector => connector.Name, StringComparer.Ordinal)
                .ThenBy(static connector => connector.Id, StringComparer.Ordinal)
                .Select(static connector =>
                {
                    var description = KernelToolJsonHelpers.Normalize(connector.Description) ?? "No description provided.";
                    return $"- {connector.Name} (id: `{connector.Id}`, type: {ConnectorToolType}, action: {InstallActionType}): {description}";
                }));
    }

    private static JsonElement EmptyRequestedSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false,
        });
}
