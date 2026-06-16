using System.Text;
using System.Text.Json;
using TianShu.Configuration;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Catalog;

/// <summary>
/// Provider 模型连通性探测器。
/// Provider model connectivity probe.
/// </summary>
public sealed class ProviderModelConnectivityProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 对已解析配置中的 provider 执行最小模型请求探测。
    /// Probes a resolved provider with minimal per-model requests.
    /// </summary>
    public async Task<ProviderModelConnectivityProbeResult> ProbeAsync(
        ResolvedTianShuConfig config,
        IReadOnlyList<string> models,
        ProviderModelConnectivityProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(models);

        options ??= new ProviderModelConnectivityProbeOptions();
        var providerId = Normalize(config.ModelProvider) ?? "openai";
        var protocol = Normalize(config.ProviderWireApi) ?? ProviderWireApi.Responses;
        var baseUrl = Normalize(config.ProviderBaseUrl);
        var apiKeyEnv = Normalize(config.ProviderEnvKey);
        var selectedModels = models
            .Select(Normalize)
            .Where(static model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(options.ModelLimit ?? models.Count, 1, Math.Max(models.Count, 1)))
            .Cast<string>()
            .ToArray();

        if (selectedModels.Length == 0)
        {
            selectedModels = Normalize(config.Model) is { } defaultModel ? [defaultModel] : [];
        }

        var validationFailure = ValidateConfiguration(baseUrl, protocol, out var normalizedProtocol);
        if (validationFailure is not null || selectedModels.Length == 0)
        {
            var reason = validationFailure ?? "没有可探测的模型。";
            return new ProviderModelConnectivityProbeResult(
                providerId,
                normalizedProtocol ?? protocol,
                baseUrl,
                apiKeyEnv,
                selectedModels.Select(model => ProviderModelConnectivityProbeItem.Failed(model, null, null, reason)).ToArray());
        }

        var requestComposer = ResolveRequestComposer(normalizedProtocol);
        var transportBinding = ResolveTransportBinding(normalizedProtocol);
        if (requestComposer is null || transportBinding is null)
        {
            return new ProviderModelConnectivityProbeResult(
                providerId,
                normalizedProtocol!,
                baseUrl,
                apiKeyEnv,
                selectedModels.Select(model => ProviderModelConnectivityProbeItem.Failed(model, null, null, $"protocol adapter 尚未实现：{normalizedProtocol}。")).ToArray());
        }

        if (string.IsNullOrWhiteSpace(apiKeyEnv))
        {
            return new ProviderModelConnectivityProbeResult(
                providerId,
                normalizedProtocol!,
                baseUrl,
                apiKeyEnv,
                selectedModels.Select(model => ProviderModelConnectivityProbeItem.Failed(model, null, null, "当前 provider 没有配置 api_key_env。")).ToArray());
        }

        var readEnvironmentVariable = options.ReadEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var apiKey = Normalize(readEnvironmentVariable(apiKeyEnv!));
        if (apiKey is null)
        {
            return new ProviderModelConnectivityProbeResult(
                providerId,
                normalizedProtocol!,
                baseUrl,
                apiKeyEnv,
                selectedModels.Select(model => ProviderModelConnectivityProbeItem.Failed(model, null, null, $"环境变量 `{apiKeyEnv}` 未设置或为空。")).ToArray());
        }

        using var client = options.HttpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(options.HttpMessageHandler, disposeHandler: false);
        client.Timeout = options.Timeout ?? DefaultTimeout;

        var items = new List<ProviderModelConnectivityProbeItem>(selectedModels.Length);
        foreach (var model in selectedModels)
        {
            items.Add(await ProbeModelAsync(
                    client,
                    requestComposer,
                    transportBinding,
                    baseUrl!,
                    apiKey,
                    model,
                    options.Prompt,
                    options.ReasoningEnabled ?? config.ModelReasoningEnabled,
                    Normalize(options.ReasoningEffort) ?? Normalize(config.ModelReasoningEffort),
                    Normalize(options.ReasoningSummary) ?? Normalize(config.ModelReasoningSummary),
                    Normalize(options.TextVerbosity) ?? Normalize(config.ModelVerbosity),
                    options.ReasoningBudgetTokens ?? (config.ModelReasoningBudgetTokens is > 0 and <= int.MaxValue
                        ? (int)config.ModelReasoningBudgetTokens.Value
                        : null),
                    options.Stream ?? true,
                    cancellationToken)
                .ConfigureAwait(false));
        }

        return new ProviderModelConnectivityProbeResult(providerId, normalizedProtocol!, baseUrl, apiKeyEnv, items);
    }

    private static string? ValidateConfiguration(
        string? baseUrl,
        string protocol,
        out string? normalizedProtocol)
    {
        normalizedProtocol = null;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "当前 provider 没有配置 base_url。";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            return $"当前 provider 的 base_url 不是绝对 URL：{baseUrl}";
        }

        try
        {
            normalizedProtocol = ProviderWireApi.NormalizeOrThrow(protocol, "provider protocol");
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    private static async Task<ProviderModelConnectivityProbeItem> ProbeModelAsync(
        HttpClient client,
        IProviderResponsesRequestComposer requestComposer,
        IProviderResponsesTransportProtocolBinding transportBinding,
        string baseUrl,
        string apiKey,
        string model,
        string? prompt,
        bool? reasoningEnabled,
        string? reasoningEffort,
        string? reasoningSummary,
        string? textVerbosity,
        int? reasoningBudgetTokens,
        bool stream,
        CancellationToken cancellationToken)
    {
        using var request = CreateFormalProbeRequest(
            requestComposer,
            transportBinding,
            baseUrl,
            apiKey,
            model,
            prompt,
            reasoningEnabled,
            reasoningEffort,
            reasoningSummary,
            textVerbosity,
            reasoningBudgetTokens,
            stream);
        var endpoint = request.RequestUri;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var (probeContent, _) = await ReadProbeContentSignalsAsync(response, apiKey, transportBinding.WireApi, cancellationToken)
                    .ConfigureAwait(false);
                if (!probeContent.HasUsableContent)
                {
                    return ProviderModelConnectivityProbeItem.Failed(
                        model,
                        endpoint?.AbsolutePath,
                        statusCode,
                        "HTTP 成功，但响应中没有可由当前 provider adapter 解析的文本内容。");
                }

                return ProviderModelConnectivityProbeItem.Success(
                    model,
                    endpoint?.AbsolutePath,
                    statusCode,
                    probeContent.HasText,
                    probeContent.HasReasoning);
            }

            var body = await ReadRedactedBodyAsync(response, apiKey, cancellationToken).ConfigureAwait(false);
            return ProviderModelConnectivityProbeItem.Failed(
                model,
                endpoint?.AbsolutePath,
                statusCode,
                string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : TruncateBodyForDisplay(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return ProviderModelConnectivityProbeItem.Failed(model, endpoint?.AbsolutePath, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IProviderResponsesRequestComposer? ResolveRequestComposer(string? protocol)
    {
        try
        {
            return ProviderResponsesRequestComposers.Resolve(protocol, "provider protocol");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("request composer", StringComparison.Ordinal)
                                                 || ex.Message.Contains("尚未绑定", StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static IProviderResponsesTransportProtocolBinding? ResolveTransportBinding(string? protocol)
    {
        try
        {
            return ProviderResponsesTransportProtocolBindings.Resolve(protocol, "provider protocol");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("transport protocol binding", StringComparison.Ordinal)
                                                 || ex.Message.Contains("尚未绑定", StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static HttpRequestMessage CreateFormalProbeRequest(
        IProviderResponsesRequestComposer requestComposer,
        IProviderResponsesTransportProtocolBinding transportBinding,
        string baseUrl,
        string apiKey,
        string model,
        string? prompt,
        bool? reasoningEnabled,
        string? reasoningEffort,
        string? reasoningSummary,
        string? textVerbosity,
        int? reasoningBudgetTokens,
        bool stream)
    {
        var composition = requestComposer.Compose(new ProviderResponsesRequestComposerContext(
            Model: model,
            Instructions: string.Empty,
            Input:
            [
                JsonSerializer.SerializeToElement(new
                {
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = NormalizePrompt(prompt),
                        },
                    },
                }),
            ],
            Tools: [],
            Store: false,
            Stream: stream,
            ToolChoice: null,
            ParallelToolCalls: null,
            ServiceTier: null,
            ReasoningEffort: IsReasoningDisabled(reasoningEnabled) ? null : reasoningEffort,
            ReasoningSummary: IsReasoningDisabled(reasoningEnabled) ? null : reasoningSummary,
            TextVerbosity: textVerbosity,
            OutputSchema: null)
        {
            ReasoningEnabled = reasoningEnabled,
            ReasoningBudgetTokens = IsReasoningDisabled(reasoningEnabled) ? null : reasoningBudgetTokens,
        });
        var requestBinding = transportBinding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                apiKey,
                StickyTurnState: null,
                TurnMetadataHeader: null,
                Kind: stream ? ProviderResponsesTransportHttpRequestKind.StreamRequest : ProviderResponsesTransportHttpRequestKind.JsonRequest,
                Model: model));
        var request = new HttpRequestMessage(HttpMethod.Post, requestBinding.Endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(composition.CreateHttpPayload(), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json"),
        };
        foreach (var header in requestBinding.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    private static string NormalizePrompt(string? prompt)
        => string.IsNullOrWhiteSpace(prompt?.Trim()) ? "hello" : prompt.Trim();

    private static async Task<(ProbeContentSignals Signals, string? Body)> ReadProbeContentSignalsAsync(
        HttpResponseMessage response,
        string apiKey,
        string protocol,
        CancellationToken cancellationToken)
    {
        if (IsEventStreamResponse(response))
        {
            return await ReadStreamingProbeContentSignalsAsync(response, apiKey, protocol, cancellationToken).ConfigureAwait(false);
        }

        var body = await ReadRedactedBodyAsync(response, apiKey, cancellationToken).ConfigureAwait(false);
        return (TryExtractProbeContent(body, protocol), body);
    }

    private static bool IsEventStreamResponse(HttpResponseMessage response)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return mediaType is not null
               && mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(ProbeContentSignals Signals, string? Body)> ReadStreamingProbeContentSignalsAsync(
        HttpResponseMessage response,
        string apiKey,
        string protocol,
        CancellationToken cancellationToken)
    {
        var bodyBuilder = new StringBuilder();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (bodyBuilder.Length < 4096)
            {
                bodyBuilder.AppendLine(line.Replace(apiKey, "<redacted>", StringComparison.Ordinal));
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (data.Length == 0 || string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var signals = TryExtractStreamingProbeContent(data, protocol);
            if (signals.HasUsableContent)
            {
                return (signals, bodyBuilder.ToString());
            }
        }

        var body = bodyBuilder.ToString();
        return (TryExtractProbeContent(body, protocol), body);
    }

    private static ProbeContentSignals TryExtractProbeContent(string? body, string protocol)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ProbeContentSignals.None;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return protocol switch
            {
                ProviderWireApi.OpenAiChatCompletions => ExtractChatCompletionsSignals(root),
                ProviderWireApi.Responses => ExtractResponsesSignals(root),
                ProviderWireApi.AnthropicMessages => ExtractAnthropicMessagesSignals(root),
                ProviderWireApi.GoogleGenerative => ExtractGoogleGenerativeSignals(root),
                _ => ExtractAnySignals(root),
            };
        }
        catch (JsonException)
        {
            return ProbeContentSignals.None;
        }
    }

    private static ProbeContentSignals TryExtractStreamingProbeContent(string data, string protocol)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            return protocol switch
            {
                ProviderWireApi.OpenAiChatCompletions => ExtractChatCompletionsStreamingSignals(root),
                ProviderWireApi.Responses => ExtractResponsesStreamingSignals(root),
                ProviderWireApi.AnthropicMessages => ExtractAnthropicMessagesStreamingSignals(root),
                ProviderWireApi.GoogleGenerative => ExtractGoogleGenerativeSignals(root),
                _ => ExtractAnySignals(root),
            };
        }
        catch (JsonException)
        {
            return ProbeContentSignals.None;
        }
    }

    private static bool TryExtractProbeText(string? body, string protocol, out string? text)
    {
        text = null;
        return TryExtractProbeContent(body, protocol).HasUsableContent;
    }

    private static ProbeContentSignals ExtractChatCompletionsStreamingSignals(JsonElement root)
    {
        var hasText = false;
        var hasReasoning = false;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                hasText |= TryReadTextProperty(delta, "content", out _);
                hasReasoning |= TryReadTextProperty(delta, "reasoning_content", out _)
                                || TryReadTextProperty(delta, "reasoning", out _);
            }
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractResponsesStreamingSignals(JsonElement root)
    {
        var rawType = ReadString(root, "type") ?? string.Empty;
        var hasText = rawType.Contains("text", StringComparison.OrdinalIgnoreCase)
                      && TryReadTextProperty(root, "delta", out _);
        var hasReasoning = rawType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                           || rawType.Contains("thinking", StringComparison.OrdinalIgnoreCase);
        if (root.TryGetProperty("item", out var item) && IsReasoningOutputItem(item))
        {
            hasReasoning = true;
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractAnthropicMessagesStreamingSignals(JsonElement root)
    {
        var hasText = false;
        var hasReasoning = false;
        if (root.TryGetProperty("content_block", out var contentBlock))
        {
            var type = ReadString(contentBlock, "type") ?? string.Empty;
            hasText |= type.Contains("text", StringComparison.OrdinalIgnoreCase);
            hasReasoning |= type.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                            || type.Contains("reasoning", StringComparison.OrdinalIgnoreCase);
        }

        if (root.TryGetProperty("delta", out var delta))
        {
            var deltaType = ReadString(delta, "type") ?? string.Empty;
            hasText |= deltaType.Contains("text", StringComparison.OrdinalIgnoreCase)
                       || TryReadTextProperty(delta, "text", out _);
            hasReasoning |= deltaType.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                            || deltaType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                            || TryReadTextProperty(delta, "thinking", out _)
                            || TryReadTextProperty(delta, "reasoning", out _);
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractChatCompletionsSignals(JsonElement root)
    {
        var hasText = TryExtractChatCompletionsVisibleText(root);
        var hasReasoning = false;
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message)
                    && (TryReadTextProperty(message, "reasoning_content", out _)
                        || TryReadTextProperty(message, "reasoning", out _)))
                {
                    hasReasoning = true;
                    break;
                }
            }
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static bool TryExtractChatCompletionsVisibleText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out var message)
                && TryReadTextProperty(message, "content", out _))
            {
                return true;
            }
        }

        return false;
    }

    private static ProbeContentSignals ExtractResponsesSignals(JsonElement root)
    {
        var hasText = TryExtractResponsesText(root, out _);
        var hasReasoning = false;
        if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (IsReasoningOutputItem(item))
                {
                    hasReasoning = true;
                    break;
                }
            }
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractAnthropicMessagesSignals(JsonElement root)
    {
        var hasText = TryExtractAnthropicMessagesText(root, out _);
        var hasReasoning = false;
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentItem in content.EnumerateArray())
            {
                if (ReadString(contentItem, "type") is { } type
                    && (type.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                        || type.Contains("reasoning", StringComparison.OrdinalIgnoreCase)))
                {
                    hasReasoning = true;
                    break;
                }
            }
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractGoogleGenerativeSignals(JsonElement root)
    {
        var hasText = TryExtractGoogleGenerativeText(root, out _);
        var hasReasoning = false;
        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array)
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("parts", out var parts)
                    || parts.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    if ((part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True)
                        || TryReadTextProperty(part, "thoughtSignature", out _)
                        || TryReadTextProperty(part, "thinking", out _)
                        || TryReadTextProperty(part, "reasoning", out _))
                    {
                        hasReasoning = true;
                        break;
                    }
                }
            }
        }

        return new ProbeContentSignals(hasText, hasReasoning);
    }

    private static ProbeContentSignals ExtractAnySignals(JsonElement root)
        => new(TryExtractAnyText(root, out _), false);

    private static bool TryExtractChatCompletionsText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out var message)
                && TryReadTextProperty(message, "content", out text))
            {
                return true;
            }

            if (choice.TryGetProperty("message", out message)
                && (TryReadTextProperty(message, "reasoning_content", out text)
                    || TryReadTextProperty(message, "reasoning", out text)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractResponsesText(JsonElement root, out string? text)
    {
        if (TryReadTextProperty(root, "output_text", out text))
        {
            return true;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (TryReadTextProperty(contentItem, "text", out text))
                {
                    return true;
                }
            }
        }

        text = null;
        return false;
    }

    private static bool TryExtractAnthropicMessagesText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var contentItem in content.EnumerateArray())
        {
            if (TryReadTextProperty(contentItem, "text", out text))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractGoogleGenerativeText(JsonElement root, out string? text)
    {
        text = null;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (TryReadTextProperty(part, "text", out text))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryExtractAnyText(JsonElement root, out string? text)
    {
        if (TryReadTextProperty(root, "text", out text)
            || TryReadTextProperty(root, "output_text", out text))
        {
            return true;
        }

        text = null;
        return false;
    }

    private static bool TryReadTextProperty(JsonElement element, string propertyName, out string? text)
    {
        text = null;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = Normalize(property.GetString());
        return text is not null;
    }

    private static bool IsReasoningOutputItem(JsonElement item)
    {
        var type = ReadString(item, "type");
        if (!string.IsNullOrWhiteSpace(type)
            && (type.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                || type.Contains("thinking", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!item.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var summaryItem in summary.EnumerateArray())
        {
            if (TryReadTextProperty(summaryItem, "text", out _))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? Normalize(value.GetString())
            : null;

    private static bool IsReasoningDisabled(bool? enabled)
        => enabled == false;

    private static async Task<string?> ReadRedactedBodyAsync(
        HttpResponseMessage response,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return body.Replace(apiKey, "<redacted>", StringComparison.Ordinal);
    }

    private static string TruncateBodyForDisplay(string body)
        => body.Length <= 500 ? body : body[..500];

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private readonly record struct ProbeContentSignals(bool HasText, bool HasReasoning)
    {
        public static ProbeContentSignals None => new(false, false);

        public bool HasUsableContent => HasText || HasReasoning;
    }
}

/// <summary>
/// Provider 模型连通性探测选项。
/// Provider model connectivity probe options.
/// </summary>
public sealed record ProviderModelConnectivityProbeOptions
{
    public int? ModelLimit { get; init; }

    public string? Prompt { get; init; }

    public TimeSpan? Timeout { get; init; }

    public bool? ReasoningEnabled { get; init; }

    public string? ReasoningEffort { get; init; }

    public string? ReasoningSummary { get; init; }

    public string? TextVerbosity { get; init; }

    public int? ReasoningBudgetTokens { get; init; }

    public bool? Stream { get; init; }

    public Func<string, string?>? ReadEnvironmentVariable { get; init; }

    public HttpMessageHandler? HttpMessageHandler { get; init; }
}

/// <summary>
/// Provider 模型连通性探测结果。
/// Provider model connectivity probe result.
/// </summary>
public sealed record ProviderModelConnectivityProbeResult(
    string ProviderId,
    string Protocol,
    string? BaseUrl,
    string? ApiKeyEnvironmentVariable,
    IReadOnlyList<ProviderModelConnectivityProbeItem> Items)
{
    public bool Succeeded => Items.Count > 0 && Items.All(static item => item.Succeeded);
}

/// <summary>
/// 单个模型连通性探测结果。
/// Per-model connectivity probe result.
/// </summary>
public sealed record ProviderModelConnectivityProbeItem(
    string Model,
    string? RequestPath,
    int? HttpStatusCode,
    bool Succeeded,
    bool HasText,
    bool HasReasoning,
    string? Reason)
{
    public static ProviderModelConnectivityProbeItem Success(string model, string? requestPath, int? httpStatusCode)
        => new(model, requestPath, httpStatusCode, true, true, false, null);

    public static ProviderModelConnectivityProbeItem Success(
        string model,
        string? requestPath,
        int? httpStatusCode,
        bool hasText,
        bool hasReasoning)
        => new(model, requestPath, httpStatusCode, true, hasText, hasReasoning, null);

    public static ProviderModelConnectivityProbeItem Failed(string model, string? requestPath, int? httpStatusCode, string? reason)
        => new(model, requestPath, httpStatusCode, false, false, false, reason);
}
