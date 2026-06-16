using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class KernelAppServerWebSocketTransportTests
{
    [Fact]
    public async Task RunWebSocketAsync_ShouldBroadcastThreadNameUpdatesToAllInitializedClients()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var threadId = "0195f4ee-6c42-7f23-9a61-3cf1b4db1001";
        var threadStore = new KernelThreadStore(storePath);
        await threadStore.InitializeAsync(CancellationToken.None);
        await threadStore.CreateThreadAsync(threadId, "D:/Repo", CancellationToken.None);

        var bindAddress = new IPEndPoint(IPAddress.Loopback, GetFreeTcpPort());
        using var serverCts = new CancellationTokenSource();
        var serverTask = StartWebSocketServerAsync(bindAddress, threadStore, serverCts.Token);

        using var ws1 = await ConnectAsync(bindAddress);
        using var ws2 = await ConnectAsync(bindAddress);

        try
        {
            await InitializeAsync(ws1, 1, "ws_client_one");
            await InitializeAsync(ws2, 2, "ws_client_two");

            await SendJsonAsync(ws1, CreateRequest(
                11,
                "thread/name/set",
                new Dictionary<string, object?>
                {
                    ["threadId"] = threadId,
                    ["name"] = "Broadcast rename",
                }));

            var (response, notification) = await ReadResponseAndNotificationAsync(ws1, 11, "thread/name/updated");
            using (response)
            using (notification)
            {
                Assert.Equal(11, response.RootElement.GetProperty("id").GetInt32());
                AssertThreadNameUpdated(notification.RootElement, threadId, "Broadcast rename");
            }

            using var ws2Notification = await ReadNotificationAsync(ws2, "thread/name/updated");
            AssertThreadNameUpdated(ws2Notification.RootElement, threadId, "Broadcast rename");
            Assert.Null(await TryReceiveJsonAsync(ws1, TimeSpan.FromMilliseconds(250)));
            Assert.Null(await TryReceiveJsonAsync(ws2, TimeSpan.FromMilliseconds(250)));
        }
        finally
        {
            await ShutdownServerAsync(serverTask, serverCts, ws1, ws2);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunWebSocketAsync_ShouldSkipUninitializedClientsWhenBroadcastingGlobalNotifications()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var threadId = "0195f4ee-6c42-7f23-9a61-3cf1b4db1002";
        var threadStore = new KernelThreadStore(storePath);
        await threadStore.InitializeAsync(CancellationToken.None);
        await threadStore.CreateThreadAsync(threadId, "D:/Repo", CancellationToken.None);

        var bindAddress = new IPEndPoint(IPAddress.Loopback, GetFreeTcpPort());
        using var serverCts = new CancellationTokenSource();
        var serverTask = StartWebSocketServerAsync(bindAddress, threadStore, serverCts.Token);

        using var ws1 = await ConnectAsync(bindAddress);
        using var ws2 = await ConnectAsync(bindAddress);

        try
        {
            await InitializeAsync(ws1, 1, "ws_client_one");

            await SendJsonAsync(ws1, CreateRequest(
                12,
                "thread/name/set",
                new Dictionary<string, object?>
                {
                    ["threadId"] = threadId,
                    ["name"] = "Single client rename",
                }));

            var (response, notification) = await ReadResponseAndNotificationAsync(ws1, 12, "thread/name/updated");
            using (response)
            using (notification)
            {
                Assert.Equal(12, response.RootElement.GetProperty("id").GetInt32());
                AssertThreadNameUpdated(notification.RootElement, threadId, "Single client rename");
            }

            Assert.Null(await TryReceiveJsonAsync(ws2, TimeSpan.FromMilliseconds(300)));
        }
        finally
        {
            await ShutdownServerAsync(serverTask, serverCts, ws1, ws2);
            DeleteDirectory(root);
        }
    }

    private static async Task ShutdownServerAsync(
        Task serverTask,
        CancellationTokenSource serverCts,
        ClientWebSocket ws1,
        ClientWebSocket ws2)
    {
        try
        {
            serverCts.Cancel();
            await CloseSocketAsync(ws1);
            await CloseSocketAsync(ws2);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<ClientWebSocket> ConnectAsync(IPEndPoint bindAddress)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://{bindAddress.Address}:{bindAddress.Port}/"), CancellationToken.None);
        return socket;
    }

    private static async Task InitializeAsync(ClientWebSocket socket, int requestId, string clientName)
    {
        await SendJsonAsync(socket, CreateRequest(
            requestId,
            "initialize",
            new Dictionary<string, object?>
            {
                ["clientInfo"] = new Dictionary<string, object?>
                {
                    ["name"] = clientName,
                    ["version"] = "1.0.0",
                },
            }));

        using var response = await ReadResponseAsync(socket, requestId);
        Assert.True(response.RootElement.TryGetProperty("result", out _));
    }

    private static string CreateRequest(int id, string method, IReadOnlyDictionary<string, object?> @params)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params,
        });

    private static async Task SendJsonAsync(ClientWebSocket socket, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private static async Task<(JsonDocument Response, JsonDocument Notification)> ReadResponseAndNotificationAsync(
        ClientWebSocket socket,
        int requestId,
        string method)
    {
        JsonDocument? response = null;
        JsonDocument? notification = null;
        try
        {
            for (var index = 0; index < 4 && (response is null || notification is null); index++)
            {
                var message = await ReceiveJsonAsync(socket, TimeSpan.FromSeconds(3));
                if (message.RootElement.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.Number
                    && idElement.GetInt32() == requestId)
                {
                    response = message;
                    continue;
                }

                if (message.RootElement.TryGetProperty("method", out var methodElement)
                    && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
                {
                    notification = message;
                    continue;
                }

                message.Dispose();
            }

            Assert.NotNull(response);
            Assert.NotNull(notification);
            return (response!, notification!);
        }
        catch
        {
            response?.Dispose();
            notification?.Dispose();
            throw;
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(ClientWebSocket socket, int requestId)
    {
        for (var index = 0; index < 4; index++)
        {
            var message = await ReceiveJsonAsync(socket, TimeSpan.FromSeconds(3));
            if (message.RootElement.TryGetProperty("id", out var idElement)
                && idElement.ValueKind == JsonValueKind.Number
                && idElement.GetInt32() == requestId)
            {
                return message;
            }

            message.Dispose();
        }

        throw new InvalidOperationException($"未收到请求 {requestId} 的响应。");
    }

    private static async Task<JsonDocument> ReadNotificationAsync(ClientWebSocket socket, string method)
    {
        for (var index = 0; index < 4; index++)
        {
            var message = await ReceiveJsonAsync(socket, TimeSpan.FromSeconds(3));
            if (message.RootElement.TryGetProperty("method", out var methodElement)
                && string.Equals(methodElement.GetString(), method, StringComparison.Ordinal))
            {
                return message;
            }

            message.Dispose();
        }

        throw new InvalidOperationException($"未收到通知 {method}。");
    }

    private static async Task<JsonDocument?> TryReceiveJsonAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        try
        {
            return await ReceiveJsonAsync(socket, timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static async Task<JsonDocument> ReceiveJsonAsync(ClientWebSocket socket, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var text = await ReceiveTextAsync(socket, cts.Token);
        if (text is null)
        {
            throw new TimeoutException("在超时时间内未收到 websocket 消息。");
        }

        return JsonDocument.Parse(text);
    }

    private static async Task<string?> ReceiveTextAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var stream = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AssertThreadNameUpdated(JsonElement message, string expectedThreadId, string expectedName)
    {
        Assert.Equal("thread/name/updated", message.GetProperty("method").GetString());
        var @params = message.GetProperty("params");
        Assert.Equal(expectedThreadId, @params.GetProperty("threadId").GetString());
        Assert.Equal(expectedName, @params.GetProperty("threadName").GetString());
    }

    private static Task StartWebSocketServerAsync(
        IPEndPoint bindAddress,
        KernelThreadStore threadStore,
        CancellationToken cancellationToken)
    {
        var appHostAssembly = Assembly.Load("TianShu.AppHost");
        var programType = appHostAssembly.GetType("TianShu.AppHost.Program", throwOnError: true)!;
        var method = programType.GetMethod(
            "RunWebSocketAsync",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            [typeof(IPEndPoint), typeof(KernelThreadStore), typeof(IReadOnlyDictionary<string, string>), typeof(string), typeof(CancellationToken)],
            modifiers: null);
        Assert.NotNull(method);
        return (Task)method!.Invoke(null, [bindAddress, threadStore, new Dictionary<string, string>(StringComparer.Ordinal), null, cancellationToken])!;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShu.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(120);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(120);
            }
        }
    }

    private static async Task CloseSocketAsync(ClientWebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
        }
        catch
        {
            socket.Abort();
        }
    }
}

