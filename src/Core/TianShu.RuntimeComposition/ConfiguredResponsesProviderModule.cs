using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TianShu.Configuration;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Provider;
using TianShu.Contracts.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.RuntimeComposition;

/// <summary>
/// 基于已解析配置的生产 Provider Module 适配器。
/// Production provider-module adapter based on the resolved configuration snapshot.
/// </summary>
public sealed class ConfiguredResponsesProviderModule : IProviderModule, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ResolvedTianShuConfig config;
    private readonly IReadOnlyList<ToolDescriptor> toolDescriptors;
    private readonly HttpClient httpClient;
    private readonly Func<string, string?> readEnvironmentVariable;
    private readonly bool ownsHttpClient;

    public ConfiguredResponsesProviderModule(
        string providerModuleId,
        ResolvedTianShuConfig config,
        IReadOnlyList<ToolDescriptor>? toolDescriptors = null,
        HttpMessageHandler? httpHandler = null,
        Func<string, string?>? readEnvironmentVariable = null,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerModuleId);
        if (httpHandler is not null && httpClient is not null)
        {
            throw new ArgumentException("不能同时传入 HttpMessageHandler 与 HttpClient。");
        }

        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.toolDescriptors = toolDescriptors ?? Array.Empty<ToolDescriptor>();
        this.readEnvironmentVariable = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var protocol = ResolveProtocolKind(config.ProviderWireApi ?? config.ProtocolAdapter);
        Descriptor = new ProviderDescriptor(
            providerModuleId,
            ResolveProviderDisplayName(config),
            protocol,
            new ProviderCapabilityProfile(SupportsStreaming: true, SupportsTools: this.toolDescriptors.Count > 0),
            string.IsNullOrWhiteSpace(config.Model) ? [] : [new TianShu.Contracts.Provider.ProviderModelDescriptor(config.Model!)],
            string.IsNullOrWhiteSpace(config.ProviderBaseUrl)
                ? null
                : new ProviderEndpointDescriptor(providerModuleId, protocol, config.ProviderBaseUrl!, config.ProviderEnvKey),
            metadata: new MetadataBag(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["providerKey"] = StructuredValue.FromString(Normalize(config.ModelProvider, "default")),
                ["wireApi"] = StructuredValue.FromString(Normalize(config.ProviderWireApi ?? config.ProtocolAdapter, ProviderWireApi.Responses)),
            }));
        if (httpClient is not null)
        {
            this.httpClient = httpClient;
            ownsHttpClient = false;
        }
        else
        {
            this.httpClient = httpHandler is null ? new HttpClient() : new HttpClient(httpHandler, disposeHandler: false);
            ownsHttpClient = true;
        }
    }

    public ProviderDescriptor Descriptor { get; }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    public async IAsyncEnumerable<ProviderStreamEvent> InvokeAsync(
        ProviderInvocationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationFailure = ValidateConfiguration();
        if (validationFailure is not null)
        {
            yield return new ProviderFailureEvent(validationFailure);
            yield break;
        }

        var wireApi = Normalize(config.ProviderWireApi ?? config.ProtocolAdapter, ProviderWireApi.Responses);
        ProviderResponsesRequestComposition? composition = null;
        ProviderResponsesTransportHttpRequestBinding? binding = null;
        ProviderFailure? compositionFailure = null;
        try
        {
            var input = BuildProviderInput(request);
            var tools = BuildProviderTools(wireApi);
            var composer = ProviderResponsesRequestComposers.Resolve(wireApi, "kernel runtime turn loop provider module");
            composition = composer.Compose(new ProviderResponsesRequestComposerContext(
                ResolveModel(request),
                BuildInstructions(),
                input,
                tools,
                Store: false,
                Stream: true,
                ToolChoice: tools.Count > 0 ? "auto" : null,
                ParallelToolCalls: false,
                ServiceTier: Normalize(config.ServiceTier),
                ReasoningEffort: Normalize(config.ModelReasoningEffort),
                ReasoningSummary: Normalize(config.ModelReasoningSummary),
                TextVerbosity: Normalize(config.ModelVerbosity),
                OutputSchema: null)
            {
                ReasoningEnabled = config.ModelReasoningEnabled,
                ReasoningBudgetTokens = config.ModelReasoningBudgetTokens is > 0 and <= int.MaxValue
                    ? (int)config.ModelReasoningBudgetTokens.Value
                    : null,
            });

            var transport = ProviderResponsesTransportProtocolBindings.Resolve(wireApi, "kernel runtime turn loop provider module");
            binding = transport.CreateHttpRequestBinding(
                config.ProviderBaseUrl!,
                new ProviderResponsesTransportHttpRequestContext(
                    ReadApiKey(),
                    request.PreviousTurnState?.ProviderTurnId,
                    BuildTurnMetadataHeader(request),
                    ProviderResponsesTransportHttpRequestKind.StreamRequest,
                    ResolveModel(request),
                    BuildTraceParent(request)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException)
        {
            compositionFailure = new ProviderFailure(
                "provider_request_composition_failed",
                ex.Message,
                isRetryable: false,
                additionalDetails: $"{ex.GetType().FullName}: {ex.Message}");
        }

        if (compositionFailure is not null || composition is null || binding is null)
        {
            yield return new ProviderFailureEvent(compositionFailure ?? new ProviderFailure(
                "provider_request_composition_failed",
                "Provider 请求组装未生成有效 transport binding。",
                isRetryable: false));
            yield break;
        }

        var httpPayload = composition.CreateHttpPayload();
        yield return new ProviderToolSurfaceEvent(ExtractProviderToolNames(httpPayload), wireApi);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, binding.Endpoint);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(httpPayload, JsonOptions),
            Encoding.UTF8,
            "application/json");
        foreach (var header in binding.Headers)
        {
            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            yield return new ProviderFailureEvent(new ProviderFailure(
                "provider_http_request_failed",
                $"Provider HTTP 请求失败：{(int)response.StatusCode} {response.ReasonPhrase}",
                isRetryable: IsRetryableStatusCode((int)response.StatusCode),
                additionalDetails: Truncate(body, 2048)));
            yield break;
        }

        var sawCompletion = false;
        var outputText = new StringBuilder();
        await foreach (var streamEvent in ReadSseEventsAsync(response, outputText, cancellationToken).ConfigureAwait(false))
        {
            if (streamEvent is ProviderTextDeltaEvent textDelta)
            {
                outputText.Append(textDelta.TextDelta);
            }

            if (streamEvent is ProviderCompletionEvent)
            {
                sawCompletion = true;
            }

            yield return streamEvent;
        }

        if (!sawCompletion)
        {
            yield return new ProviderCompletionEvent(new ProviderCompletion(
                outputText.Length == 0 ? "(provider stream completed without text)" : outputText.ToString()));
        }
    }

    private ProviderFailure? ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(config.Model))
        {
            return ConfigurationFailure("provider_model_missing", "配置缺少 model。");
        }

        if (string.IsNullOrWhiteSpace(config.ModelProvider))
        {
            return ConfigurationFailure("provider_key_missing", "配置缺少 provider。");
        }

        if (string.IsNullOrWhiteSpace(config.ProviderBaseUrl))
        {
            return ConfigurationFailure("provider_base_url_missing", "配置缺少 provider base_url。");
        }

        if (string.IsNullOrWhiteSpace(config.ProviderEnvKey))
        {
            return ConfigurationFailure("provider_api_key_env_missing", "配置缺少 provider api_key_env。");
        }

        if (string.IsNullOrWhiteSpace(readEnvironmentVariable(config.ProviderEnvKey)))
        {
            return ConfigurationFailure("provider_api_key_missing", $"环境变量 `{config.ProviderEnvKey}` 未设置。");
        }

        return null;
    }

    private static ProviderFailure ConfigurationFailure(string code, string message)
        => new(code, message, isRetryable: false);

    private IReadOnlyList<JsonElement> BuildProviderInput(ProviderInvocationRequest request)
    {
        List<JsonElement> input = [];
        if (!string.IsNullOrWhiteSpace(request.Conversation.SystemPrompt))
        {
            input.Add(BuildMessage("system", request.Conversation.SystemPrompt!));
        }

        foreach (var item in request.Conversation.History)
        {
            AppendInputItem(input, item);
        }

        foreach (var item in request.Inputs)
        {
            AppendInputItem(input, item);
        }

        return input.Count == 0 ? [BuildMessage("user", string.Empty)] : input;
    }

    private static void AppendInputItem(ICollection<JsonElement> input, ProviderInputItem item)
    {
        switch (item)
        {
            case TextProviderInputItem text:
                input.Add(BuildMessage("user", text.Text));
                break;
            case ToolCallProviderInputItem toolCall:
                input.Add(BuildFunctionCall(toolCall));
                break;
            case ToolResultProviderInputItem toolResult:
                input.Add(JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = toolResult.CallId.Value,
                    ["output"] = JsonSerializer.Serialize(toolResult.Result, JsonOptions),
                }, JsonOptions));
                break;
        }
    }

    private static JsonElement BuildMessage(string role, string text)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "message",
            ["role"] = role,
            ["content"] = new object?[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "input_text",
                    ["text"] = text,
                },
            },
        }, JsonOptions);

    private static JsonElement BuildFunctionCall(ToolCallProviderInputItem toolCall)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "function_call",
            ["call_id"] = toolCall.CallId.Value,
            ["name"] = toolCall.ToolId,
            ["arguments"] = JsonSerializer.Serialize(toolCall.Arguments.ToPlainObject(), JsonOptions),
        };
        if (!string.IsNullOrWhiteSpace(toolCall.Content))
        {
            payload["content"] = toolCall.Content;
        }

        if (!string.IsNullOrWhiteSpace(toolCall.ReasoningContent))
        {
            payload["reasoning_content"] = toolCall.ReasoningContent;
        }

        return JsonSerializer.SerializeToElement(payload, JsonOptions);
    }

    private IReadOnlyList<JsonElement> BuildProviderTools(string wireApi)
    {
        if (toolDescriptors.Count == 0)
        {
            return [];
        }

        var definitions = toolDescriptors
            .Where(static descriptor => descriptor.InputSchema.HasValue)
            .Select(static descriptor => new ProviderResponsesFunctionToolDefinition(
                descriptor.ToolId,
                descriptor.Description,
                descriptor.InputSchema!.Value,
                descriptor.OutputSchema,
                strict: false))
            .Cast<ProviderResponsesToolDefinition>()
            .ToArray();
        if (definitions.Length == 0)
        {
            return [];
        }

        var builder = ProviderResponsesToolSurfaceBuilders.Resolve(wireApi, "kernel runtime turn loop provider module");
        return builder.Build(new ProviderResponsesToolSurfaceBuilderContext(definitions))
            .Select(static tool => JsonSerializer.SerializeToElement(tool, JsonOptions))
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractProviderToolNames(IReadOnlyDictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("tools", out var toolsValue) || toolsValue is null)
        {
            return Array.Empty<string>();
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(toolsValue, JsonOptions));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return document.RootElement
            .EnumerateArray()
            .Select(ReadProviderToolName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ReadProviderToolName(JsonElement tool)
    {
        if (tool.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (tool.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString();
        }

        if (tool.TryGetProperty("function", out var function)
            && function.ValueKind == JsonValueKind.Object
            && function.TryGetProperty("name", out var functionName)
            && functionName.ValueKind == JsonValueKind.String)
        {
            return functionName.GetString();
        }

        return null;
    }

    private async IAsyncEnumerable<ProviderStreamEvent> ReadSseEventsAsync(
        HttpResponseMessage response,
        StringBuilder outputText,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            foreach (var streamEvent in ProjectJsonEvent(json, outputText))
            {
                yield return streamEvent;
            }

            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        var chatToolCalls = new ChatToolCallAccumulator();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var payload = data.ToString().TrimEnd('\r', '\n');
                    data.Clear();
                    if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
                    {
                        yield break;
                    }

                    foreach (var streamEvent in ProjectJsonEvent(payload, outputText, chatToolCalls))
                    {
                        yield return streamEvent;
                    }
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                data.AppendLine(line["data:".Length..].TrimStart());
            }
        }
    }

    private static IReadOnlyList<ProviderStreamEvent> ProjectJsonEvent(
        string json,
        StringBuilder outputText,
        ChatToolCallAccumulator? chatToolCalls = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        List<ProviderStreamEvent> events = [];
        if (TryReadProviderFailure(root, out var failure))
        {
            events.Add(new ProviderFailureEvent(failure));
            return events;
        }

        if (TryReadTextDelta(root, out var textDelta))
        {
            events.Add(new ProviderTextDeltaEvent(textDelta));
        }

        foreach (var directive in ReadResponseToolDirectives(root))
        {
            events.Add(new ProviderToolDirectiveEvent(directive));
        }

        if (chatToolCalls is not null)
        {
            foreach (var directive in chatToolCalls.Project(root))
            {
                events.Add(new ProviderToolDirectiveEvent(directive));
            }
        }
        else
        {
            foreach (var directive in ReadChatToolDirectives(root))
            {
                events.Add(new ProviderToolDirectiveEvent(directive));
            }
        }

        if (TryReadCompletion(root, outputText.ToString(), out var completion))
        {
            events.Add(new ProviderCompletionEvent(completion));
        }

        return events;
    }

    private static bool TryReadProviderFailure(JsonElement root, out ProviderFailure failure)
    {
        failure = null!;
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = ReadString(error, "code") ?? "provider_error";
            var message = ReadString(error, "message") ?? "Provider returned an error.";
            failure = new ProviderFailure(code, message, isRetryable: false, additionalDetails: error.GetRawText());
            return true;
        }

        return false;
    }

    private static bool TryReadTextDelta(JsonElement root, out string textDelta)
    {
        textDelta = string.Empty;
        var eventType = ReadString(root, "type") ?? ReadString(root, "event");
        if (string.Equals(eventType, "response.output_text.delta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "content_block_delta", StringComparison.OrdinalIgnoreCase))
        {
            textDelta = ReadString(root, "delta")
                        ?? ReadString(root, "text")
                        ?? ReadString(root, "content")
                        ?? string.Empty;
            if (textDelta.Length == 0
                && root.TryGetProperty("delta", out var delta)
                && delta.ValueKind == JsonValueKind.Object)
            {
                textDelta = ReadString(delta, "text")
                            ?? ReadString(delta, "content")
                            ?? string.Empty;
            }

            return textDelta.Length > 0;
        }

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("delta", out var delta)
                    && ReadString(delta, "content") is { Length: > 0 } choiceDelta)
                {
                    textDelta = choiceDelta;
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<ProviderToolDirective> ReadResponseToolDirectives(JsonElement root)
    {
        if (TryReadResponseFunctionCall(root, out var responseDirective))
        {
            yield return responseDirective;
        }
    }

    private static IEnumerable<ProviderToolDirective> ReadChatToolDirectives(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta)
                    || !delta.TryGetProperty("tool_calls", out var toolCalls)
                    || toolCalls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    if (TryReadChatToolCall(toolCall, out var chatDirective))
                    {
                        yield return chatDirective;
                    }
                }
            }
        }
    }

    private static bool TryReadResponseFunctionCall(JsonElement root, out ProviderToolDirective directive)
    {
        directive = null!;
        JsonElement item;
        if (string.Equals(ReadString(root, "type"), "response.output_item.done", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("item", out var outputItem))
        {
            item = outputItem;
        }
        else
        {
            item = root;
        }

        if (!string.Equals(ReadString(item, "type"), "function_call", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = ReadString(item, "name");
        var callId = ReadString(item, "call_id") ?? ReadString(item, "id");
        var arguments = ReadString(item, "arguments") ?? "{}";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        directive = new ProviderToolDirective(new CallId(callId!), name!, ParseStructuredArguments(arguments));
        return true;
    }

    private static bool TryReadChatToolCall(JsonElement toolCall, out ProviderToolDirective directive)
    {
        directive = null!;
        var callId = ReadString(toolCall, "id");
        if (!toolCall.TryGetProperty("function", out var function))
        {
            return false;
        }

        var name = ReadString(function, "name");
        var arguments = ReadString(function, "arguments") ?? "{}";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(callId))
        {
            return false;
        }

        directive = new ProviderToolDirective(new CallId(callId!), name!, ParseStructuredArguments(arguments));
        return true;
    }

    private sealed class ChatToolCallAccumulator
    {
        private readonly Dictionary<int, PendingChatToolCall> pending = new();

        public IReadOnlyList<ProviderToolDirective> Project(JsonElement root)
        {
            Accumulate(root);
            if (!HasToolCallFinish(root))
            {
                return Array.Empty<ProviderToolDirective>();
            }

            var directives = pending
                .OrderBy(static pair => pair.Key)
                .Select(static pair => pair.Value)
                .Where(static call => !string.IsNullOrWhiteSpace(call.CallId)
                                      && !string.IsNullOrWhiteSpace(call.ToolName))
                .Select(static call => new ProviderToolDirective(
                    new CallId(call.CallId!),
                    call.ToolName!,
                    ParseStructuredArguments(call.Arguments.ToString())))
                .ToArray();
            pending.Clear();
            return directives;
        }

        private void Accumulate(JsonElement root)
        {
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta)
                    || !delta.TryGetProperty("tool_calls", out var toolCalls)
                    || toolCalls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var fallbackIndex = 0;
                foreach (var toolCall in toolCalls.EnumerateArray())
                {
                    var index = ReadInt(toolCall, "index") ?? fallbackIndex++;
                    if (!pending.TryGetValue(index, out var call))
                    {
                        call = new PendingChatToolCall();
                        pending[index] = call;
                    }

                    call.Apply(toolCall);
                }
            }
        }

        private bool HasToolCallFinish(JsonElement root)
        {
            if (pending.Count == 0
                || !root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return choices.EnumerateArray().Any(static choice =>
                !string.IsNullOrWhiteSpace(ReadString(choice, "finish_reason")));
        }
    }

    private sealed class PendingChatToolCall
    {
        public string? CallId { get; private set; }

        public string? ToolName { get; private set; }

        public StringBuilder Arguments { get; } = new();

        public void Apply(JsonElement toolCall)
        {
            CallId ??= ReadString(toolCall, "id");
            if (!toolCall.TryGetProperty("function", out var function))
            {
                return;
            }

            ToolName ??= ReadString(function, "name");
            if (ReadString(function, "arguments") is { } arguments)
            {
                Arguments.Append(arguments);
            }
        }
    }

    private static StructuredValue ParseStructuredArguments(string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);
            return StructuredValue.FromJsonElement(document.RootElement);
        }
        catch (JsonException)
        {
            return StructuredValue.FromPlainObject(new Dictionary<string, object?>
            {
                ["value"] = arguments,
            });
        }
    }

    private static bool TryReadCompletion(JsonElement root, string fallbackText, out ProviderCompletion completion)
    {
        completion = null!;
        var eventType = ReadString(root, "type") ?? ReadString(root, "event");
        JsonElement response = root;
        if (string.Equals(eventType, "response.completed", StringComparison.OrdinalIgnoreCase)
            && root.TryGetProperty("response", out var completedResponse))
        {
            response = completedResponse;
        }
        else if (!string.Equals(eventType, "response.completed", StringComparison.OrdinalIgnoreCase)
                 && root.TryGetProperty("choices", out var choices)
                 && choices.ValueKind == JsonValueKind.Array
                 && choices.EnumerateArray().Any(choice => !string.IsNullOrWhiteSpace(ReadString(choice, "finish_reason"))))
        {
            completion = new ProviderCompletion(string.IsNullOrWhiteSpace(fallbackText) ? "(chat completion finished)" : fallbackText);
            return true;
        }
        else if (!string.Equals(eventType, "message_stop", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(eventType, "response.completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var outputText = ReadOutputText(response);
        completion = new ProviderCompletion(
            string.IsNullOrWhiteSpace(outputText) ? (string.IsNullOrWhiteSpace(fallbackText) ? "(provider completed)" : fallbackText) : outputText,
            ReadUsage(response));
        return true;
    }

    private static string? ReadOutputText(JsonElement response)
    {
        if (ReadString(response, "output_text") is { Length: > 0 } outputText)
        {
            return outputText;
        }

        if (!response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> parts = [];
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var text = ReadString(contentItem, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text!);
                }
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
    }

    private static ProviderUsage? ReadUsage(JsonElement response)
    {
        if (!response.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var inputTokens = ReadInt(usage, "input_tokens") ?? ReadInt(usage, "prompt_tokens");
        var outputTokens = ReadInt(usage, "output_tokens") ?? ReadInt(usage, "completion_tokens");
        if (inputTokens is null || outputTokens is null)
        {
            return null;
        }

        int? reasoningTokens = null;
        if (usage.TryGetProperty("output_tokens_details", out var outputDetails)
            && outputDetails.ValueKind == JsonValueKind.Object)
        {
            reasoningTokens = ReadInt(outputDetails, "reasoning_tokens");
        }

        return new ProviderUsage(inputTokens.Value, outputTokens.Value, reasoningTokens);
    }

    private string ReadApiKey()
    {
        var apiKey = readEnvironmentVariable(config.ProviderEnvKey!);
        return string.IsNullOrWhiteSpace(apiKey)
            ? throw new InvalidOperationException($"环境变量 `{config.ProviderEnvKey}` 未设置。")
            : apiKey;
    }

    private string ResolveModel(ProviderInvocationRequest request)
        => string.Equals(request.Model, "default", StringComparison.OrdinalIgnoreCase)
            ? config.Model!
            : request.Model;

    private string BuildInstructions()
        => "You are TianShu's CLI kernel runtime loop. Use the available read-only tools when needed, then answer the user.";

    private static string BuildTurnMetadataHeader(ProviderInvocationRequest request)
        => JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["executionId"] = request.ExecutionId.Value,
            ["threadId"] = request.Conversation.ThreadId?.Value,
            ["turnId"] = request.Conversation.TurnId?.Value,
            ["runtimeStepId"] = request.InvocationContext?.RuntimeStepId,
            ["sourceGraphId"] = request.InvocationContext?.SourceGraphId,
            ["sourceStageId"] = request.InvocationContext?.SourceStageId,
        }, JsonOptions);

    private static string BuildTraceParent(ProviderInvocationRequest request)
    {
        var seed = string.Join(
            "|",
            request.ExecutionId.Value,
            request.InvocationContext?.RuntimeStepId ?? "provider-step-unknown",
            request.Conversation.ThreadId?.Value ?? "thread-unknown",
            request.Conversation.TurnId?.Value ?? "turn-unknown");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        return $"00-{hash[..32]}-{hash.Substring(32, 16)}-01";
    }

    private static ProviderProtocolKind ResolveProtocolKind(string? wireApi)
        => ProviderWireApi.NormalizeOrThrow(wireApi, "kernel runtime turn loop provider module") switch
        {
            ProviderWireApi.OpenAiChatCompletions => ProviderProtocolKind.OpenAiChatCompletions,
            ProviderWireApi.AnthropicMessages => ProviderProtocolKind.AnthropicMessages,
            ProviderWireApi.GoogleGenerative => ProviderProtocolKind.GoogleGenerative,
            _ => ProviderProtocolKind.OpenAiResponses,
        };

    private static string ResolveProviderDisplayName(ResolvedTianShuConfig config)
        => $"{Normalize(config.ModelProvider, "default")} ({Normalize(config.ProviderWireApi ?? config.ProtocolAdapter, ProviderWireApi.Responses)})";

    private static bool IsRetryableStatusCode(int statusCode)
        => statusCode is 408 or 409 or 425 or 429 or >= 500;

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string Normalize(string? value, string fallback)
        => Normalize(value) ?? fallback;

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out var intValue) ? intValue : null;
    }
}
