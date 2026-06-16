using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;

namespace TianShu.Execution.Integration.Tests;

[Collection("EnvironmentVariables")]
public sealed class AppHostServerPluginProtocolTests
{
    [Fact]
    public async Task RunAsync_ShouldReturnPluginListPayload()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var kernelHome = Path.Combine(root, ".kernel-home");
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = false
                """);
            WriteMarketplacePlugin(root, "debug", "sample", ["connector_example"]);

            var input = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "plugin/list",
                @params = new
                {
                    cwds = new[] { NormalizePath(root) },
                },
            });
            var output = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(input)), output, new KernelThreadStore(storePath));
            await server.RunAsync(CancellationToken.None);

            var messages = output
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                using var message = messages.Single(x => IsResponseId(x.RootElement, 1));
                var marketplaces = message.RootElement.GetProperty("result").GetProperty("marketplaces");
                var marketplace = Assert.Single(marketplaces.EnumerateArray());
                Assert.Equal("debug", marketplace.GetProperty("name").GetString());
                Assert.Equal(NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")), NormalizePath(marketplace.GetProperty("path").GetString()!));
                Assert.False(marketplace.TryGetProperty("marketplacePath", out _));
                var plugin = Assert.Single(marketplace.GetProperty("plugins").EnumerateArray());
                Assert.Equal("sample", plugin.GetProperty("name").GetString());
                Assert.False(plugin.GetProperty("enabled").GetBoolean());
                Assert.Equal("local", plugin.GetProperty("source").GetProperty("type").GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnPluginReadPayload()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var kernelHome = Path.Combine(root, ".kernel-home");
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                """
                [plugins]
                enabled = true
                [plugins.installed."sample@debug"]
                enabled = true
                """);
            WriteMarketplacePlugin(root, "debug", "sample", ["connector_example"]);

            var input = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "plugin/read",
                @params = new
                {
                    marketplacePath = NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")),
                    pluginName = "sample",
                    cwd = NormalizePath(root),
                },
            });
            var output = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(input)), output, new KernelThreadStore(storePath));
            await server.RunAsync(CancellationToken.None);

            var messages = output
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                using var message = messages.Single(x => IsResponseId(x.RootElement, 1));
                var plugin = message.RootElement.GetProperty("result").GetProperty("plugin");
                Assert.Equal("debug", plugin.GetProperty("marketplaceName").GetString());
                Assert.Equal(NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")), NormalizePath(plugin.GetProperty("marketplacePath").GetString()!));

                var summary = plugin.GetProperty("summary");
                Assert.Equal("sample@debug", summary.GetProperty("id").GetString());
                Assert.Equal("sample", summary.GetProperty("name").GetString());
                Assert.True(summary.GetProperty("enabled").GetBoolean());
                Assert.False(summary.GetProperty("installed").GetBoolean());
                Assert.Equal("AVAILABLE", summary.GetProperty("installPolicy").GetString());
                Assert.Equal("ON_INSTALL", summary.GetProperty("authPolicy").GetString());
                Assert.Equal("local", summary.GetProperty("source").GetProperty("type").GetString());
                Assert.Equal(NormalizePath(Path.Combine(root, ".agents", "plugins", "sample")), NormalizePath(summary.GetProperty("source").GetProperty("path").GetString()!));

                var skills = plugin.GetProperty("skills");
                var skill = Assert.Single(skills.EnumerateArray());
                Assert.Equal("sample:search", skill.GetProperty("name").GetString());
                Assert.Equal("sample search", skill.GetProperty("description").GetString());

                var apps = plugin.GetProperty("apps");
                var app = Assert.Single(apps.EnumerateArray());
                Assert.Equal("connector_example", app.GetProperty("id").GetString());

                var mcpServers = plugin.GetProperty("mcpServers");
                Assert.Equal("sample", Assert.Single(mcpServers.EnumerateArray()).GetString());
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldReturnAppsNeedingAuthForPluginInstall()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var kernelHome = Path.Combine(root, ".kernel-home");
        var storePath = Path.Combine(root, "threads.json");
        var originalCurrentDirectory = Environment.CurrentDirectory;
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");
        await using var mockServer = await MockPluginAppsServer.StartAsync();

        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(tianShuHome);
            WriteFile(
                Path.Combine(tianShuHome, "tianshu.toml"),
                $$"""
                chatgpt_base_url = "{{mockServer.BaseUrl}}"

                [plugins]

                enabled = true

                [apps]
                enabled = true
                """);
            WriteChatGptAuth(tianShuHome, "chatgpt-token", "account-123");
            WriteMarketplacePlugin(root, "debug", "sample", ["alpha", "beta"]);

            var input = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "plugin/install",
                @params = new
                {
                    marketplacePath = NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")),
                    pluginName = "sample",
                },
            });
            var output = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(input)), output, new KernelThreadStore(storePath));
            await server.RunAsync(CancellationToken.None);

            var rawOutput = output.ToString();
            var messages = rawOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                var response = messages.SingleOrDefault(x => IsResponseId(x.RootElement, 1));
                Assert.True(response is not null, rawOutput);
                using var message = response!;
                var appsNeedingAuth = message.RootElement.GetProperty("result").GetProperty("appsNeedingAuth");
                var app = Assert.Single(appsNeedingAuth.EnumerateArray());
                Assert.Equal("alpha", app.GetProperty("id").GetString());
                Assert.Equal("Alpha", app.GetProperty("name").GetString());
                Assert.Equal("Alpha connector", app.GetProperty("description").GetString());
                Assert.Equal(JsonValueKind.Null, app.GetProperty("installUrl").ValueKind);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.CurrentDirectory = originalCurrentDirectory;
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldRejectPluginReadWhenRequiredParamsMissing()
    {
        var root = CreateTempDirectory();
        var tianShuHome = Path.Combine(root, ".tianshu-home");
        var kernelHome = Path.Combine(root, ".kernel-home");
        var storePath = Path.Combine(root, "threads.json");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        var originalKernelHome = Environment.GetEnvironmentVariable("TIANSHU_STATE_HOME");

        try
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", kernelHome);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(tianShuHome);

            var input = JsonSerializer.Serialize(new
            {
                id = 1,
                method = "plugin/read",
                @params = new
                {
                    marketplacePath = NormalizePath(Path.Combine(root, ".agents", "plugins", "marketplace.json")),
                },
            });
            var output = new StringWriter();
            var server = new AppHostServer(new StringReader(KernelAppServerTestProtocol.WithInitialize(input)), output, new KernelThreadStore(storePath));
            await server.RunAsync(CancellationToken.None);

            var messages = output
                .ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => JsonDocument.Parse(line))
                .ToArray();
            try
            {
                using var message = messages.Single(x => IsErrorResponseId(x.RootElement, 1));
                var error = message.RootElement.GetProperty("error");
                Assert.Equal(-32602, error.GetProperty("code").GetInt32());
                Assert.Contains("marketplacePath/pluginName", error.GetProperty("message").GetString(), StringComparison.Ordinal);
            }
            finally
            {
                foreach (var message in messages)
                {
                    message.Dispose();
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            Environment.SetEnvironmentVariable("TIANSHU_STATE_HOME", originalKernelHome);
            DeleteDirectory(root);
        }
    }

    private sealed class MockPluginAppsServer : IAsyncDisposable
    {
        private readonly HttpListener listener;
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly Task loopTask;

        private MockPluginAppsServer(HttpListener listener)
        {
            this.listener = listener;
            BaseUrl = listener.Prefixes.Single().TrimEnd('/');
            loopTask = Task.Run(() => RunAsync(cancellationTokenSource.Token));
        }

        public string BaseUrl { get; }

        public static Task<MockPluginAppsServer> StartAsync()
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{GetFreePort()}/");
            listener.Start();
            return Task.FromResult(new MockPluginAppsServer(listener));
        }

        public async ValueTask DisposeAsync()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();
            listener.Close();
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            cancellationTokenSource.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                    await HandleAsync(context, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    context?.Response.OutputStream.Close();
                }
            }
        }

        private static async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var request = context.Request;
            if (!IsAuthorized(request))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }

            if (request.HttpMethod == "GET" && request.Url is not null)
            {
                if (request.Url.AbsolutePath == "/connectors/directory/list")
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        apps = new object[]
                        {
                            new
                            {
                                id = "alpha",
                                name = "Alpha",
                                description = "Alpha connector",
                                installUrl = (string?)null,
                                isAccessible = false,
                                isEnabled = true,
                                pluginDisplayNames = Array.Empty<string>(),
                            },
                            new
                            {
                                id = "beta",
                                name = "Beta",
                                description = "Beta connector",
                                installUrl = (string?)null,
                                isAccessible = false,
                                isEnabled = true,
                                pluginDisplayNames = Array.Empty<string>(),
                            },
                        },
                        nextToken = (string?)null,
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (request.Url.AbsolutePath == "/connectors/directory/list_workspace")
                {
                    await WriteJsonAsync(context.Response, new { apps = Array.Empty<object>(), nextToken = (string?)null }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            if (request.HttpMethod == "POST" && request.Url is not null && request.Url.AbsolutePath == "/api/codex/apps")
            {
                using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
                var payloadText = await reader.ReadToEndAsync().ConfigureAwait(false);
                using var payload = JsonDocument.Parse(payloadText);
                var method = payload.RootElement.GetProperty("method").GetString();
                if (string.Equals(method, "initialize", StringComparison.Ordinal))
                {
                    context.Response.Headers["MCP-Session-Id"] = "session-1";
                    await WriteJsonAsync(context.Response, new
                    {
                        jsonrpc = "2.0",
                        id = payload.RootElement.GetProperty("id").GetInt32(),
                        result = new
                        {
                            protocolVersion = "2025-06-18",
                            capabilities = new { tools = new { } },
                            serverInfo = new { name = "mock-codex-apps", version = "1.0.0" },
                        },
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                    return;
                }

                if (string.Equals(method, "tools/list", StringComparison.Ordinal))
                {
                    context.Response.Headers["MCP-Session-Id"] = "session-1";
                    await WriteJsonAsync(context.Response, new
                    {
                        jsonrpc = "2.0",
                        id = payload.RootElement.GetProperty("id").GetInt32(),
                        result = new
                        {
                            tools = new object[]
                            {
                                new
                                {
                                    name = "connector_beta",
                                    description = "Connector beta tool",
                                    inputSchema = new { type = "object", additionalProperties = false },
                                    _meta = new
                                    {
                                        connector_id = "beta",
                                        connector_name = "Beta App",
                                    },
                                },
                            },
                        },
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }

        private static bool IsAuthorized(HttpListenerRequest request)
        {
            var authorization = request.Headers[HttpRequestHeader.Authorization.ToString()];
            var accountId = request.Headers["chatgpt-account-id"];
            return string.Equals(authorization, "Bearer chatgpt-token", StringComparison.Ordinal)
                && string.Equals(accountId, "account-123", StringComparison.Ordinal);
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }

        private static int GetFreePort()
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

    private static bool IsResponseId(JsonElement element, int id)
        => element.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.GetInt32() == id
           && element.TryGetProperty("result", out _);

    private static bool IsErrorResponseId(JsonElement element, int id)
        => element.TryGetProperty("id", out var idElement)
           && idElement.ValueKind == JsonValueKind.Number
           && idElement.GetInt32() == id
           && element.TryGetProperty("error", out _);

    private static void WriteMarketplacePlugin(string workspace, string marketplaceName, string pluginName, IReadOnlyList<string> appIds)
    {
        var marketplaceRoot = Path.Combine(workspace, ".agents", "plugins");
        var pluginRoot = Path.Combine(marketplaceRoot, pluginName);
        WriteFile(
            Path.Combine(marketplaceRoot, "marketplace.json"),
            $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginName}}",
                  "source": {
                    "source": "local",
                    "path": "./{{pluginName}}"
                  }
                }
              ]
            }
            """);
        WriteFile(
            Path.Combine(pluginRoot, ".tianshu-plugin", "plugin.json"),
            $$"""{"name":"{{pluginName}}"}""");
        WriteFile(
            Path.Combine(pluginRoot, "skills", "search", "SKILL.md"),
            "---\ndescription: sample search\n---\n");
        WriteFile(
            Path.Combine(pluginRoot, ".mcp.json"),
            """
            {
              "mcpServers": {
                "sample": {
                  "command": "rg",
                  "args": ["--version"]
                }
              }
            }
            """);
        var apps = appIds.ToDictionary(appId => appId, appId => (object)new { id = appId });
        WriteFile(
            Path.Combine(pluginRoot, ".app.json"),
            JsonSerializer.Serialize(new { apps }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void WriteChatGptAuth(string tianShuHome, string accessToken, string accountId)
    {
        var idToken = CreateJwt(accountId);
        var authJson = JsonSerializer.Serialize(new
        {
            auth_mode = "chatgpt",
            tokens = new
            {
                access_token = accessToken,
                refresh_token = "refresh-token",
                account_id = accountId,
                id_token = idToken,
            },
        }, new JsonSerializerOptions { WriteIndented = true });
        WriteFile(Path.Combine(tianShuHome, "auth.json"), authJson);
    }

    private static string CreateJwt(string accountId)
    {
        var headerJson = JsonSerializer.Serialize(new { alg = "none", typ = "JWT" });
        var payloadJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["https://api.openai.com/auth"] = new Dictionary<string, object?>
            {
                ["chatgpt_account_id"] = accountId,
            },
        });
        var signatureJson = "signature";
        return $"{Base64UrlEncode(headerJson)}.{Base64UrlEncode(payloadJson)}.{Base64UrlEncode(signatureJson)}";
    }

    private static string Base64UrlEncode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string NormalizePath(string path)
        => path.Replace("\\", "/", StringComparison.Ordinal);

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuTests", Guid.NewGuid().ToString("N"));
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
}
