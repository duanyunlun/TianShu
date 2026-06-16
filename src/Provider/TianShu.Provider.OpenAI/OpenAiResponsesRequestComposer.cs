using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAI;

/// <summary>
/// OpenAI Responses request composer。
/// Request composer for the OpenAI Responses API.
/// </summary>
public sealed class OpenAiResponsesRequestComposer : IProviderResponsesRequestComposer
{
    private const string OutputSchemaName = "codex_output_schema";

    /// <inheritdoc />
    public string WireApi => "responses";

    /// <inheritdoc />
    public ProviderResponsesRequestComposition Compose(ProviderResponsesRequestComposerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Model);
        ArgumentNullException.ThrowIfNull(context.Input);
        ArgumentNullException.ThrowIfNull(context.Tools);

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = context.Model,
            ["instructions"] = context.Instructions,
            ["tools"] = context.Tools,
            ["store"] = context.Store,
            ["include"] = BuildIncludeFields(context.ReasoningEffort, context.ReasoningSummary),
        };

        if (context.Stream.HasValue)
        {
            payload["stream"] = context.Stream.Value;
        }

        if (!string.IsNullOrWhiteSpace(context.ToolChoice))
        {
            payload["tool_choice"] = context.ToolChoice;
        }

        if (context.ParallelToolCalls.HasValue)
        {
            payload["parallel_tool_calls"] = context.ParallelToolCalls.Value;
        }

        if (!string.IsNullOrWhiteSpace(context.ServiceTier))
        {
            payload["service_tier"] = context.ServiceTier;
        }

        var reasoning = BuildReasoningPayload(context.ReasoningEffort, context.ReasoningSummary);
        if (reasoning is not null)
        {
            payload["reasoning"] = reasoning;
        }

        var text = BuildTextPayload(context.TextVerbosity, context.OutputSchema);
        if (text is not null)
        {
            payload["text"] = text;
        }

        return new ProviderResponsesRequestComposition(payload, CloneJsonElements(context.Input));
    }

    private static IReadOnlyList<string> BuildIncludeFields(string? reasoningEffort, string? reasoningSummary)
    {
        return string.IsNullOrWhiteSpace(reasoningEffort) && string.IsNullOrWhiteSpace(reasoningSummary)
            ? Array.Empty<string>()
            : ["reasoning.encrypted_content"];
    }

    private static object? BuildReasoningPayload(string? effort, string? summary)
    {
        if (string.IsNullOrWhiteSpace(effort) && string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(effort))
        {
            payload["effort"] = effort;
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            payload["summary"] = summary;
        }

        return payload.Count == 0 ? null : payload;
    }

    private static object? BuildTextPayload(string? verbosity, JsonElement? outputSchema)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(verbosity))
        {
            payload["verbosity"] = NormalizeTextVerbosity(verbosity);
        }

        if (outputSchema is { } schema)
        {
            payload["format"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = OutputSchemaName,
                ["type"] = "json_schema",
                ["strict"] = true,
                ["schema"] = schema,
            };
        }

        return payload.Count == 0 ? null : payload;
    }

    private static string NormalizeTextVerbosity(string verbosity)
        => verbosity.Trim().ToLowerInvariant() switch
        {
            "concise" => "low",
            "normal" => "medium",
            "detailed" => "high",
            var value => value,
        };

    private static IReadOnlyList<JsonElement> CloneJsonElements(IEnumerable<JsonElement> items)
        => items.Select(static item => item.Clone()).ToArray();
}
