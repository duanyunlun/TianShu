using System.Text.Json;
using System.Text.RegularExpressions;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Tools.Search;

/// <summary>
/// Search 工具域 Provider。
/// Provider for the Search tool domain.
/// </summary>
public sealed class SearchToolProvider : ITianShuToolProvider
{
    public IReadOnlyList<ToolDescriptor> DescribeTools(TianShuToolRegistrationContext context)
    {
        _ = context;
        return [SearchToolHandler.DescriptorInstance, ToolSuggestToolHandler.DescriptorInstance];
    }

    public ITianShuToolHandler CreateHandler(string toolKey, TianShuToolActivationContext context)
    {
        _ = context;
        if (string.Equals(toolKey, SearchToolHandler.ToolName, StringComparison.Ordinal))
        {
            return new SearchToolHandler();
        }

        if (string.Equals(toolKey, ToolSuggestToolHandler.ToolName, StringComparison.Ordinal))
        {
            return new ToolSuggestToolHandler();
        }

        throw new InvalidOperationException($"Unknown search tool: {toolKey}");
    }
}

internal sealed class ToolSuggestToolHandler : ITianShuToolHandler
{
    public const string ToolName = "tool_suggest";

    private const string ConnectorToolType = "connector";
    private const string PluginToolType = "plugin";
    private const string InstallActionType = "install";
    private const string EnableActionType = "enable";

    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
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
                description = "Connector or plugin id to suggest.",
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

    public static ToolDescriptor DescriptorInstance { get; } = new(
        ToolName,
        "Tool Suggest",
        "Suggests a discoverable connector or plugin when a required capability is not currently available.",
        capabilities: [new ToolCapability("tool-discovery", "Suggest discoverable tools through governed host confirmation.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            ToolName,
            ToolImplementationKind.Managed,
            implementationId: "tianshu.tools.search"),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public async ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        var toolType = Normalize(ReadString(request.Input, "tool_type"));
        var actionType = Normalize(ReadString(request.Input, "action_type"));
        var toolId = Normalize(ReadString(request.Input, "tool_id"));
        var suggestReason = Normalize(ReadString(request.Input, "suggest_reason"));

        if (string.IsNullOrWhiteSpace(suggestReason))
        {
            return BuildFailure(request, "suggest_reason must not be empty");
        }

        if (string.Equals(toolType, PluginToolType, StringComparison.OrdinalIgnoreCase))
        {
            return BuildFailure(request, "plugin tool suggestions are not currently available");
        }

        if (!string.Equals(actionType, InstallActionType, StringComparison.OrdinalIgnoreCase))
        {
            return BuildFailure(request, "connector tool suggestions currently support only action_type=\"install\"");
        }

        if (context.ToolSuggestionServices is null)
        {
            return BuildFailure(request, "tool_suggest is unavailable");
        }

        var discoverableConnectors = await context.ToolSuggestionServices
            .ListDiscoverableConnectorsAsync(cancellationToken)
            .ConfigureAwait(false);
        var connector = discoverableConnectors.FirstOrDefault(
            candidate => string.Equals(candidate.Id, toolId, StringComparison.OrdinalIgnoreCase));
        if (connector is null)
        {
            return BuildFailure(request, $"tool_id must match one of the discoverable tools exposed by {ToolName}");
        }

        TianShuToolSuggestionResult result;
        try
        {
            result = await context.ToolSuggestionServices
                .SuggestConnectorAsync(
                    new TianShuToolSuggestionRequest(
                        toolType ?? ConnectorToolType,
                        actionType ?? InstallActionType,
                        connector.Id,
                        suggestReason!),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return BuildFailure(request, exception.Message);
        }

        var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["completed"] = result.Completed,
            ["user_confirmed"] = result.UserConfirmed,
            ["tool_type"] = result.ToolType,
            ["action_type"] = result.ActionType,
            ["tool_id"] = result.ToolId,
            ["tool_name"] = result.ToolName,
            ["suggest_reason"] = result.SuggestReason,
        });

        return new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", payload, isTerminal: true)]);
    }

    private static ToolInvocationResult BuildFailure(ToolInvocationRequest request, string message)
        => new(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure("tool_suggest.invalid_request", message));

    private static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SearchToolHandler : ITianShuToolHandler
{
    public const string ToolName = "tool_search";
    private const int DefaultLimit = 8;

    private static readonly Regex TokenRegex = new("[A-Za-z0-9_]+", RegexOptions.Compiled);
    private static readonly JsonElement InputSchemaElement = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Search query for apps/connectors tools." },
            limit = new { type = "number", description = $"Maximum number of tools to return (defaults to {DefaultLimit})." },
        },
        required = new[] { "query" },
        additionalProperties = false,
    });

    public static ToolDescriptor DescriptorInstance { get; } = new(
        ToolName,
        "Tool Search",
        "Searches over apps/connectors tool metadata and exposes matching tools for the next model call.",
        capabilities: [new ToolCapability("tool-discovery", "Search deferred dynamic tools.")],
        approvalRequirement: ToolApprovalRequirement.None,
        concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
        implementationBinding: new ToolImplementationBinding(
            ToolName,
            ToolImplementationKind.Managed,
            implementationId: "tianshu.tools.search"),
        inputSchema: InputSchemaElement);

    public ToolDescriptor Descriptor => DescriptorInstance;

    public ValueTask<ToolInvocationResult> InvokeAsync(
        ToolInvocationRequest request,
        TianShuToolInvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var query = Normalize(ReadString(request.Input, "query"));
        if (string.IsNullOrWhiteSpace(query))
        {
            return Failure(request, "query must not be empty");
        }

        var limit = ReadInt(request.Input, "limit") ?? DefaultLimit;
        if (limit <= 0)
        {
            return Failure(request, "limit must be greater than zero");
        }

        var entries = BuildEntries(context.DynamicTools)
            .OrderBy(static x => x.FullName, StringComparer.Ordinal)
            .ToArray();
        var ranked = entries.Length == 0 ? [] : RankEntries(entries, query!, limit);
        var payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>
        {
            ["tools"] = SerializeDeferredTools(ranked.Select(static x => x.Entry).ToArray()),
        });

        return ValueTask.FromResult(new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            [new ToolStreamItem("text", payload, isTerminal: true)]));
    }

    private static ValueTask<ToolInvocationResult> Failure(ToolInvocationRequest request, string message)
    {
        return ValueTask.FromResult(new ToolInvocationResult(
            request.CallId,
            request.ToolKey,
            failure: new ToolInvocationFailure("tool_search.invalid_request", message)));
    }

    private static IReadOnlyList<SearchableToolEntry> BuildEntries(IReadOnlyList<TianShuToolDiscoveryDescriptor>? dynamicTools)
    {
        return (dynamicTools ?? Array.Empty<TianShuToolDiscoveryDescriptor>())
            .Select(static descriptor =>
            {
                var inputKeys = ReadInputKeys(descriptor.InputSchema);
                return new SearchableToolEntry(
                    FullName: descriptor.FullName,
                    ShortName: descriptor.ShortName,
                    Namespace: descriptor.Namespace,
                    Server: descriptor.Server,
                    Title: descriptor.Title,
                    Description: descriptor.Description,
                    ConnectorName: descriptor.ConnectorName,
                    ConnectorDescription: descriptor.ConnectorDescription,
                    InputSchema: descriptor.InputSchema,
                    InputKeys: inputKeys,
                    SearchText: BuildSearchText(
                        descriptor.FullName,
                        descriptor.ShortName,
                        descriptor.Server,
                        descriptor.Title,
                        descriptor.Description,
                        descriptor.ConnectorName,
                        descriptor.ConnectorDescription,
                        inputKeys));
            })
            .ToArray();
    }

    private static IReadOnlyList<string> ReadInputKeys(JsonElement? inputSchema)
    {
        if (inputSchema is not { ValueKind: JsonValueKind.Object } schema
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return properties.EnumerateObject()
            .Select(static x => x.Name)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSearchText(
        string fullName,
        string shortName,
        string? server,
        string? title,
        string? description,
        string? connectorName,
        string? connectorDescription,
        IReadOnlyList<string> inputKeys)
    {
        var parts = new List<string> { fullName, shortName };
        Append(parts, server);
        Append(parts, title);
        Append(parts, description);
        Append(parts, connectorName);
        Append(parts, connectorDescription);
        foreach (var key in inputKeys)
        {
            Append(parts, key);
        }

        return string.Join(' ', parts);
    }

    private static void Append(List<string> parts, string? value)
    {
        var normalized = Normalize(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            parts.Add(normalized!);
        }
    }

    private static IReadOnlyList<ScoredToolEntry> RankEntries(
        IReadOnlyList<SearchableToolEntry> entries,
        string query,
        int limit)
    {
        var queryTerms = Tokenize(query);
        if (queryTerms.Count == 0)
        {
            return Array.Empty<ScoredToolEntry>();
        }

        var docs = entries.Select(entry => new TokenizedEntry(entry, Tokenize(entry.SearchText))).ToArray();
        var averageLength = docs.Length == 0 ? 0d : docs.Average(static x => x.Terms.Values.Sum());
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in docs)
        {
            foreach (var term in doc.Terms.Keys)
            {
                documentFrequency[term] = documentFrequency.TryGetValue(term, out var count) ? count + 1 : 1;
            }
        }

        const double k1 = 1.5;
        const double b = 0.75;
        var scored = new List<ScoredToolEntry>();
        foreach (var doc in docs)
        {
            var docLength = doc.Terms.Values.Sum();
            var score = 0d;
            foreach (var queryTerm in queryTerms.Keys)
            {
                if (!doc.Terms.TryGetValue(queryTerm, out var tf) || tf <= 0)
                {
                    continue;
                }

                var df = documentFrequency.TryGetValue(queryTerm, out var frequency) ? frequency : 0;
                if (df == 0)
                {
                    continue;
                }

                var idf = Math.Log(1d + ((docs.Length - df + 0.5d) / (df + 0.5d)));
                var normalization = tf + k1 * (1d - b + b * (docLength / Math.Max(averageLength, 1d)));
                score += idf * ((tf * (k1 + 1d)) / normalization);
            }

            scored.Add(new ScoredToolEntry(doc.Entry, score));
        }

        return scored
            .OrderByDescending(static x => x.Score)
            .ThenBy(static x => x.Entry.FullName, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static Dictionary<string, int> Tokenize(string text)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in TokenRegex.Matches(text))
        {
            var token = match.Value.Trim().ToLowerInvariant();
            if (token.Length == 0)
            {
                continue;
            }

            map[token] = map.TryGetValue(token, out var count) ? count + 1 : 1;
        }

        return map;
    }

    private static IReadOnlyList<object> SerializeDeferredTools(IReadOnlyList<SearchableToolEntry> entries)
    {
        var groupedByNamespace = new SortedDictionary<string, List<SearchableToolEntry>>(StringComparer.Ordinal);
        var topLevelTools = new List<object>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Namespace))
            {
                topLevelTools.Add(BuildDeferredFunctionTool(entry, useShortName: false));
                continue;
            }

            if (!groupedByNamespace.TryGetValue(entry.Namespace!, out var bucket))
            {
                bucket = [];
                groupedByNamespace[entry.Namespace!] = bucket;
            }

            bucket.Add(entry);
        }

        foreach (var pair in groupedByNamespace)
        {
            var first = pair.Value[0];
            var description = Normalize(first.ConnectorDescription)
                ?? (string.IsNullOrWhiteSpace(first.ConnectorName)
                    ? string.Empty
                    : $"Tools for working with {first.ConnectorName}.");
            topLevelTools.Add(new Dictionary<string, object?>
            {
                ["type"] = "namespace",
                ["name"] = pair.Key,
                ["description"] = description,
                ["tools"] = pair.Value
                    .OrderBy(static tool => tool.ShortName, StringComparer.Ordinal)
                    .Select(static tool => BuildDeferredFunctionTool(tool, useShortName: true))
                    .ToArray(),
            });
        }

        return topLevelTools;
    }

    private static Dictionary<string, object?> BuildDeferredFunctionTool(SearchableToolEntry entry, bool useShortName)
        => new(StringComparer.Ordinal)
        {
            ["type"] = "function",
            ["name"] = useShortName ? entry.ShortName : entry.FullName,
            ["description"] = entry.Description ?? string.Empty,
            ["strict"] = false,
            ["defer_loading"] = true,
            ["parameters"] = entry.InputSchema
                           ?? JsonSerializer.SerializeToElement(new
                           {
                               type = "object",
                           }),
        };

    private static string? ReadString(StructuredValue input, string propertyName)
        => input.TryGetProperty(propertyName, out var value) ? value?.GetString() : null;

    private static int? ReadInt(StructuredValue input, string propertyName)
    {
        if (!input.TryGetProperty(propertyName, out var value) || value is null)
        {
            return null;
        }

        return int.TryParse(value.GetString(), out var parsed) ? parsed : null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SearchableToolEntry(
        string FullName,
        string ShortName,
        string? Namespace,
        string? Server,
        string? Title,
        string? Description,
        string? ConnectorName,
        string? ConnectorDescription,
        JsonElement? InputSchema,
        IReadOnlyList<string> InputKeys,
        string SearchText);

    private sealed record TokenizedEntry(SearchableToolEntry Entry, Dictionary<string, int> Terms);

    private sealed record ScoredToolEntry(SearchableToolEntry Entry, double Score);
}
