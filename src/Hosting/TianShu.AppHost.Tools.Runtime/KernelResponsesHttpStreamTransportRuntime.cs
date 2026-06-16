using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses HTTP stream transport 运行时，负责 HTTP stream request 发送、SSE 枚举和 stream retry 通知。
/// Runtime that sends HTTP response stream requests, enumerates SSE events, and publishes stream retry notifications.
/// </summary>
internal sealed class KernelResponsesHttpStreamTransportRuntime
{
    private readonly HttpClient providerHttpClient;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly Action<HttpRequestMessage> applyW3cTraceContext;
    private readonly KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime;
    private readonly KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<
        IAsyncEnumerable<string>,
        TurnOperationState,
        IProviderResponsesStreamChunkParser,
        CancellationToken,
        Task<ResponsesStreamResult>> processResponsesEventStreamAsync;
    private readonly TimeSpan responsesStreamRetryBaseDelay;
    private readonly TimeSpan maxResponsesStreamRetryDelay;

    public KernelResponsesHttpStreamTransportRuntime(
        HttpClient providerHttpClient,
        JsonSerializerOptions jsonOptions,
        Action<HttpRequestMessage> applyW3cTraceContext,
        KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime,
        KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<
            IAsyncEnumerable<string>,
            TurnOperationState,
            IProviderResponsesStreamChunkParser,
            CancellationToken,
            Task<ResponsesStreamResult>> processResponsesEventStreamAsync,
        TimeSpan responsesStreamRetryBaseDelay,
        TimeSpan maxResponsesStreamRetryDelay)
    {
        this.providerHttpClient = providerHttpClient ?? throw new ArgumentNullException(nameof(providerHttpClient));
        this.jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        this.applyW3cTraceContext = applyW3cTraceContext ?? throw new ArgumentNullException(nameof(applyW3cTraceContext));
        this.providerRequestDiagnosticsRuntime = providerRequestDiagnosticsRuntime ?? throw new ArgumentNullException(nameof(providerRequestDiagnosticsRuntime));
        this.responsesStreamFailureRuntime = responsesStreamFailureRuntime ?? throw new ArgumentNullException(nameof(responsesStreamFailureRuntime));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        this.processResponsesEventStreamAsync = processResponsesEventStreamAsync ?? throw new ArgumentNullException(nameof(processResponsesEventStreamAsync));
        this.responsesStreamRetryBaseDelay = responsesStreamRetryBaseDelay;
        this.maxResponsesStreamRetryDelay = maxResponsesStreamRetryDelay;
    }

    public async Task<ResponsesStreamResult> StreamWithRetryAsync(
        string baseUrl,
        string apiKey,
        string model,
        string? provider,
        int requestSequence,
        ProviderResponsesRequestComposition requestComposition,
        TurnOperationState state,
        string? turnMetadataHeader,
        ResponsesTransportSettings settings,
        IProviderResponsesTransportProtocolBinding transportProtocolBinding,
        IProviderResponsesTransportRetryStrategy transportRetryStrategy,
        CancellationToken cancellationToken)
    {
        for (var retryIndex = 0; ; retryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await StreamAsync(
                    baseUrl,
                    apiKey,
                    model,
                    provider,
                    requestSequence,
                    requestComposition,
                    state,
                    turnMetadataHeader,
                    settings,
                    transportProtocolBinding,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var decision = transportRetryStrategy.EvaluateHttpStreamRetry(
                    responsesStreamFailureRuntime.ClassifyHttpStreamFailure(ex, cancellationToken),
                    retryIndex,
                    settings.RequestMaxRetries,
                    responsesStreamRetryBaseDelay,
                    maxResponsesStreamRetryDelay,
                    cancellationToken.IsCancellationRequested);
                if (!decision.ShouldRetry)
                {
                    throw;
                }

                await writeNotificationAsync("error", new
                {
                    threadId = state.ThreadId,
                    turnId = state.TurnId,
                    message = decision.RetryMessage,
                    error = new
                    {
                        message = ex.Message,
                    },
                    willRetry = true,
                }, CancellationToken.None).ConfigureAwait(false);

                if (decision.Delay > TimeSpan.Zero)
                {
                    await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public async Task<ResponsesStreamResult> StreamAsync(
        string baseUrl,
        string apiKey,
        string model,
        string? provider,
        int requestSequence,
        ProviderResponsesRequestComposition requestComposition,
        TurnOperationState state,
        string? turnMetadataHeader,
        ResponsesTransportSettings settings,
        IProviderResponsesTransportProtocolBinding transportProtocolBinding,
        CancellationToken cancellationToken)
    {
        var requestBinding = transportProtocolBinding.CreateHttpRequestBinding(
            baseUrl,
            new ProviderResponsesTransportHttpRequestContext(
                apiKey,
                state.StickyTurnState,
                turnMetadataHeader,
                ProviderResponsesTransportHttpRequestKind.StreamRequest,
                model));
        var httpPayload = requestComposition.CreateHttpPayload();
        var requestJson = JsonSerializer.Serialize(httpPayload, jsonOptions);
        await providerRequestDiagnosticsRuntime.CaptureAsync(
            state,
            httpPayload,
            requestSequence,
            model,
            provider,
            "http",
            "http",
            requestComposition.InputPropertyName,
            requestJson,
            cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestBinding.Endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };
        KernelResponsesTransportRuntimeHelpers.ApplyTransportHeaders(request.Headers, requestBinding.Headers);

        applyW3cTraceContext(request);

        using var response = await providerHttpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        var turnState = transportProtocolBinding.ReadStickyTurnState(
            KernelResponsesTransportRuntimeHelpers.CreateTransportResponseHeaders(response.Headers));
        if (!string.IsNullOrWhiteSpace(turnState))
        {
            state.StickyTurnState = turnState;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var brief = body.Length > 240 ? $"{body[..240]}..." : body;
            var message = $"模型请求失败：HTTP {(int)response.StatusCode} {response.StatusCode}，{brief}";
            if ((int)response.StatusCode >= 500)
            {
                throw new KernelResponsesStreamException(message, isRetryable: true);
            }

            throw new InvalidOperationException(message);
        }

        return await processResponsesEventStreamAsync(
            EnumerateSseDataEventsAsync(stream, settings.StreamIdleTimeout, cancellationToken),
            state,
            ProviderResponsesStreamChunkParsers.Resolve(transportProtocolBinding.WireApi),
            cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<string> EnumerateSseDataEventsAsync(
        Stream stream,
        TimeSpan idleTimeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var dataLines = new List<string>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line;
            try
            {
                line = await reader.ReadLineAsync().WaitAsync(idleTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new KernelResponsesStreamException(
                    BuildStreamIdleTimeoutMessage(idleTimeout),
                    isRetryable: true);
            }

            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    var data = string.Join("\n", dataLines);
                    dataLines.Clear();
                    if (!string.Equals(data, "[DONE]", StringComparison.Ordinal))
                    {
                        yield return data;
                    }
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLines.Add(line["data:".Length..].TrimStart());
            }
        }

        if (dataLines.Count > 0)
        {
            var data = string.Join("\n", dataLines);
            if (!string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                yield return data;
            }
        }
    }

    private static string BuildStreamIdleTimeoutMessage(TimeSpan idleTimeout)
        => $"responses stream idle timeout before response.completed after {idleTimeout:c}";
}
