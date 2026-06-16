using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TianShu.Provider.Abstractions;

namespace TianShu.AppHost.Tools.Runtime;

/// <summary>
/// Responses WebSocket stream transport 运行时，负责 WebSocket 连接、预热、消息收发和 HTTP fallback。
/// Runtime that manages Responses WebSocket connections, warmup, message I/O, and HTTP fallback.
/// </summary>
internal sealed class KernelResponsesWebSocketStreamTransportRuntime
{
    private readonly JsonSerializerOptions jsonOptions;
    private readonly KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime;
    private readonly KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime;
    private readonly KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime;
    private readonly Func<KernelRuntimeThread, CancellationToken, Task> persistRuntimeThreadSessionSnapshotAsync;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<
        IAsyncEnumerable<string>,
        TurnOperationState,
        IProviderResponsesStreamChunkParser,
        CancellationToken,
        Task<ResponsesStreamResult>> processResponsesEventStreamAsync;
    private readonly TimeSpan responsesStreamRetryBaseDelay;
    private readonly TimeSpan maxResponsesStreamRetryDelay;

    public KernelResponsesWebSocketStreamTransportRuntime(
        JsonSerializerOptions jsonOptions,
        KernelProviderRequestDiagnosticsRuntime providerRequestDiagnosticsRuntime,
        KernelResponsesStreamFailureRuntime responsesStreamFailureRuntime,
        KernelResponsesHttpStreamTransportRuntime responsesHttpStreamTransportRuntime,
        Func<KernelRuntimeThread, CancellationToken, Task> persistRuntimeThreadSessionSnapshotAsync,
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
        this.jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        this.providerRequestDiagnosticsRuntime = providerRequestDiagnosticsRuntime ?? throw new ArgumentNullException(nameof(providerRequestDiagnosticsRuntime));
        this.responsesStreamFailureRuntime = responsesStreamFailureRuntime ?? throw new ArgumentNullException(nameof(responsesStreamFailureRuntime));
        this.responsesHttpStreamTransportRuntime = responsesHttpStreamTransportRuntime ?? throw new ArgumentNullException(nameof(responsesHttpStreamTransportRuntime));
        this.persistRuntimeThreadSessionSnapshotAsync = persistRuntimeThreadSessionSnapshotAsync ?? throw new ArgumentNullException(nameof(persistRuntimeThreadSessionSnapshotAsync));
        this.writeNotificationAsync = writeNotificationAsync ?? throw new ArgumentNullException(nameof(writeNotificationAsync));
        this.processResponsesEventStreamAsync = processResponsesEventStreamAsync ?? throw new ArgumentNullException(nameof(processResponsesEventStreamAsync));
        this.responsesStreamRetryBaseDelay = responsesStreamRetryBaseDelay;
        this.maxResponsesStreamRetryDelay = maxResponsesStreamRetryDelay;
    }

    public static bool CanUseTransport(
        KernelRuntimeThread runtimeThread,
        TurnRequestContext context,
        IProviderResponsesTransportProtocolBinding transportProtocolBinding)
    {
        if (runtimeThread.ProviderHttpFallbackEnabled)
        {
            return false;
        }

        if (context.ProviderSupportsWebsockets != true)
        {
            return false;
        }

        var wireApi = ProviderWireApi.NormalizeOrThrow(context.ProviderWireApi, "turn context providerWireApi");
        if (!string.IsNullOrWhiteSpace(wireApi)
            && !string.Equals(wireApi, transportProtocolBinding.WireApi, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public async Task PrewarmSessionAsync(
        KernelRuntimeThread runtimeThread,
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
        ResponsesWebSocketTurnSession session,
        CancellationToken cancellationToken)
    {
        if (runtimeThread.ProviderHttpFallbackEnabled || session.LastRequestInput is not null)
        {
            return;
        }

        for (var retryIndex = 0; ; retryIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await PrewarmSessionCoreAsync(
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
                    session,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var decision = transportRetryStrategy.EvaluateWebSocketRetry(
                    responsesStreamFailureRuntime.ClassifyWebSocketFailure(ex, cancellationToken),
                    retryIndex,
                    settings.StreamMaxRetries,
                    responsesStreamRetryBaseDelay,
                    maxResponsesStreamRetryDelay,
                    cancellationToken.IsCancellationRequested);
                if (!decision.ShouldSwitchToHttpTransport)
                {
                    if (!decision.ShouldRetry)
                    {
                        throw;
                    }

                    await session.ResetConnectionAsync().ConfigureAwait(false);
                    if (decision.Delay > TimeSpan.Zero)
                    {
                        await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                await EnableHttpFallbackAsync(runtimeThread, session).ConfigureAwait(false);
                return;
            }
        }
    }

    public async Task<ResponsesStreamResult> StreamWithFallbackAsync(
        KernelRuntimeThread runtimeThread,
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
        ResponsesWebSocketTurnSession session,
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
                    session,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                var decision = transportRetryStrategy.EvaluateWebSocketRetry(
                    responsesStreamFailureRuntime.ClassifyWebSocketFailure(ex, cancellationToken),
                    retryIndex,
                    settings.StreamMaxRetries,
                    responsesStreamRetryBaseDelay,
                    maxResponsesStreamRetryDelay,
                    cancellationToken.IsCancellationRequested);
                if (!decision.ShouldSwitchToHttpTransport)
                {
                    if (!decision.ShouldRetry)
                    {
                        throw;
                    }

                    await session.ResetConnectionAsync().ConfigureAwait(false);
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

                    continue;
                }

                await EnableHttpFallbackAsync(runtimeThread, session).ConfigureAwait(false);
                return await responsesHttpStreamTransportRuntime.StreamWithRetryAsync(
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
                    transportRetryStrategy,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ResponsesStreamResult> StreamAsync(
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
        ResponsesWebSocketTurnSession session,
        CancellationToken cancellationToken)
    {
        var socket = await EnsureConnectedAsync(
                baseUrl,
                apiKey,
                state,
                turnMetadataHeader,
                settings,
                transportProtocolBinding,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        var requestSignature = KernelResponsesTransportRuntimeHelpers.BuildResponsesWebSocketRequestSignature(
            requestComposition.TransportPayload,
            jsonOptions);
        var (effectiveInput, previousResponseId) = KernelResponsesTransportRuntimeHelpers.BuildResponsesWebSocketRequestInput(
            requestComposition.Input,
            requestSignature,
            session);
        var requestBinding = transportProtocolBinding.CreateWebSocketRequestBinding(
            new ProviderResponsesTransportWebSocketRequestContext(
                requestComposition.TransportPayload,
                effectiveInput,
                previousResponseId,
                turnMetadataHeader));
        var requestJson = JsonSerializer.Serialize(requestBinding.Payload, jsonOptions);
        await providerRequestDiagnosticsRuntime.CaptureAsync(
            state,
            requestBinding.Payload,
            requestSequence,
            model,
            provider,
            "websocket",
            "websocket",
            requestComposition.InputPropertyName,
            requestJson,
            cancellationToken).ConfigureAwait(false);
        await SendMessageAsync(socket, requestJson, cancellationToken).ConfigureAwait(false);

        var result = await processResponsesEventStreamAsync(
            EnumerateDataEventsAsync(socket, settings.StreamIdleTimeout, cancellationToken),
            state,
            ProviderResponsesStreamChunkParsers.Resolve(transportProtocolBinding.WireApi),
            cancellationToken).ConfigureAwait(false);

        CaptureSessionTurnState(session, requestComposition.Input, requestSignature, result);
        return result;
    }

    private async Task PrewarmSessionCoreAsync(
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
        ResponsesWebSocketTurnSession session,
        CancellationToken cancellationToken)
    {
        var socket = await EnsureConnectedAsync(
                baseUrl,
                apiKey,
                state,
                turnMetadataHeader,
                settings,
                transportProtocolBinding,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        var requestSignature = KernelResponsesTransportRuntimeHelpers.BuildResponsesWebSocketRequestSignature(
            requestComposition.TransportPayload,
            jsonOptions);
        var warmupPayload = new Dictionary<string, object?>(requestComposition.TransportPayload, StringComparer.Ordinal)
        {
            ["generate"] = false,
        };
        var requestBinding = transportProtocolBinding.CreateWebSocketRequestBinding(
            new ProviderResponsesTransportWebSocketRequestContext(
                warmupPayload,
                KernelResponsesTransportRuntimeHelpers.CloneJsonElements(requestComposition.Input),
                PreviousResponseId: null,
                turnMetadataHeader));
        var requestJson = JsonSerializer.Serialize(requestBinding.Payload, jsonOptions);
        await providerRequestDiagnosticsRuntime.CaptureAsync(
            state,
            requestBinding.Payload,
            requestSequence,
            model,
            provider,
            "websocket-warmup",
            "websocket",
            requestComposition.InputPropertyName,
            requestJson,
            cancellationToken).ConfigureAwait(false);
        await SendMessageAsync(socket, requestJson, cancellationToken).ConfigureAwait(false);

        var result = await processResponsesEventStreamAsync(
            EnumerateDataEventsAsync(socket, settings.StreamIdleTimeout, cancellationToken),
            state,
            ProviderResponsesStreamChunkParsers.Resolve(transportProtocolBinding.WireApi),
            cancellationToken).ConfigureAwait(false);

        CaptureSessionTurnState(session, requestComposition.Input, requestSignature, result);
    }

    private async Task<ClientWebSocket> EnsureConnectedAsync(
        string baseUrl,
        string apiKey,
        TurnOperationState state,
        string? turnMetadataHeader,
        ResponsesTransportSettings settings,
        IProviderResponsesTransportProtocolBinding transportProtocolBinding,
        ResponsesWebSocketTurnSession session,
        CancellationToken cancellationToken)
    {
        if (session.Socket is { State: WebSocketState.Open } socket)
        {
            return socket;
        }

        await session.ResetConnectionAsync().ConfigureAwait(false);

        var newSocket = new ClientWebSocket();
        newSocket.Options.CollectHttpResponseDetails = true;
        newSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        var connectionBinding = transportProtocolBinding.CreateWebSocketConnectionBinding(
            baseUrl,
            new ProviderResponsesTransportWebSocketConnectContext(
                apiKey,
                state.ThreadId,
                state.StickyTurnState,
                turnMetadataHeader));
        KernelResponsesTransportRuntimeHelpers.ApplyTransportHeaders(newSocket.Options, connectionBinding.Headers);

        KernelCustomCaSupport.ConfigureClientWebSocketOptions(newSocket.Options);
        try
        {
            try
            {
                await newSocket.ConnectAsync(connectionBinding.Endpoint, cancellationToken)
                    .WaitAsync(settings.WebsocketConnectTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new KernelResponsesStreamException("websocket connect timeout", isRetryable: true);
            }

            CaptureTurnState(transportProtocolBinding, newSocket, state);
            session.Attach(newSocket);
            return newSocket;
        }
        catch (Exception ex)
        {
            CaptureTurnState(transportProtocolBinding, newSocket, state);
            var isUpgradeRequired = newSocket.HttpStatusCode == HttpStatusCode.UpgradeRequired;
            newSocket.Dispose();
            if (isUpgradeRequired)
            {
                throw new KernelResponsesWebSocketUpgradeRequiredException(
                    "provider websocket upgrade returned 426 Upgrade Required",
                    ex);
            }

            throw;
        }
    }

    private async Task EnableHttpFallbackAsync(
        KernelRuntimeThread runtimeThread,
        ResponsesWebSocketTurnSession session)
    {
        runtimeThread.MarkProviderHttpFallbackEnabled();
        try
        {
            await persistRuntimeThreadSessionSnapshotAsync(runtimeThread, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        await session.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task SendMessageAsync(
        ClientWebSocket socket,
        string json,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<string> EnumerateDataEventsAsync(
        ClientWebSocket socket,
        TimeSpan idleTimeout,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReceiveMessageAsync(socket, idleTimeout, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                yield break;
            }

            yield return message;
        }
    }

    private static async Task<string?> ReceiveMessageAsync(
        ClientWebSocket socket,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken)
                    .WaitAsync(idleTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new KernelResponsesStreamException("websocket stream idle timeout", isRetryable: true);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void CaptureTurnState(
        IProviderResponsesTransportProtocolBinding transportProtocolBinding,
        ClientWebSocket socket,
        TurnOperationState state)
    {
        try
        {
            var responseHeaders = socket.HttpResponseHeaders;
            if (responseHeaders is null)
            {
                return;
            }

            var turnState = transportProtocolBinding.ReadStickyTurnState(
                KernelResponsesTransportRuntimeHelpers.CreateTransportResponseHeaders(responseHeaders));
            if (!string.IsNullOrWhiteSpace(turnState))
            {
                state.StickyTurnState = turnState;
            }
        }
        catch
        {
            // 某些连接失败路径不会暴露完整响应头，这里保持静默回退。
        }
    }

    private static void CaptureSessionTurnState(
        ResponsesWebSocketTurnSession session,
        IReadOnlyList<JsonElement> requestInput,
        string requestSignature,
        ResponsesStreamResult result)
    {
        session.LastRequestInput = KernelResponsesTransportRuntimeHelpers.CloneJsonElements(requestInput);
        session.LastRequestSignature = requestSignature;
        session.LastResponseId = result.ResponseId;
        session.LastResponseItems = SerializeToJsonElements(BuildFollowUpResponseItems(result.OutputItemsAdded, result.OutputItemsDone));
    }

    private static IReadOnlyList<object> BuildFollowUpResponseItems(
        IReadOnlyList<JsonElement> outputItemsAdded,
        IReadOnlyList<JsonElement> outputItemsDone)
    {
        var source = outputItemsDone.Count > 0 ? outputItemsDone : outputItemsAdded;
        var items = new List<object>(source.Count);
        foreach (var item in source)
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(item.Clone());
        }

        return items;
    }

    private static IReadOnlyList<JsonElement> SerializeToJsonElements(IEnumerable<object> items)
        => items.Select(static item => JsonSerializer.SerializeToElement(item)).ToArray();
}
