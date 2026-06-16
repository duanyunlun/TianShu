using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TianShu.AppHost.Tools;

namespace TianShu.AppHost.Tests;

public sealed class KernelMcpManagerResourceTests
{
    [Fact]
    public async Task KernelMcpManager_ShouldHandleStreamableHttpResourcesWorkflow()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer();
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                var allResources = await manager.ListResourcesAsync(null, null, CancellationToken.None);
            Assert.Null(allResources.Server);
            Assert.Null(allResources.NextCursor);
            Assert.Equal(2, allResources.Resources.Count);
            Assert.All(allResources.Resources, static entry => Assert.Equal("docs", entry.Server));

            var firstPage = await manager.ListResourcesAsync("docs", null, CancellationToken.None);
            Assert.Equal("docs", firstPage.Server);
            Assert.Equal("cursor-1", firstPage.NextCursor);
            Assert.Single(firstPage.Resources);

            var secondPage = await manager.ListResourcesAsync("docs", "cursor-1", CancellationToken.None);
            Assert.Equal("docs", secondPage.Server);
            Assert.Null(secondPage.NextCursor);
            Assert.Single(secondPage.Resources);

            var allTemplates = await manager.ListResourceTemplatesAsync(null, null, CancellationToken.None);
            Assert.Null(allTemplates.Server);
            Assert.Null(allTemplates.NextCursor);
            Assert.Equal(2, allTemplates.ResourceTemplates.Count);
            Assert.All(allTemplates.ResourceTemplates, static entry => Assert.Equal("docs", entry.Server));

            var readResult = await manager.ReadResourceAsync("docs", "file://docs/readme.md", CancellationToken.None);
            Assert.Equal("docs", readResult.Server);
            Assert.Equal("file://docs/readme.md", readResult.Uri);
            var contents = readResult.Result.GetProperty("contents");
            Assert.Single(contents.EnumerateArray());

                var logs = server.Requests.ToArray();
                Assert.Contains(logs, static record => record.Method == "initialize" && record.JsonRpc == "2.0");
                Assert.Contains(logs, static record => record.Method == "notifications/initialized" && record.JsonRpc == "2.0" && !record.Id.HasValue);
                Assert.Contains(logs, static record => record.Method == "resources/list" && record.SessionId == "session-http-1");
                Assert.Contains(logs, static record => record.Method == "resources/templates/list" && record.SessionId == "session-http-1");
                Assert.Contains(logs, static record => record.Method == "resources/read" && record.SessionId == "session-http-1");
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldBuildStatusDataFromSnapshot()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer();
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                var statuses = await manager.BuildStatusDataAsync(["docs"], CancellationToken.None);
                var status = Assert.Single(statuses);
                Assert.Equal("docs", status.Name);
                Assert.Equal("not_logged_in", status.AuthStatus);

                var tool = Assert.IsType<JsonElement>(Assert.Single(status.Tools).Value);
                Assert.Equal("search", tool.GetProperty("name").GetString());

                Assert.Equal(2, status.Resources.Count);
                var resource = Assert.IsType<JsonElement>(status.Resources[0]);
                Assert.Equal("file://docs/readme.md", resource.GetProperty("uri").GetString());

                Assert.Equal(2, status.ResourceTemplates.Count);
                var template = Assert.IsType<JsonElement>(status.ResourceTemplates[0]);
                Assert.Equal("docs://{path}", template.GetProperty("uriTemplate").GetString());

                var methods = server.Requests.Select(static request => request.Method).ToArray();
                Assert.Contains("tools/list", methods);
                Assert.Contains("resources/list", methods);
                Assert.Contains("resources/templates/list", methods);
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldFilterStatusToolsUsingEnabledAndDisabledLists()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer(tools: ["search", "allowed", "blocked"]);
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                enabled_tools = ["search", "allowed"]
                disabled_tools = ["search"]
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                var statuses = await manager.BuildStatusDataAsync(["docs"], CancellationToken.None);
                var status = Assert.Single(statuses);
                Assert.Equal(["allowed"], status.Tools.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldApplyStartupTimeoutToInitialization()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer(initializeDelay: TimeSpan.FromMilliseconds(200));
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                startup_timeout_sec = 0.05
                tool_timeout_sec = 1
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(
                    () => manager.ListResourcesAsync("docs", null, CancellationToken.None));
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldApplyToolTimeoutAfterInitialization()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer(resourcesListDelay: TimeSpan.FromMilliseconds(200));
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                startup_timeout_sec = 1
                tool_timeout_sec = 0.05
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(
                    () => manager.ListResourcesAsync("docs", null, CancellationToken.None));
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldHandleStdioResourcesWorkflow()
    {
        var root = CreateTempDirectory();
        var logPath = Path.Combine(root, "stdio-log.jsonl");
        var scriptPath = Path.Combine(root, "mcp_stdio_server.py");
        await File.WriteAllTextAsync(scriptPath, BuildPythonMcpServerScript());
        await File.WriteAllTextAsync(
            Path.Combine(root, "tianshu.toml"),
            $"""
            [mcp_servers.docs]
            command = "python"
            args = ["{EscapeTomlString(scriptPath)}", "{EscapeTomlString(logPath)}"]
            """);

        try
        {
            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root);
            try
            {
                var allResources = await manager.ListResourcesAsync(null, null, CancellationToken.None);
                Assert.Equal(2, allResources.Resources.Count);

                var firstPage = await manager.ListResourcesAsync("docs", null, CancellationToken.None);
                Assert.Equal("cursor-1", firstPage.NextCursor);
                Assert.Single(firstPage.Resources);

                var secondPage = await manager.ListResourcesAsync("docs", "cursor-1", CancellationToken.None);
                Assert.Null(secondPage.NextCursor);
                Assert.Single(secondPage.Resources);

                var allTemplates = await manager.ListResourceTemplatesAsync(null, null, CancellationToken.None);
                Assert.Equal(2, allTemplates.ResourceTemplates.Count);

                var readResult = await manager.ReadResourceAsync("docs", "file://docs/readme.md", CancellationToken.None);
                Assert.Equal("file://docs/readme.md", readResult.Uri);
                Assert.Single(readResult.Result.GetProperty("contents").EnumerateArray());
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }

            var logs = (await File.ReadAllLinesAsync(logPath))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                Assert.Contains(logs, static doc => doc.RootElement.GetProperty("jsonrpc").GetString() == "2.0"
                    && doc.RootElement.GetProperty("method").GetString() == "initialize");
                Assert.Contains(logs, static doc => doc.RootElement.GetProperty("jsonrpc").GetString() == "2.0"
                    && doc.RootElement.GetProperty("method").GetString() == "notifications/initialized"
                    && !doc.RootElement.TryGetProperty("id", out _));
                Assert.Contains(logs, static doc => doc.RootElement.GetProperty("method").GetString() == "resources/list");
                Assert.Contains(logs, static doc => doc.RootElement.GetProperty("method").GetString() == "resources/templates/list");
                Assert.Contains(logs, static doc => doc.RootElement.GetProperty("method").GetString() == "resources/read");
            }
            finally
            {
                foreach (var log in logs)
                {
                    log.Dispose();
                }
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task KernelMcpManager_ShouldPushSandboxStateWhenCapabilityAdvertised()
    {
        var root = CreateTempDirectory();
        await using var server = new LoopbackMcpHttpServer(enableSandboxStateCapability: true);
        await server.StartAsync();

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "tianshu.toml"),
                $"""
                [mcp_servers.docs]
                url = "{server.Endpoint}"
                """);

            var manager = new KernelMcpManager(
                _ => Task.FromResult(new Dictionary<string, string>()),
                tianShuHome: root,
                httpClient: new HttpClient
                {
                    Timeout = Timeout.InfiniteTimeSpan,
                });
            try
            {
                await manager.UpdateSandboxStateAsync(
                    KernelMcpSandboxState.Create(
                        JsonSerializer.SerializeToElement(new { type = "workspaceWrite" }),
                        root),
                    CancellationToken.None);

                var resources = await manager.ListResourcesAsync("docs", null, CancellationToken.None);
                Assert.Equal("docs", resources.Server);
                Assert.Single(resources.Resources);

                var methods = server.Requests.Select(static request => request.Method).ToArray();
                Assert.Equal(
                    ["initialize", "notifications/initialized", "tianshu/sandbox-state/update", "resources/list"],
                    methods);
            }
            finally
            {
                await DisposeManagerClientsAsync(manager);
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string BuildPythonMcpServerScript()
    {
        return """
import json
import pathlib
import sys

log_path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("mcp-stdio-log.jsonl")
log_path.parent.mkdir(parents=True, exist_ok=True)

with log_path.open("a", encoding="utf-8") as log_file:
    while True:
        line = sys.stdin.readline()
        if not line:
            break
        line = line.strip()
        if not line:
            continue
        msg = json.loads(line)
        log_file.write(json.dumps(msg, ensure_ascii=False) + "\n")
        log_file.flush()
        method = msg.get("method")
        if method == "notifications/initialized":
            continue
        if method == "initialize":
            response = {
                "jsonrpc": "2.0",
                "id": msg["id"],
                "result": {
                    "protocolVersion": "2025-06-18",
                    "capabilities": {},
                    "serverInfo": {"name": "stdio-test", "version": "1.0.0"},
                },
            }
        elif method == "resources/list":
            cursor = ((msg.get("params") or {}).get("cursor"))
            if cursor == "cursor-1":
                response = {
                    "jsonrpc": "2.0",
                    "id": msg["id"],
                    "result": {
                        "resources": [
                            {"uri": "file://docs/chapter-2.md", "name": "Chapter 2"}
                        ]
                    },
                }
            else:
                response = {
                    "jsonrpc": "2.0",
                    "id": msg["id"],
                    "result": {
                        "resources": [
                            {"uri": "file://docs/readme.md", "name": "README"}
                        ],
                        "nextCursor": "cursor-1",
                    },
                }
        elif method == "resources/templates/list":
            cursor = ((msg.get("params") or {}).get("cursor"))
            if cursor == "template-cursor-1":
                response = {
                    "jsonrpc": "2.0",
                    "id": msg["id"],
                    "result": {
                        "resourceTemplates": [
                            {"uriTemplate": "docs://{path}?rev=2", "name": "Revision Template"}
                        ]
                    },
                }
            else:
                response = {
                    "jsonrpc": "2.0",
                    "id": msg["id"],
                    "result": {
                        "resourceTemplates": [
                            {"uriTemplate": "docs://{path}", "name": "Primary Template"}
                        ],
                        "nextCursor": "template-cursor-1",
                    },
                }
        elif method == "resources/read":
            uri = (msg.get("params") or {}).get("uri")
            response = {
                "jsonrpc": "2.0",
                "id": msg["id"],
                "result": {
                    "contents": [
                        {"uri": uri, "mimeType": "text/plain", "text": "body:" + (uri or "")}
                    ]
                },
            }
        else:
            response = {
                "jsonrpc": "2.0",
                "id": msg["id"],
                "error": {"code": -32601, "message": f"unknown method: {method}"},
            }
        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()
""";
    }

    private static async Task DisposeManagerClientsAsync(KernelMcpManager manager)
    {
        var method = typeof(KernelMcpManager).GetMethod("ResetClientsAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(manager, []) as Task;
        Assert.NotNull(task);
        await task!;
    }
    private static string EscapeTomlString(string value)
        => value.Replace("\\", "\\\\");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuKernelMcpManagerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record HttpRequestRecord(string Method, string JsonRpc, int? Id, string? SessionId);

    private sealed class LoopbackMcpHttpServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly ConcurrentQueue<HttpRequestRecord> requests = new();
        private readonly bool enableSandboxStateCapability;
        private readonly IReadOnlyList<string> tools;
        private readonly TimeSpan initializeDelay;
        private readonly TimeSpan resourcesListDelay;
        private CancellationTokenSource? cts;
        private Task? loopTask;

        public LoopbackMcpHttpServer(
            bool enableSandboxStateCapability = false,
            IReadOnlyList<string>? tools = null,
            TimeSpan? initializeDelay = null,
            TimeSpan? resourcesListDelay = null)
        {
            this.enableSandboxStateCapability = enableSandboxStateCapability;
            this.tools = tools ?? ["search"];
            this.initializeDelay = initializeDelay ?? TimeSpan.Zero;
            this.resourcesListDelay = resourcesListDelay ?? TimeSpan.Zero;
        }

        public string Endpoint { get; private set; } = string.Empty;

        public IReadOnlyCollection<HttpRequestRecord> Requests => requests.ToArray();

        public Task StartAsync()
        {
            var port = GetFreeTcpPort();
            Endpoint = $"http://127.0.0.1:{port}/mcp/";
            listener.Prefixes.Add(Endpoint);
            listener.Start();
            cts = new CancellationTokenSource();
            loopTask = Task.Run(() => ProcessLoopAsync(cts.Token));
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (cts is not null)
            {
                cts.Cancel();
            }

            if (listener.IsListening)
            {
                listener.Stop();
            }

            if (loopTask is not null)
            {
                try
                {
                    await loopTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            listener.Close();
            cts?.Dispose();
        }

        private async Task ProcessLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                await HandleContextAsync(context, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var method = root.GetProperty("method").GetString() ?? string.Empty;
            var jsonRpc = root.GetProperty("jsonrpc").GetString() ?? string.Empty;
            int? id = null;
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.TryGetInt32(out var numericId))
            {
                id = numericId;
            }

            requests.Enqueue(new HttpRequestRecord(method, jsonRpc, id, context.Request.Headers["MCP-Session-Id"]));

            var delay = method switch
            {
                "initialize" => initializeDelay,
                "resources/list" => resourcesListDelay,
                _ => TimeSpan.Zero,
            };
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            switch (method)
            {
                case "initialize":
                    object capabilities = enableSandboxStateCapability
                        ? new
                        {
                            experimental = new Dictionary<string, object?>
                            {
                                [KernelMcpManager.McpSandboxStateCapability] = new { },
                            },
                        }
                        : new Dictionary<string, object?>();
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.Headers["MCP-Session-Id"] = "session-http-1";
                    await WriteJsonAsync(context.Response, new
                    {
                        jsonrpc = "2.0",
                        id,
                        result = new
                        {
                            protocolVersion = "2025-06-18",
                            capabilities,
                            serverInfo = new
                            {
                                name = "http-test",
                                version = "1.0.0",
                            },
                        },
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "notifications/initialized":
                    context.Response.StatusCode = 202;
                    context.Response.Close();
                    break;
                case "tianshu/sandbox-state/update":
                    await WriteJsonAsync(context.Response, new
                    {
                        jsonrpc = "2.0",
                        id,
                        result = new { },
                    }, cancellationToken).ConfigureAwait(false);
                    break;
                case "tools/list":
                    await WriteJsonAsync(context.Response, BuildListToolsResponse(id), cancellationToken).ConfigureAwait(false);
                    break;
                case "resources/list":
                    await WriteJsonAsync(context.Response, BuildListResourcesResponse(root, id), cancellationToken).ConfigureAwait(false);
                    break;
                case "resources/templates/list":
                    await WriteJsonAsync(context.Response, BuildListTemplatesResponse(root, id), cancellationToken).ConfigureAwait(false);
                    break;
                case "resources/read":
                    await WriteJsonAsync(context.Response, BuildReadResponse(root, id), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context.Response, new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32601, message = $"unknown method: {method}" },
                    }, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        private object BuildListToolsResponse(int? id)
        {
            return new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    tools = tools.Select(
                        static name => new
                        {
                            name,
                            description = $"{name} description",
                            inputSchema = new
                            {
                                type = "object",
                                properties = new { },
                            },
                        }),
                },
            };
        }

        private static object BuildListResourcesResponse(JsonElement root, int? id)
        {
            var cursor = root.TryGetProperty("params", out var parameters)
                         && parameters.ValueKind == JsonValueKind.Object
                         && parameters.TryGetProperty("cursor", out var cursorElement)
                ? cursorElement.GetString()
                : null;
            return cursor == "cursor-1"
                ? new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        resources = new object[]
                        {
                            new { uri = "file://docs/chapter-2.md", name = "Chapter 2" },
                        },
                    },
                }
                : new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        resources = new object[]
                        {
                            new { uri = "file://docs/readme.md", name = "README" },
                        },
                        nextCursor = "cursor-1",
                    },
                };
        }

        private static object BuildListTemplatesResponse(JsonElement root, int? id)
        {
            var cursor = root.TryGetProperty("params", out var parameters)
                         && parameters.ValueKind == JsonValueKind.Object
                         && parameters.TryGetProperty("cursor", out var cursorElement)
                ? cursorElement.GetString()
                : null;
            return cursor == "template-cursor-1"
                ? new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        resourceTemplates = new object[]
                        {
                            new { uriTemplate = "docs://{path}?rev=2", name = "Revision Template" },
                        },
                    },
                }
                : new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        resourceTemplates = new object[]
                        {
                            new { uriTemplate = "docs://{path}", name = "Primary Template" },
                        },
                        nextCursor = "template-cursor-1",
                    },
                };
        }

        private static object BuildReadResponse(JsonElement root, int? id)
        {
            var uri = root.GetProperty("params").GetProperty("uri").GetString();
            return new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    contents = new object[]
                    {
                        new { uri, mimeType = "text/plain", text = $"body:{uri}" },
                    },
                },
            };
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
        {
            response.StatusCode = 200;
            response.ContentType = "application/json";
            var json = JsonSerializer.Serialize(payload);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            response.Close();
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
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
    }
}
