using System.Text.Json;
using TianShu.AppHost.Tools;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses request composition 运行时，负责组装 provider request、transport 绑定和 request 日志负载。
/// Runtime that composes provider requests, transport bindings, and request log payloads.
/// </summary>
internal sealed class KernelResponsesRequestCompositionRuntime
{
    private static readonly TimeSpan DefaultResponsesStreamIdleTimeout = TimeSpan.FromMinutes(5);

    private readonly KernelToolRegistry toolRegistry;
    private readonly Func<TurnRequestContext, CancellationToken, Task<KernelResponsesNativeToolOptions>> resolveResponsesNativeToolOptionsAsync;
    private readonly Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly int responsesStreamMaxRetries;
    private readonly TimeSpan responsesStreamIdleTimeout;

    public KernelResponsesRequestCompositionRuntime(
        KernelToolRegistry toolRegistry,
        Func<TurnRequestContext, CancellationToken, Task<KernelResponsesNativeToolOptions>> resolveResponsesNativeToolOptionsAsync,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
        JsonSerializerOptions jsonOptions,
        int responsesStreamMaxRetries,
        TimeSpan responsesStreamIdleTimeout)
    {
        this.toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        this.resolveResponsesNativeToolOptionsAsync = resolveResponsesNativeToolOptionsAsync
                                                      ?? throw new ArgumentNullException(nameof(resolveResponsesNativeToolOptionsAsync));
        this.persistTurnLogAsync = persistTurnLogAsync ?? throw new ArgumentNullException(nameof(persistTurnLogAsync));
        this.jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        this.responsesStreamMaxRetries = Math.Max(responsesStreamMaxRetries, 0);
        this.responsesStreamIdleTimeout = responsesStreamIdleTimeout > TimeSpan.Zero
            ? responsesStreamIdleTimeout
            : DefaultResponsesStreamIdleTimeout;
    }

    public async Task<KernelResponsesProviderRequest> ComposeAsync(
        TurnOperationState state,
        TurnRequestContext context,
        IReadOnlyList<object> requestInput,
        string model,
        string baseUrl,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var transportProtocolBinding = ProviderResponsesTransportProtocolBindings.Resolve(
            context.ProviderWireApi,
            "turn context providerWireApi");
        var transportRetryStrategy = ProviderResponsesTransportRetryStrategies.Resolve(
            context.ProviderWireApi,
            "turn context providerWireApi");
        var requestComposer = ProviderResponsesRequestComposers.Resolve(
            context.ProviderWireApi,
            "turn context providerWireApi");
        var transportSettings = ResolveTransportSettings(context);
        var nativeToolOptions = await resolveResponsesNativeToolOptionsAsync(context, cancellationToken).ConfigureAwait(false);
        var tools = toolRegistry.BuildProviderResponsesToolList(
            context.DynamicTools,
            nativeToolOptions,
            providerWireApi: context.ProviderWireApi);
        var requestComposition = requestComposer.Compose(
            new ProviderResponsesRequestComposerContext(
                Model: model,
                Instructions: KernelTurnExecutionRuntimeHelpers.ResolveTurnInstructions(context),
                Input: SerializeToJsonElements(requestInput),
                Tools: SerializeToJsonElements(tools),
                Store: false,
                Stream: true,
                ToolChoice: "auto",
                ParallelToolCalls: ProviderModelCatalogs.SupportsParallelToolCalls(model),
                ServiceTier: context.ServiceTier?.Value,
                ReasoningEffort: ResolveReasoningEffort(context, model),
                ReasoningSummary: ResolveReasoningSummary(context, model),
                TextVerbosity: ResolveTextVerbosity(context, model),
                OutputSchema: context.OutputSchema?.ToJsonElement()));
        var turnMetadataHeader = BuildTurnMetadataHeader(state, context);
        var endpoint = transportProtocolBinding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                apiKey,
                state.StickyTurnState,
                turnMetadataHeader,
                ProviderResponsesTransportHttpRequestKind.StreamRequest,
                model)).Endpoint;

        return new KernelResponsesProviderRequest(
            Model: model,
            BaseUrl: baseUrl,
            ApiKey: apiKey,
            RequestComposition: requestComposition,
            TurnMetadataHeader: turnMetadataHeader,
            TransportSettings: transportSettings,
            TransportProtocolBinding: transportProtocolBinding,
            TransportRetryStrategy: transportRetryStrategy,
            ToolNames: DescribeResponsesToolNames(tools),
            NativeToolOptions: nativeToolOptions,
            Endpoint: endpoint);
    }

    public Task PersistRequestLogAsync(
        TurnOperationState state,
        int requestSequence,
        KernelResponsesProviderRequest request,
        CancellationToken cancellationToken)
        => persistTurnLogAsync(
            state.ThreadId,
            state.TurnId,
            "turn.responses.request",
            "inProgress",
            $"responses request #{requestSequence} tools={request.ToolNames.Count}",
            new
            {
                threadId = state.ThreadId,
                turnId = state.TurnId,
                requestSequence,
                model = request.Model,
                endpoint = request.Endpoint,
                toolNames = request.ToolNames,
                nativeToolOptions = new
                {
                    request.NativeToolOptions.WebSearchMode,
                    request.NativeToolOptions.ImageGenerationEnabled,
                    request.NativeToolOptions.ArtifactToolEnabled,
                    request.NativeToolOptions.McpResourceToolsEnabled,
                    request.NativeToolOptions.SearchToolEnabled,
                    request.NativeToolOptions.ToolSuggestEnabled,
                    request.NativeToolOptions.CodeModeEnabled,
                    request.NativeToolOptions.CodeModeEnabledToolNames,
                    request.NativeToolOptions.MultiAgentEnabled,
                    request.NativeToolOptions.FanoutEnabled,
                    request.NativeToolOptions.AgentJobWorkerToolsEnabled,
                },
            },
            cancellationToken);

    private ResponsesTransportSettings ResolveTransportSettings(TurnRequestContext context)
    {
        var requestMaxRetries = context.ProviderRequestMaxRetries ?? context.ProviderStreamMaxRetries ?? responsesStreamMaxRetries;
        var streamMaxRetries = context.ProviderStreamMaxRetries ?? responsesStreamMaxRetries;
        var streamIdleTimeout = responsesStreamIdleTimeout;
        if (context.ProviderStreamIdleTimeoutMs is > 0 and var timeoutMs)
        {
            streamIdleTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        return new ResponsesTransportSettings(
            RequestMaxRetries: Math.Max(requestMaxRetries, 0),
            StreamMaxRetries: Math.Max(streamMaxRetries, 0),
            StreamIdleTimeout: streamIdleTimeout > TimeSpan.Zero ? streamIdleTimeout : DefaultResponsesStreamIdleTimeout,
            WebsocketConnectTimeout: context.ProviderWebsocketConnectTimeoutMs is > 0 and var websocketTimeoutMs
                ? TimeSpan.FromMilliseconds(websocketTimeoutMs)
                : TimeSpan.FromMilliseconds(15000),
            SupportsWebsockets: context.ProviderSupportsWebsockets == true);
    }

    private string? BuildTurnMetadataHeader(TurnOperationState state, TurnRequestContext context)
    {
        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["turn_id"] = state.TurnId,
        };

        var sandbox = Normalize(context.SandboxMode);
        if (!string.IsNullOrWhiteSpace(sandbox))
        {
            metadata["sandbox"] = sandbox;
        }

        return JsonSerializer.Serialize(metadata, jsonOptions);
    }

    private static string? ResolveReasoningEffort(TurnRequestContext context, string model)
        => Normalize(context.CollaborationMode?.Settings.ReasoningEffort)
           ?? ProviderModelCatalogs.GetDefaultReasoningEffort(model);

    private static string? ResolveReasoningSummary(TurnRequestContext context, string model)
    {
        if (!ProviderModelCatalogs.SupportsReasoningSummaries(model))
        {
            return null;
        }

        return Normalize(context.ReasoningSummary)
               ?? ProviderModelCatalogs.GetDefaultReasoningSummary(model);
    }

    private static string? ResolveTextVerbosity(TurnRequestContext context, string model)
    {
        if (!ProviderModelCatalogs.SupportsVerbosity(model))
        {
            return null;
        }

        return Normalize(context.Verbosity)
               ?? ProviderModelCatalogs.GetDefaultVerbosity(model);
    }

    private static IReadOnlyList<JsonElement> SerializeToJsonElements(IEnumerable<object> items)
        => items.Select(static item => JsonSerializer.SerializeToElement(item)).ToArray();

    private static IReadOnlyList<string> DescribeResponsesToolNames(IReadOnlyList<object> tools)
    {
        if (tools.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var name = ReadResponsesToolName(tool);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = names.ToArray();
        Array.Sort(result, StringComparer.Ordinal);
        return result;
    }

    private static string? ReadResponsesToolName(object tool)
    {
        if (tool is JsonElement element)
        {
            return NormalizeToolString(ReadJsonString(element, "name")) ?? NormalizeToolString(ReadJsonString(element, "type"));
        }

        if (tool is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return ReadToolNameFromDictionary(readOnlyDictionary);
        }

        if (tool is IDictionary<string, object?> dictionary)
        {
            return ReadToolNameFromDictionary(dictionary);
        }

        var serialized = JsonSerializer.SerializeToElement(tool);
        return serialized.ValueKind == JsonValueKind.Object
            ? NormalizeToolString(ReadJsonString(serialized, "name")) ?? NormalizeToolString(ReadJsonString(serialized, "type"))
            : null;
    }

    private static string? ReadToolNameFromDictionary(IReadOnlyDictionary<string, object?> tool)
        => ReadToolStringValue(tool, "name") ?? ReadToolStringValue(tool, "type");

    private static string? ReadToolNameFromDictionary(IDictionary<string, object?> tool)
        => ReadToolStringValue(tool, "name") ?? ReadToolStringValue(tool, "type");

    private static string? ReadToolStringValue(IReadOnlyDictionary<string, object?> tool, string key)
        => tool.TryGetValue(key, out var value)
            ? NormalizeToolString(value switch
            {
                string text => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                _ => value?.ToString(),
            })
            : null;

    private static string? ReadToolStringValue(IDictionary<string, object?> tool, string key)
        => tool.TryGetValue(key, out var value)
            ? NormalizeToolString(value switch
            {
                string text => text,
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                _ => value?.ToString(),
            })
            : null;

    private static string? ReadJsonString(JsonElement json, string propertyName)
        => json.ValueKind == JsonValueKind.Object
           && json.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? NormalizeToolString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record KernelResponsesProviderRequest(
    string Model,
    string BaseUrl,
    string ApiKey,
    ProviderResponsesRequestComposition RequestComposition,
    string? TurnMetadataHeader,
    ResponsesTransportSettings TransportSettings,
    IProviderResponsesTransportProtocolBinding TransportProtocolBinding,
    IProviderResponsesTransportRetryStrategy TransportRetryStrategy,
    IReadOnlyList<string> ToolNames,
    KernelResponsesNativeToolOptions NativeToolOptions,
    string Endpoint);
