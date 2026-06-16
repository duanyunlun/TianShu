using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.Contracts.Tools;
using TianShu.Execution.Runtime;

namespace TianShu.AppHost.Tools.Runtime;

internal sealed class KernelRealtimeAppHostRuntime
{
    private const string RealtimeV2TianShuToolName = "tianshu";
    private const string RealtimeAudioVoice = "fathom";
    private const int RealtimeAudioSampleRate = 24000;
    private const int RealtimeAudioChannels = 1;
    private static readonly TimeSpan RealtimeStartTimeout = TimeSpan.FromSeconds(10);

    private readonly KernelThreadStore threadStore;
    private readonly KernelThreadManager threadManager;
    private readonly Func<KernelThreadRecord, string, string, string, KernelRealtimeSessionState> buildConfiguredRealtimeSessionState;
    private readonly Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession;
    private readonly Func<string?, string?> normalize;
    private readonly Func<string, object, CancellationToken, Task> writeNotificationAsync;
    private readonly Func<JsonElement, object, CancellationToken, Task> writeResultAsync;
    private readonly Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync;

    public KernelRealtimeAppHostRuntime(
        KernelThreadStore threadStore,
        KernelThreadManager threadManager,
        Func<KernelThreadRecord, string, string, string, KernelRealtimeSessionState> buildConfiguredRealtimeSessionState,
        Func<KernelThreadRecord, KernelThreadSessionState> buildDefaultThreadSession,
        Func<string?, string?> normalize,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<JsonElement, object, CancellationToken, Task> writeResultAsync,
        Func<JsonElement?, int, string, CancellationToken, Task> writeErrorAsync)
    {
        this.threadStore = threadStore;
        this.threadManager = threadManager;
        this.buildConfiguredRealtimeSessionState = buildConfiguredRealtimeSessionState;
        this.buildDefaultThreadSession = buildDefaultThreadSession;
        this.normalize = normalize;
        this.writeNotificationAsync = writeNotificationAsync;
        this.writeResultAsync = writeResultAsync;
        this.writeErrorAsync = writeErrorAsync;
    }

    public async Task HandleThreadRealtimeStartAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteRealtimeErrorResponseAsync(id, string.Empty, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionId = Normalize(ReadString(@params, "sessionId"))
            ?? $"realtime_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}";
        var prompt = Normalize(ReadString(@params, "prompt")) ?? string.Empty;
        var session = buildConfiguredRealtimeSessionState(thread, threadId, sessionId, prompt);
        var runtimeThread = threadManager.GetOrAttachThread(thread, buildDefaultThreadSession, loaded: true);
        if (await TryStartRealtimeWebSocketAsync(id, runtimeThread, session, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        runtimeThread.SetRealtimeSession(session);
        await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        if (session.TryMarkStartedNotificationWritten())
        {
            await WriteNotificationAsync("thread/realtime/started", new
            {
                threadId,
                sessionId = session.SessionId,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleThreadRealtimeAppendAudioAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId"));
        var audio = TryReadObject(@params, "audio", out var audioObject) ? audioObject : default;
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteRealtimeErrorResponseAsync(id, string.Empty, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!threadManager.TryGetThread(threadId, out var runtimeThread)
            || runtimeThread?.RealtimeSession is not KernelRealtimeSessionState session)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程未启动实时会话：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionId = Normalize(ReadString(@params, "sessionId"));
        if (sessionId is not null && !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32600, "sessionId 与已启动会话不一致。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session.UsesRealtimeWebSocket)
        {
            var audioData = ReadString(audio, "data");
            if (string.IsNullOrWhiteSpace(audioData))
            {
                await WriteRealtimeErrorResponseAsync(id, threadId, -32602, "audio.data 不能为空。", cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                await SendRealtimeTransportMessageAsync(session, new
                {
                    type = "input_audio_buffer.append",
                    audio = audioData,
                }, cancellationToken).ConfigureAwait(false);
                await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteRealtimeErrorResponseAsync(id, threadId, -32603, $"追加实时音频失败：{ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var chunkIndex = session.AddAudio();
        await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);

        await WriteNotificationAsync("thread/realtime/itemAdded", new
        {
            threadId,
            item = new
            {
                type = "input_audio",
                sessionId = session.SessionId,
                chunkIndex,
            },
        }, cancellationToken).ConfigureAwait(false);

        await WriteNotificationAsync("thread/realtime/outputAudio/delta", new
        {
            threadId,
            audio = new
            {
                data = ReadString(audio, "data") ?? "AQID",
                sampleRate = ReadInt(audio, "sampleRate") ?? 24000,
                numChannels = ReadInt(audio, "numChannels") ?? 1,
                samplesPerChannel = ReadInt(audio, "samplesPerChannel"),
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleThreadRealtimeAppendTextAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId"));
        var text = ReadString(@params, "text");
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteRealtimeErrorResponseAsync(id, string.Empty, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!threadManager.TryGetThread(threadId, out var runtimeThread)
            || runtimeThread?.RealtimeSession is not KernelRealtimeSessionState session)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程未启动实时会话：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionId = Normalize(ReadString(@params, "sessionId"));
        if (sessionId is not null && !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32600, "sessionId 与已启动会话不一致。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (session.UsesRealtimeWebSocket)
        {
            var normalizedTextForTransport = Normalize(text);
            if (string.IsNullOrWhiteSpace(normalizedTextForTransport))
            {
                await WriteRealtimeErrorResponseAsync(id, threadId, -32602, "text 不能为空。", cancellationToken).ConfigureAwait(false);
                return;
            }

            try
            {
                var contentType = session.EventParser == KernelRealtimeEventParser.V1 ? "text" : "input_text";
                await SendRealtimeTransportMessageAsync(session, new
                {
                    type = "conversation.item.create",
                    item = new
                    {
                        type = "message",
                        role = "user",
                        content = new[]
                        {
                            new
                            {
                                type = contentType,
                                text = normalizedTextForTransport,
                            },
                        },
                    },
                }, cancellationToken).ConfigureAwait(false);
                await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteRealtimeErrorResponseAsync(id, threadId, -32603, $"追加实时文本失败：{ex.Message}", cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var normalizedText = session.AddText(text);
        await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);

        if (normalizedText is not null)
        {
            await WriteNotificationAsync("thread/realtime/itemAdded", new
            {
                threadId,
                item = new
                {
                    type = "input_text",
                    text = normalizedText,
                    sessionId = session.SessionId,
                    chunkIndex = session.TextChunkCount,
                },
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleThreadRealtimeHandoffOutputAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteRealtimeErrorResponseAsync(id, string.Empty, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!threadManager.TryGetThread(threadId, out var runtimeThread)
            || runtimeThread?.RealtimeSession is not KernelRealtimeSessionState session)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程未启动实时会话：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionId = Normalize(ReadString(@params, "sessionId"));
        if (sessionId is not null && !string.Equals(session.SessionId, sessionId, StringComparison.Ordinal))
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32600, "sessionId 与已启动会话不一致。", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!session.UsesRealtimeWebSocket)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32600, "当前实时会话未启用 websocket 传输。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var handoffId = Normalize(ReadString(@params, "handoffId")
            ?? ReadString(@params, "handoff_id")
            ?? ReadString(@params, "callId")
            ?? ReadString(@params, "call_id"));
        if (string.IsNullOrWhiteSpace(handoffId))
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32602, "handoffId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var outputText = ReadString(@params, "output")
            ?? ReadString(@params, "outputText")
            ?? ReadString(@params, "output_text")
            ?? ReadString(@params, "text")
            ?? string.Empty;

        try
        {
            await SendRealtimeTransportMessageAsync(
                    session,
                    BuildRealtimeHandoffOutputPayload(session, handoffId!, outputText),
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32603, $"回写实时 handoff 输出失败：{ex.Message}", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task HandleThreadRealtimeStopAsync(JsonElement id, JsonElement @params, CancellationToken cancellationToken)
    {
        var threadId = Normalize(ReadString(@params, "threadId"));
        if (string.IsNullOrWhiteSpace(threadId))
        {
            await WriteRealtimeErrorResponseAsync(id, string.Empty, -32602, "threadId 不能为空。", cancellationToken).ConfigureAwait(false);
            return;
        }

        var thread = await threadStore.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        if (thread is null)
        {
            await WriteRealtimeErrorResponseAsync(id, threadId, -32004, $"线程不存在：{threadId}", cancellationToken).ConfigureAwait(false);
            return;
        }

        var currentSession = threadManager.TryGetThread(threadId, out var runtimeThread)
            ? runtimeThread?.RealtimeSession
            : null;
        if (currentSession is not null)
        {
            var sessionId = Normalize(ReadString(@params, "sessionId"));
            if (sessionId is not null && !string.Equals(currentSession.SessionId, sessionId, StringComparison.Ordinal))
            {
                await WriteRealtimeErrorResponseAsync(id, threadId, -32600, "sessionId 与已启动会话不一致。", cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var removed = currentSession is not null;
        if (runtimeThread is not null)
        {
            runtimeThread.SetRealtimeSession(null);
        }

        var session = currentSession;
        if (removed && session is not null && session.UsesRealtimeWebSocket)
        {
            session.TryMarkClosedNotificationWritten();
            await CloseRealtimeTransportCoreAsync(session, "requested").ConfigureAwait(false);
        }

        await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);

        if (removed && session is not null)
        {
            await WriteNotificationAsync("thread/realtime/closed", new
            {
                threadId,
                reason = "requested",
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ShutdownRealtimeSessionsAsync()
    {
        foreach (var threadId in threadManager.GetLoadedThreadIds())
        {
            if (!threadManager.TryGetThread(threadId, out var runtimeThread)
                || runtimeThread?.RealtimeSession is not KernelRealtimeSessionState session)
            {
                continue;
            }

            runtimeThread.SetRealtimeSession(null);
            session.TryMarkClosedNotificationWritten();
            await CloseRealtimeTransportCoreAsync(session, "server_shutdown").ConfigureAwait(false);
        }
    }

    public Task CloseRealtimeTransportAsync(KernelRealtimeSessionState session, string closeReason)
        => CloseRealtimeTransportCoreAsync(session, closeReason);

    private async Task<bool> TryStartRealtimeWebSocketAsync(
        JsonElement id,
        KernelRuntimeThread runtimeThread,
        KernelRealtimeSessionState session,
        CancellationToken cancellationToken)
    {
        if (!session.UsesRealtimeWebSocket)
        {
            return false;
        }

        ClientWebSocket? socket = null;
        CancellationTokenSource? receiveLoopCancellation = null;
        try
        {
            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            KernelCustomCaSupport.ConfigureClientWebSocketOptions(socket.Options);
            ConfigureRealtimeRequestHeaders(socket.Options, runtimeThread.Session, session.SessionId);
            await socket.ConnectAsync(
                    BuildRealtimeWebSocketUri(session.RealtimeWebSocketBaseUrl!, session.Model, session.EventParser),
                    cancellationToken)
                .ConfigureAwait(false);
            await SendRealtimeTransportMessageAsync(socket, BuildRealtimeSessionUpdatePayload(session), cancellationToken).ConfigureAwait(false);

            var updatedSessionId = await WaitForRealtimeSessionUpdatedAsync(runtimeThread, session, socket, cancellationToken)
                .ConfigureAwait(false);

            runtimeThread.SetRealtimeSession(session);
            await WriteResultAsync(id, new { }, cancellationToken).ConfigureAwait(false);
            if (session.TryMarkStartedNotificationWritten())
            {
                await WriteNotificationAsync("thread/realtime/started", new
                {
                    threadId = session.ThreadId,
                    sessionId = updatedSessionId,
                }, cancellationToken).ConfigureAwait(false);
            }

            receiveLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var receiveLoopTask = RunRealtimeTransportReceiveLoopAsync(runtimeThread, session, socket, receiveLoopCancellation.Token);
            session.AttachTransport(socket, receiveLoopCancellation, receiveLoopTask);
            socket = null;
            receiveLoopCancellation = null;
            return true;
        }
        catch (Exception ex)
        {
            await WriteRealtimeErrorResponseAsync(
                    id,
                    session.ThreadId,
                    -32603,
                    $"启动实时会话失败：{ex.Message}",
                    cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            if (receiveLoopCancellation is not null)
            {
                receiveLoopCancellation.Cancel();
                receiveLoopCancellation.Dispose();
            }

            if (socket is not null)
            {
                try
                {
                    if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "startup_failed", CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch
                {
                    // ignore close failures during startup cleanup
                }

                socket.Dispose();
            }
        }
    }

    private async Task<string> WaitForRealtimeSessionUpdatedAsync(
        KernelRuntimeThread runtimeThread,
        KernelRealtimeSessionState session,
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(RealtimeStartTimeout);

        while (true)
        {
            using var message = await ReceiveRealtimeTransportMessageAsync(socket, timeoutCts.Token).ConfigureAwait(false);
            if (message is null)
            {
                throw new InvalidOperationException("实时会话在启动完成前已关闭。");
            }

            var eventType = Normalize(ReadString(message.RootElement, "type")) ?? string.Empty;
            if (string.Equals(eventType, "session.updated", StringComparison.Ordinal))
            {
                var updatedSessionId = Normalize(ReadString(message.RootElement, "session", "id")) ?? session.SessionId;
                session.UpdateSessionId(updatedSessionId);
                return updatedSessionId;
            }

            if (string.Equals(eventType, "error", StringComparison.Ordinal))
            {
                var messageText = ExtractRealtimeErrorMessage(message.RootElement);
                throw new InvalidOperationException(messageText);
            }

            await DispatchRealtimeTransportEventAsync(runtimeThread, session, message.RootElement, CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task RunRealtimeTransportReceiveLoopAsync(
        KernelRuntimeThread runtimeThread,
        KernelRealtimeSessionState session,
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        string? transportError = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var message = await ReceiveRealtimeTransportMessageAsync(socket, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    break;
                }

                await DispatchRealtimeTransportEventAsync(runtimeThread, session, message.RootElement, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (WebSocketException ex)
        {
            transportError = Normalize(ex.Message) ?? "realtime_transport_error";
        }
        catch (Exception ex)
        {
            transportError = Normalize(ex.Message) ?? "realtime_transport_error";
        }
        finally
        {
            var shouldWriteClosed = session.TryMarkClosedNotificationWritten();
            if (ReferenceEquals(runtimeThread.RealtimeSession, session))
            {
                runtimeThread.SetRealtimeSession(null);
            }

            if (!string.IsNullOrWhiteSpace(transportError) && shouldWriteClosed)
            {
                await WriteNotificationAsync("thread/realtime/error", new
                {
                    threadId = session.ThreadId,
                    message = transportError,
                }, CancellationToken.None).ConfigureAwait(false);
            }

            if (shouldWriteClosed)
            {
                await WriteNotificationAsync("thread/realtime/closed", new
                {
                    threadId = session.ThreadId,
                    reason = "transport_closed",
                }, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchRealtimeTransportEventAsync(
        KernelRuntimeThread runtimeThread,
        KernelRealtimeSessionState session,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        var eventType = Normalize(ReadString(message, "type")) ?? string.Empty;
        switch (eventType)
        {
            case "session.updated":
            {
                var updatedSessionId = Normalize(ReadString(message, "session", "id")) ?? session.SessionId;
                session.UpdateSessionId(updatedSessionId);
                if (session.TryMarkStartedNotificationWritten())
                {
                    await WriteNotificationAsync("thread/realtime/started", new
                    {
                        threadId = session.ThreadId,
                        sessionId = updatedSessionId,
                    }, cancellationToken).ConfigureAwait(false);
                }

                break;
            }
            case "conversation.output_audio.delta":
            case "response.output_audio.delta":
                await WriteNotificationAsync("thread/realtime/outputAudio/delta", new
                {
                    threadId = session.ThreadId,
                    audio = new
                    {
                        data = ReadString(message, "delta") ?? string.Empty,
                        sampleRate = ReadInt(message, "sample_rate") ?? ReadInt(message, "sampleRate") ?? RealtimeAudioSampleRate,
                        numChannels = ReadInt(message, "channels") ?? ReadInt(message, "numChannels") ?? RealtimeAudioChannels,
                        samplesPerChannel = ReadInt(message, "samples_per_channel") ?? ReadInt(message, "samplesPerChannel"),
                    },
                }, cancellationToken).ConfigureAwait(false);
                break;
            case "conversation.input_transcript.delta":
                session.AppendTranscriptDelta("user", ReadString(message, "delta") ?? string.Empty);
                break;
            case "conversation.output_transcript.delta":
                session.AppendTranscriptDelta("assistant", ReadString(message, "delta") ?? string.Empty);
                break;
            case "conversation.item.input_audio_transcription.delta":
                session.AppendTranscriptDelta("user", ReadString(message, "delta") ?? string.Empty);
                break;
            case "conversation.item.input_audio_transcription.completed":
                session.AppendTranscriptDelta("user", ReadString(message, "transcript") ?? string.Empty);
                break;
            case "response.output_text.delta":
            case "response.output_audio_transcript.delta":
                session.AppendTranscriptDelta("assistant", ReadString(message, "delta") ?? string.Empty);
                break;
            case "conversation.item.added":
                if (message.TryGetProperty("item", out var item))
                {
                    await WriteNotificationAsync("thread/realtime/itemAdded", new
                    {
                        threadId = session.ThreadId,
                        item = item.Clone(),
                    }, cancellationToken).ConfigureAwait(false);
                }

                break;
            case "conversation.handoff.requested":
                await WriteNotificationAsync("thread/realtime/itemAdded", new
                {
                    threadId = session.ThreadId,
                    item = BuildRealtimeHandoffRequestedItem(
                        handoffId: ReadString(message, "handoff_id") ?? string.Empty,
                        itemId: ReadString(message, "item_id") ?? string.Empty,
                        inputTranscript: ReadString(message, "input_transcript") ?? string.Empty,
                        activeTranscript: session.DrainActiveTranscript()),
                }, cancellationToken).ConfigureAwait(false);
                break;
            case "conversation.item.done":
                if (TryBuildRealtimeV2HandoffRequestedItem(message, session, out var handoffItem))
                {
                    await WriteNotificationAsync("thread/realtime/itemAdded", new
                    {
                        threadId = session.ThreadId,
                        item = handoffItem,
                    }, cancellationToken).ConfigureAwait(false);
                }

                break;
            case "error":
                await WriteNotificationAsync("thread/realtime/error", new
                {
                    threadId = session.ThreadId,
                    message = ExtractRealtimeErrorMessage(message),
                }, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task SendRealtimeTransportMessageAsync(
        KernelRealtimeSessionState session,
        object payload,
        CancellationToken cancellationToken)
    {
        var socket = session.TransportSocket;
        if (socket is null)
        {
            throw new InvalidOperationException("实时会话尚未建立传输连接。");
        }

        await SendRealtimeTransportMessageAsync(socket, payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendRealtimeTransportMessageAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException($"实时传输当前不可写：{socket.State}");
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<JsonDocument?> ReceiveRealtimeTransportMessageAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
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

        stream.Position = 0;
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static object BuildRealtimeSessionUpdatePayload(KernelRealtimeSessionState session)
    {
        var normalizedSessionMode = session.EventParser == KernelRealtimeEventParser.V1
            ? KernelRealtimeSessionMode.Conversational
            : session.SessionMode;
        var audioPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["input"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["format"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "audio/pcm",
                    ["rate"] = RealtimeAudioSampleRate,
                },
            },
        };
        if (normalizedSessionMode == KernelRealtimeSessionMode.Conversational)
        {
            audioPayload["output"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["voice"] = RealtimeAudioVoice,
            };
        }

        var sessionPayload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = normalizedSessionMode == KernelRealtimeSessionMode.Conversational
                ? session.EventParser == KernelRealtimeEventParser.V1
                    ? "quicksilver"
                    : "realtime"
                : "transcription",
            ["audio"] = audioPayload,
        };
        if (normalizedSessionMode == KernelRealtimeSessionMode.Conversational)
        {
            sessionPayload["instructions"] = session.EffectiveInstructions;
        }

        if (session.EventParser == KernelRealtimeEventParser.RealtimeV2)
        {
            sessionPayload["tools"] = new object[]
            {
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["type"] = "function",
                    ["name"] = RealtimeV2TianShuToolName,
                    ["description"] = "Delegate work to TianShu and return the result.",
                    ["parameters"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["prompt"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "string",
                                ["description"] = "Prompt text for the delegated TianShu task.",
                            },
                        },
                        ["required"] = new[] { "prompt" },
                        ["additionalProperties"] = false,
                    },
                },
            };
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "session.update",
            ["session"] = sessionPayload,
        };
    }

    private static object BuildRealtimeHandoffOutputPayload(KernelRealtimeSessionState session, string handoffId, string outputText)
    {
        return session.EventParser == KernelRealtimeEventParser.V1
            ? new
            {
                type = "conversation.handoff.append",
                handoff_id = handoffId,
                output_text = outputText,
            }
            : new
            {
                type = "conversation.item.create",
                item = ToolUseFollowUpItemProjector.BuildFunctionCallOutputItem(
                    handoffId,
                    isCustomToolCall: false,
                    output: outputText),
            };
    }

    private static Uri BuildRealtimeWebSocketUri(string baseUrl, string? model, KernelRealtimeEventParser eventParser)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"无效 realtime websocket 地址：{baseUrl}");
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme switch
            {
                "http" => "ws",
                "https" => "wss",
                "ws" => "ws",
                "wss" => "wss",
                _ => throw new InvalidOperationException($"不支持的 realtime websocket 协议：{uri.Scheme}"),
            },
        };
        builder.Path = NormalizeRealtimeWebSocketPath(builder.Path);
        builder.Query = BuildRealtimeWebSocketQuery(builder.Query, model, eventParser);
        return builder.Uri;
    }

    private void ConfigureRealtimeRequestHeaders(
        ClientWebSocketOptions options,
        KernelThreadSessionState threadSession,
        string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            options.SetRequestHeader("x-session-id", sessionId);
        }

        var apiKeyEnv = Normalize(threadSession.ProviderApiKeyEnvironmentVariable) ?? "OPENAI_API_KEY";
        var apiKey = Normalize(Environment.GetEnvironmentVariable(apiKeyEnv));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        }
    }

    private static string NormalizeRealtimeWebSocketPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return "/v1/realtime";
        }

        if (path.EndsWith("/realtime", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.EndsWith("/realtime/", StringComparison.OrdinalIgnoreCase))
        {
            return path.TrimEnd('/');
        }

        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{path}/realtime";
        }

        if (path.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{path}realtime";
        }

        return path;
    }

    private static string BuildRealtimeWebSocketQuery(string? existingQuery, string? model, KernelRealtimeEventParser eventParser)
    {
        var pairs = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(existingQuery))
        {
            foreach (var segment in existingQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = segment.IndexOf('=');
                var key = separatorIndex >= 0 ? segment[..separatorIndex] : segment;
                var value = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : string.Empty;
                if (string.Equals(key, "intent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "model", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pairs.Add(new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(key),
                    Uri.UnescapeDataString(value)));
            }
        }

        if (eventParser == KernelRealtimeEventParser.V1)
        {
            pairs.Insert(0, new KeyValuePair<string, string>("intent", "quicksilver"));
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            pairs.Add(new KeyValuePair<string, string>("model", model!));
        }

        return string.Join("&", pairs.Select(static pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static object BuildRealtimeHandoffRequestedItem(
        string handoffId,
        string itemId,
        string inputTranscript,
        IReadOnlyList<KernelRealtimeTranscriptEntry> activeTranscript)
    {
        return new
        {
            type = "handoff_request",
            handoff_id = handoffId,
            item_id = itemId,
            input_transcript = inputTranscript,
            active_transcript = activeTranscript
                .Select(static entry => new
                {
                    role = entry.Role,
                    text = entry.Text,
                })
                .ToArray(),
        };
    }

    private bool TryBuildRealtimeV2HandoffRequestedItem(
        JsonElement message,
        KernelRealtimeSessionState session,
        out object? handoffItem)
    {
        handoffItem = null;
        if (!message.TryGetProperty("item", out var item)
            || !string.Equals(ReadString(item, "type"), "function_call", StringComparison.Ordinal)
            || !string.Equals(ReadString(item, "name"), RealtimeV2TianShuToolName, StringComparison.Ordinal))
        {
            return false;
        }

        var handoffId = Normalize(ReadString(item, "call_id") ?? ReadString(item, "id"));
        var itemId = Normalize(ReadString(item, "id")) ?? handoffId;
        if (string.IsNullOrWhiteSpace(handoffId) || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        handoffItem = BuildRealtimeHandoffRequestedItem(
            handoffId,
            itemId,
            ExtractRealtimeHandoffInputTranscript(ReadString(item, "arguments")),
            session.DrainActiveTranscript());
        return true;
    }

    private string ExtractRealtimeHandoffInputTranscript(string? rawArguments)
    {
        var arguments = Normalize(rawArguments);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "input_transcript", "input", "text", "prompt", "query" })
                {
                    if (document.RootElement.TryGetProperty(key, out var value)
                        && value.ValueKind == JsonValueKind.String)
                    {
                        var transcript = Normalize(value.GetString());
                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            return transcript;
                        }
                    }
                }
            }
        }
        catch
        {
            // fall through and use the raw text.
        }

        return arguments!;
    }

    private string ExtractRealtimeErrorMessage(JsonElement message)
    {
        var directMessage = Normalize(ReadString(message, "message"));
        if (!string.IsNullOrWhiteSpace(directMessage))
        {
            return directMessage!;
        }

        if (!message.TryGetProperty("error", out var error))
        {
            return "realtime_error";
        }

        var nestedMessage = error.ValueKind == JsonValueKind.Object
            ? Normalize(ReadString(message, "error", "message"))
            : Normalize(error.ValueKind == JsonValueKind.String ? error.GetString() : error.GetRawText());
        return string.IsNullOrWhiteSpace(nestedMessage) ? "realtime_error" : nestedMessage!;
    }

    private async Task CloseRealtimeTransportCoreAsync(KernelRealtimeSessionState session, string closeReason)
    {
        var (socket, cancellation, task) = session.DetachTransport();
        cancellation?.Cancel();

        if (socket is not null)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, closeReason, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // ignore close failures during transport shutdown
            }
        }

        if (task is not null)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // ignore receive loop shutdown failures
            }
        }

        socket?.Dispose();
        cancellation?.Dispose();
    }

    private async Task WriteRealtimeErrorResponseAsync(
        JsonElement id,
        string threadId,
        int code,
        string message,
        CancellationToken cancellationToken)
    {
        await WriteNotificationAsync("thread/realtime/error", new
        {
            threadId,
            message,
        }, cancellationToken).ConfigureAwait(false);

        await WriteErrorAsync(id, code, message, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadString(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement json, params string[] path)
    {
        var current = json;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.Number when current.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(current.GetString(), out var value) => value,
            _ => null,
        };
    }

    private static bool TryReadObject(JsonElement json, string propertyName, out JsonElement value)
    {
        value = default;
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var candidate))
        {
            return false;
        }

        if (candidate.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private string? Normalize(string? value)
        => normalize(value);

    private Task WriteNotificationAsync(string method, object payload, CancellationToken cancellationToken)
        => writeNotificationAsync(method, payload, cancellationToken);

    private Task WriteResultAsync(JsonElement id, object payload, CancellationToken cancellationToken)
        => writeResultAsync(id, payload, cancellationToken);

    private Task WriteErrorAsync(JsonElement? id, int code, string message, CancellationToken cancellationToken)
        => writeErrorAsync(id, code, message, cancellationToken);
}
