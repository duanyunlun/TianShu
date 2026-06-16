using TianShu.AppHost.State;
using TianShu.AppHost.Tools;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace TianShu.AppHost;

/// <summary>
/// AppHost 的 WebSocket 传输宿主。
/// WebSocket transport host owned by AppHost.
/// </summary>
internal sealed class AppHostWebSocketTransportHost
{
    private readonly HttpListener listener = new();
    private readonly KernelThreadStore threadStore;
    private readonly IReadOnlyDictionary<string, string> cliConfigOverrides;
    private readonly string? cliConfigFilePath;
    private readonly KernelGlobalNotificationHub globalNotificationHub = new();
    private readonly ConcurrentDictionary<long, CancellationTokenSource> connectionCancellationSources = new();
    private readonly ConcurrentBag<Task> connectionTasks = new();
    private long connectionSequence;

    public AppHostWebSocketTransportHost(
        IPEndPoint bindAddress,
        KernelThreadStore threadStore,
        IReadOnlyDictionary<string, string> cliConfigOverrides,
        string? cliConfigFilePath = null)
    {
        this.threadStore = threadStore;
        this.cliConfigOverrides = cliConfigOverrides;
        this.cliConfigFilePath = cliConfigFilePath;
        listener.Prefixes.Add($"http://{bindAddress.Address}:{bindAddress.Port}/");
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        using var registration = cancellationToken.Register(static state =>
        {
            try
            {
                ((HttpListener)state!).Close();
            }
            catch
            {
                // ignore shutdown races
            }
        }, listener);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (cancellationToken.IsCancellationRequested
                                           || ex is HttpListenerException
                                           || ex is ObjectDisposedException)
                {
                    break;
                }

                var connectionId = Interlocked.Increment(ref connectionSequence);
                var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionCancellationSources[connectionId] = connectionCts;
                var task = HandleConnectionAsync(connectionId, context, connectionCts);
                connectionTasks.Add(task);
                _ = task.ContinueWith(
                    static (_, state) =>
                    {
                        var tuple = ((ConcurrentDictionary<long, CancellationTokenSource> Sources, long ConnectionId))state!;
                        if (tuple.Sources.TryRemove(tuple.ConnectionId, out var source))
                        {
                            source.Dispose();
                        }
                    },
                    (connectionCancellationSources, connectionId),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        finally
        {
            foreach (var source in connectionCancellationSources.Values)
            {
                try
                {
                    source.Cancel();
                }
                catch
                {
                    // ignore shutdown races
                }
            }

            listener.Close();
            await Task.WhenAll(connectionTasks.ToArray()).ConfigureAwait(false);
        }
    }

    private async Task HandleConnectionAsync(long connectionId, HttpListenerContext context, CancellationTokenSource connectionCts)
    {
        try
        {
            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                return;
            }

            var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
            using var webSocket = webSocketContext.WebSocket;
            using var input = new WebSocketTextReader(webSocket, connectionCts.Token);
            using var output = new WebSocketTextWriter(webSocket, connectionCts.Token);
            var server = new AppHostServer(
                input,
                output,
                threadStore,
                cliConfigOverrides,
                cliConfigFilePath,
                httpClient: null,
                globalNotificationHub,
                () => connectionCts.Cancel());

            await server.RunAsync(connectionCts.Token).ConfigureAwait(false);
            await CloseSocketAsync(webSocket).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (connectionCts.IsCancellationRequested)
        {
            // expected during connection shutdown
        }
        catch
        {
            // keep listener alive for sibling connections
        }
    }

    private static async Task CloseSocketAsync(WebSocket webSocket)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            try
            {
                webSocket.Abort();
            }
            catch
            {
                // ignore close failures
            }
        }
    }

    private sealed class WebSocketTextReader(WebSocket webSocket, CancellationToken cancellationToken) : TextReader
    {
        private readonly byte[] buffer = new byte[8192];

        public override async Task<string?> ReadLineAsync()
        {
            if (webSocket.State is not WebSocketState.Open and not WebSocketState.CloseSent and not WebSocketState.CloseReceived)
            {
                return null;
            }

            var builder = new StringBuilder();
            while (true)
            {
                WebSocketReceiveResult received;
                try
                {
                    received = await webSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
                catch
                {
                    return null;
                }

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (received.Count > 0)
                {
                    builder.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                }

                if (received.EndOfMessage)
                {
                    break;
                }
            }

            return builder.ToString();
        }
    }

    private sealed class WebSocketTextWriter(WebSocket webSocket, CancellationToken cancellationToken) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override async Task WriteLineAsync(string? value)
        {
            if (webSocket.State != WebSocketState.Open)
            {
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }

        public override Task FlushAsync() => Task.CompletedTask;
    }
}
