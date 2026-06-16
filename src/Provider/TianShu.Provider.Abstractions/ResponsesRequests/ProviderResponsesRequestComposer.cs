using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider-specific Responses 请求组装上下文。
/// Context for provider-specific Responses request composition.
/// </summary>
public sealed record ProviderResponsesRequestComposerContext(
    string Model,
    string Instructions,
    IReadOnlyList<JsonElement> Input,
    IReadOnlyList<JsonElement> Tools,
    bool Store,
    bool? Stream,
    string? ToolChoice,
    bool? ParallelToolCalls,
    string? ServiceTier,
    string? ReasoningEffort,
    string? ReasoningSummary,
    string? TextVerbosity,
    JsonElement? OutputSchema)
{
    /// <summary>
    /// 是否显式请求 provider reasoning / thinking 能力。
    /// Whether provider reasoning/thinking should be explicitly requested.
    /// </summary>
    public bool? ReasoningEnabled { get; init; }

    /// <summary>
    /// thinking budget 建议值；仅由支持该概念的 adapter 映射。
    /// Suggested thinking budget; mapped only by adapters that support it.
    /// </summary>
    public int? ReasoningBudgetTokens { get; init; }
}

/// <summary>
/// Provider 生成的 Responses 请求组合结果。
/// Provider-generated Responses request composition.
/// </summary>
public sealed record ProviderResponsesRequestComposition(
    IReadOnlyDictionary<string, object?> TransportPayload,
    IReadOnlyList<JsonElement> Input,
    string? InputPropertyName = "input")
{
    /// <summary>
    /// 生成 HTTP `/responses` 请求体。
    /// Creates the HTTP `/responses` request payload.
    /// </summary>
    public Dictionary<string, object?> CreateHttpPayload()
    {
        var payload = new Dictionary<string, object?>(TransportPayload, StringComparer.Ordinal)
        { };

        if (!string.IsNullOrWhiteSpace(InputPropertyName))
        {
            payload[InputPropertyName] = Input;
        }

        return payload;
    }
}

/// <summary>
/// Provider-specific Responses request composer。
/// Provider-specific composer for Responses request payloads.
/// </summary>
public interface IProviderResponsesRequestComposer
{
    /// <summary>
    /// 该 composer 对应的 wire API 标识。
    /// Wire API identifier owned by this composer.
    /// </summary>
    string WireApi { get; }

    /// <summary>
    /// 组装 provider-specific Responses 请求。
    /// Composes a provider-specific Responses request.
    /// </summary>
    ProviderResponsesRequestComposition Compose(ProviderResponsesRequestComposerContext context);
}
