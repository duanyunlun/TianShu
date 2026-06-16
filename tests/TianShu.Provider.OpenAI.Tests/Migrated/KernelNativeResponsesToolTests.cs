using System.Net;
using System.Text;
using System.Text.Json;
using TianShu.AppHost;
using TianShu.AppHost.Tools.Runtime;
using TianShu.Contracts.Tools;
using TianShu.Tools.Artifacts;
using TianShu.Tools.Code;
using TianShu.Tools.FileSystem;
using TianShu.Tools.FileSystemMutating;
using TianShu.Tools.McpResources;
using TianShu.Tools.Search;

namespace TianShu.Provider.OpenAI.Tests;

[Collection("EnvironmentVariables")]
public sealed class KernelNativeResponsesToolTests
{
    [Fact]
    public void BuildProviderResponsesToolList_ShouldAppendNativeResponsesTools()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ArtifactToolProvider());
        RegisterProviderTools(registry, new McpResourceToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: "live",
            ImageGenerationEnabled: true,
            WebSearchSupportsImageContent: true,
            McpResourceToolsEnabled: true));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "view_image");
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "web_search"
            && tool.GetProperty("external_web_access").GetBoolean()
            && tool.GetProperty("search_content_types").GetArrayLength() == 2);
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "image_generation");
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "list_mcp_resources");
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "list_mcp_resource_templates");
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "read_mcp_resource");
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldAdvertiseApplyPatchAsCustomWhenFreeformEnabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new MutatingFileSystemToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ApplyPatchFreeform: true));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var applyPatch = Assert.Single(json.RootElement.EnumerateArray().Where(static tool =>
            tool.GetProperty("name").GetString() == "apply_patch"));
        Assert.Equal("custom", applyPatch.GetProperty("type").GetString());
        Assert.Equal("grammar", applyPatch.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal("lark", applyPatch.GetProperty("format").GetProperty("syntax").GetString());
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideApplyPatchWhenDisabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new MutatingFileSystemToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ApplyPatchEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "apply_patch", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideExperimentalFileToolsWhenModelDoesNotAdvertiseThem()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new FileSystemToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ExperimentalSupportedTools: Array.Empty<string>()));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var toolNames = json.RootElement.EnumerateArray()
            .Where(static tool => tool.TryGetProperty("name", out _))
            .Select(static tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.DoesNotContain("list_dir", toolNames);
        Assert.DoesNotContain("read_file", toolNames);
        Assert.DoesNotContain("grep_files", toolNames);
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideViewImageWhenDisabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ArtifactToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ViewImageEnabled: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "view_image", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldOmitViewImageDetailWithoutOriginalCapability()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ArtifactToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ViewImageEnabled: true,
            ViewImageCanRequestOriginalDetail: false));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var viewImage = Assert.Single(json.RootElement.EnumerateArray().Where(static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "view_image", StringComparison.Ordinal)));
        Assert.False(viewImage.GetProperty("parameters").GetProperty("properties").TryGetProperty("detail", out _));
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldIncludeViewImageDetailWithOriginalCapability()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new ArtifactToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            ViewImageEnabled: true,
            ViewImageCanRequestOriginalDetail: true));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var viewImage = Assert.Single(json.RootElement.EnumerateArray().Where(static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "view_image", StringComparison.Ordinal)));
        Assert.True(viewImage.GetProperty("parameters").GetProperty("properties").TryGetProperty("detail", out _));
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldIncludeToolSearchWhenSearchGateEnabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new SearchToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            SearchToolEnabled: true,
            SearchToolConnectorNames: new[] { "Calendar", "Gmail" }));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        var tool = Assert.Single(json.RootElement.EnumerateArray().Where(static item =>
            item.GetProperty("type").GetString() == "tool_search"));
        Assert.Equal("client", tool.GetProperty("execution").GetString());
        Assert.Contains("Calendar, Gmail", tool.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Equal("object", tool.GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideToolSearchWhenSearchGateDisabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new SearchToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            SearchToolEnabled: false,
            SearchToolConnectorNames: new[] { "Calendar" }));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static item =>
            item.GetProperty("type").GetString() == "tool_search");
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldHideToolSuggestWhenSearchGateDisabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new SearchToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            SearchToolEnabled: false,
            ToolSuggestEnabled: true,
            ToolSuggestDiscoverableConnectors:
            [
                new KernelToolSuggestConnectorInfo(
                    "connector_2128aebfecb84f64a069897515042a44",
                    "Google Calendar",
                    "Plan events and schedules.",
                    "https://chatgpt.com/apps/google-calendar/connector_2128aebfecb84f64a069897515042a44"),
            ]));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "tool_suggest", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildProviderResponsesToolList_ShouldIncludeCodeModeToolsWhenEnabled()
    {
        var registry = new KernelToolRegistry();
        RegisterProviderTools(registry, new CodeToolProvider());

        var tools = registry.BuildProviderResponsesToolList(new KernelResponsesNativeToolOptions(
            WebSearchMode: null,
            ImageGenerationEnabled: false,
            CodeModeEnabled: true,
            CodeModeEnabledToolNames: new[] { "shell_command", "view_image", "write_stdin" }));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(tools));
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "custom"
            && tool.GetProperty("name").GetString() == "exec");
        Assert.Contains(json.RootElement.EnumerateArray(), static tool =>
            tool.GetProperty("type").GetString() == "function"
            && tool.GetProperty("name").GetString() == "exec_wait");
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "js_repl", StringComparison.Ordinal));
        Assert.DoesNotContain(json.RootElement.EnumerateArray(), static tool =>
            tool.TryGetProperty("name", out var name)
            && string.Equals(name.GetString(), "js_repl_reset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeConfiguredMcpResourceToolsInProviderRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig(
                """
            [mcp_servers.docs]
            url = "http://127.0.0.1:9/mcp/"
            """));

        const string threadId = "thread_native_mcp_resource_tools_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);
            Assert.True(File.Exists(setupStore.RolloutRecorder.ResolveRolloutPath(threadId)));

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-mcp-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-mcp-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请读取 MCP 资源" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = requestJson.RootElement.GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "function"
                && tool.GetProperty("name").GetString() == "list_mcp_resources");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "function"
                && tool.GetProperty("name").GetString() == "list_mcp_resource_templates");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "function"
                && tool.GetProperty("name").GetString() == "read_mcp_resource");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeConfiguredNativeResponsesToolsInProviderRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig(
                """
                web_search = "cached"

                [features]
                image_generation = true
                """));

        const string threadId = "thread_native_responses_tools_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);
            Assert.True(
                File.Exists(setupStore.RolloutRecorder.ResolveRolloutPath(threadId)),
                $"post-materialize rollout missing: {setupStore.RolloutRecorder.ResolveRolloutPath(threadId)}");

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请回答这个问题" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = requestJson.RootElement.GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "web_search"
                && !tool.GetProperty("external_web_access").GetBoolean());
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "image_generation");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldUseTextOnlyWebSearchWhenBundledModelDoesNotAdvertiseImages()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig(
                """
                model = "gpt-5"
                web_search = "cached"
                """));

        const string threadId = "thread_native_responses_web_search_images_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-web-search-images-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-web-search-images-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请回答这个问题" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var webSearch = Assert.Single(requestJson.RootElement.GetProperty("tools").EnumerateArray().Where(static tool =>
                tool.GetProperty("type").GetString() == "web_search"));
            Assert.False(webSearch.TryGetProperty("search_content_types", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHideApplyPatchWhenModelDoesNotSupportIt()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig("model = \"gpt-5\""));
        await WriteModelsCacheAsync(
            Path.Combine(tianShuHome, "models_cache.json"),
            new
            {
                slug = "gpt-5",
                display_name = "gpt-5",
                description = "cached description",
                default_reasoning_level = "medium",
                supported_reasoning_levels = new[]
                {
                    new
                    {
                        effort = "medium",
                        description = "Balances speed and reasoning depth for everyday tasks",
                    },
                },
                shell_type = "shell_command",
                visibility = "list",
                supported_in_api = true,
                priority = 0,
                base_instructions = "base instructions",
                supports_reasoning_summaries = false,
                default_reasoning_summary = "auto",
                support_verbosity = false,
                supports_parallel_tool_calls = false,
                supports_image_detail_original = false,
                input_modalities = new[] { "text", "image" },
                prefer_websockets = false,
                supports_search_tool = false,
            });

        const string threadId = "thread_native_apply_patch_hidden_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-apply-patch-hidden-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-apply-patch-hidden-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请回答这个问题" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            Assert.DoesNotContain(requestJson.RootElement.GetProperty("tools").EnumerateArray(), static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "apply_patch", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHideExperimentalFileToolsWhenModelDoesNotAdvertiseThem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig("model = \"gpt-5\""));
        await WriteModelsCacheAsync(
            Path.Combine(tianShuHome, "models_cache.json"),
            new
            {
                slug = "gpt-5",
                display_name = "gpt-5",
                description = "cached description",
                default_reasoning_level = "medium",
                supported_reasoning_levels = new[]
                {
                    new
                    {
                        effort = "medium",
                        description = "Balances speed and reasoning depth for everyday tasks",
                    },
                },
                shell_type = "shell_command",
                visibility = "list",
                supported_in_api = true,
                priority = 0,
                base_instructions = "base instructions",
                supports_reasoning_summaries = false,
                default_reasoning_summary = "auto",
                support_verbosity = false,
                supports_parallel_tool_calls = false,
                supports_image_detail_original = false,
                input_modalities = new[] { "text", "image" },
                experimental_supported_tools = Array.Empty<string>(),
                prefer_websockets = false,
                supports_search_tool = false,
            });

        var toolNames = await CaptureProviderToolNamesFromEnvironmentAsync(root, storePath, tianShuHome, "thread_native_experimental_tools_hidden_001");
        Assert.DoesNotContain("list_dir", toolNames);
        Assert.DoesNotContain("read_file", toolNames);
        Assert.DoesNotContain("grep_files", toolNames);
    }

    [Fact]
    public async Task RunAsync_ShouldHideExperimentalFileToolsWhenBundledModelDoesNotAdvertiseThem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig("model = \"gpt-5\""));

        var toolNames = await CaptureProviderToolNamesFromEnvironmentAsync(root, storePath, tianShuHome, "thread_native_experimental_tools_legacy_cache_001");
        Assert.DoesNotContain("list_dir", toolNames);
        Assert.DoesNotContain("read_file", toolNames);
        Assert.DoesNotContain("grep_files", toolNames);
    }

    [Fact]
    public async Task RunAsync_ShouldHideViewImageWhenToolsConfigDisablesIt()
    {
        var toolNames = await CaptureProviderToolNamesAsync(
            """
            [tools]
            view_image = false
            """);

        Assert.DoesNotContain("view_image", toolNames);
    }

    [Fact]
    public async Task RunAsync_ShouldHideRequestUserInputForSubAgentSession()
    {
        var toolNames = await CaptureProviderToolNamesAsync(
            string.Empty,
            KernelSessionSource.SubAgent(KernelSubAgentSource.Review));

        Assert.DoesNotContain("request_user_input", toolNames);
    }

    [Fact]
    public async Task RunAsync_ShouldExposeShellCommandAndRequestPermissions_WhenModelAndFeatureRequireThem()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig(
                """
            [features]
            exec_permission_approvals = true
            request_permissions_tool = true
            """,
                routeModel: "gpt-5.4"));

        const string threadId = "thread_native_shell_command_request_permissions_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-shell-command-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-shell-command-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    model = "gpt-5.4",
                    input = new[]
                    {
                        new { text = "请准备执行命令" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = requestJson.RootElement.GetProperty("tools").EnumerateArray().ToArray();

            Assert.DoesNotContain(tools, static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "shell", StringComparison.Ordinal));
            var shellCommand = Assert.Single(tools.Where(static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "shell_command", StringComparison.Ordinal)));
            var requestPermissions = Assert.Single(tools.Where(static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "request_permissions", StringComparison.Ordinal)));

            var additionalPermissions = shellCommand
                .GetProperty("parameters")
                .GetProperty("properties")
                .GetProperty("additional_permissions")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(static property => property.Name)
                .ToArray();
            Assert.Contains("network", additionalPermissions);
            Assert.Contains("file_system", additionalPermissions);
            Assert.DoesNotContain("macos", additionalPermissions);

            var requestPermissionProperties = requestPermissions
                .GetProperty("parameters")
                .GetProperty("properties")
                .GetProperty("permissions")
                .GetProperty("properties")
                .EnumerateObject()
                .Select(static property => property.Name)
                .ToArray();
            Assert.Contains("network", requestPermissionProperties);
            Assert.Contains("file_system", requestPermissionProperties);
            Assert.DoesNotContain("macos", requestPermissionProperties);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeConfiguredCodeModeToolsInProviderRequest()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig(
                """
            [features]
            code_mode = true
            """));

        const string threadId = "thread_native_code_mode_tools_001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-code-mode-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-code-mode-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请进入 exec 模式" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = requestJson.RootElement.GetProperty("tools");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "custom"
                && tool.GetProperty("name").GetString() == "exec");
            Assert.Contains(tools.EnumerateArray(), static tool =>
                tool.GetProperty("type").GetString() == "function"
                && tool.GetProperty("name").GetString() == "exec_wait");
            Assert.DoesNotContain(tools.EnumerateArray(), static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "js_repl", StringComparison.Ordinal));
            Assert.DoesNotContain(tools.EnumerateArray(), static tool =>
                tool.TryGetProperty("name", out var name)
                && string.Equals(name.GetString(), "js_repl_reset", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldPreferResumeConfigWebSearchOverrideOverGlobalConfig()
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(
            Path.Combine(tianShuHome, "tianshu.toml"),
            WithCurrentOpenAiRouteConfig("web_search = \"live\""));

        const string threadId = "019d38ac-8f4c-7dd3-9210-b98b9d297001";
        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-override-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-override-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var input = string.Join(
                Environment.NewLine,
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "thread/resume",
                    @params = new
                    {
                        threadId,
                        config = new Dictionary<string, object?>
                        {
                            ["web_search"] = "disabled",
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "turn/start",
                    @params = new
                    {
                        threadId,
                        input = new[]
                        {
                            new { text = "只做本地检查" },
                        },
                    },
                }));

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(input));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.DoesNotContain("no rollout found", writer.ToString(), StringComparison.Ordinal);
            var persisted = await threadStore.GetThreadAsync(threadId, CancellationToken.None);
            Assert.NotNull(persisted);
            Assert.NotNull(persisted!.ConfigSnapshot);
            Assert.Equal("disabled", persisted.ConfigSnapshot!.WebSearchMode);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            var tools = requestJson.RootElement.GetProperty("tools");
            Assert.DoesNotContain(tools.EnumerateArray(), static tool =>
                tool.TryGetProperty("type", out var type)
                && string.Equals(type.GetString(), "web_search", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldHideMultiAgentAndFanoutTools_WhenFeaturesAreDisabled()
    {
        var toolNames = await CaptureProviderToolNamesAsync(
            """
            [features]
            multi_agent = false
            enable_fanout = false
            """);

        Assert.DoesNotContain("spawn_agent", toolNames);
        Assert.DoesNotContain("send_input", toolNames);
        Assert.DoesNotContain("resume_agent", toolNames);
        Assert.DoesNotContain("wait", toolNames);
        Assert.DoesNotContain("close_agent", toolNames);
        Assert.DoesNotContain("spawn_agents_on_csv", toolNames);
        Assert.DoesNotContain("report_agent_job_result", toolNames);
    }

    [Fact]
    public async Task RunAsync_ShouldIncludeConfiguredMultiAgentAndFanoutTools_ButKeepWorkerToolHiddenForRegularTurns()
    {
        var toolNames = await CaptureProviderToolNamesAsync(
            """
            [features]
            multi_agent = true
            enable_fanout = true
            """);

        Assert.Contains("spawn_agent", toolNames);
        Assert.Contains("send_input", toolNames);
        Assert.Contains("resume_agent", toolNames);
        Assert.Contains("wait", toolNames);
        Assert.Contains("close_agent", toolNames);
        Assert.Contains("spawn_agents_on_csv", toolNames);
        Assert.DoesNotContain("report_agent_job_result", toolNames);
    }

    private static async Task<string[]> CaptureProviderToolNamesAsync(string configText, KernelSessionSource? sessionSource = null)
    {
        var root = CreateTempDirectory();
        var storePath = Path.Combine(root, "threads.json");
        var tianShuHome = Path.Combine(root, ".tianshu");
        Directory.CreateDirectory(tianShuHome);
        await File.WriteAllTextAsync(Path.Combine(tianShuHome, "tianshu.toml"), WithCurrentOpenAiRouteConfig(configText));
        return await CaptureProviderToolNamesFromEnvironmentAsync(root, storePath, tianShuHome, "thread_native_multi_agent_tools_001", sessionSource);
    }

    private static string WithCurrentOpenAiRouteConfig(string? configText, string routeModel = "gpt-5")
    {
        var trimmed = (configText ?? string.Empty).Trim();
        var firstTableIndex = FindFirstTableHeaderIndex(trimmed);
        var rootConfig = firstTableIndex >= 0 ? trimmed[..firstTableIndex].Trim() : trimmed;
        var tableConfig = firstTableIndex >= 0 ? trimmed[firstTableIndex..].Trim() : string.Empty;

        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(rootConfig))
        {
            segments.Add(rootConfig);
        }

        segments.Add("model_route_set = \"default\"");
        if (!string.IsNullOrWhiteSpace(tableConfig))
        {
            segments.Add(tableConfig);
        }

        segments.Add($$"""
            [providers.openai]
            base_url = "https://api.openai.com/v1"
            api_key_env = "OPENAI_API_KEY"
            default_protocol = "responses"

            [model_route_sets.default]
            display_name = "Default"
            routes = [
              { kind = "default", candidates = [
                { provider = "openai", model = "{{routeModel}}", protocol = "responses" },
              ] },
            ]
            """);

        return string.Join(Environment.NewLine + Environment.NewLine, segments) + Environment.NewLine;
    }

    private static int FindFirstTableHeaderIndex(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var lineEnd = text.IndexOf("\n", lineStart, StringComparison.Ordinal);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            var line = text[lineStart..lineEnd].TrimStart();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                return lineStart;
            }

            lineStart = lineEnd + 1;
        }

        return -1;
    }

    private static async Task<string[]> CaptureProviderToolNamesFromEnvironmentAsync(
        string root,
        string storePath,
        string tianShuHome,
        string threadId,
        KernelSessionSource? sessionSource = null)
    {

        var originalApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        var originalTianShuHome = Environment.GetEnvironmentVariable("TIANSHU_HOME");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "test-key");
        Environment.SetEnvironmentVariable("TIANSHU_HOME", tianShuHome);

        try
        {
            var setupStore = new KernelThreadStore(storePath);
            await setupStore.InitializeAsync(CancellationToken.None);
            _ = await setupStore.CreateThreadAsync(threadId, root, CancellationToken.None);
            if (sessionSource is not null)
            {
                var thread = await setupStore.GetThreadAsync(threadId, CancellationToken.None)
                             ?? throw new InvalidOperationException($"线程不存在：{threadId}");
                var snapshot = thread.ConfigSnapshot ?? BuildTestThreadConfigSnapshot(root);
                thread.ConfigSnapshot = snapshot with
                {
                    SessionSource = sessionSource,
                };
                _ = await setupStore.UpsertThreadAsync(thread, CancellationToken.None);
            }
            await MaterializeThreadRolloutAsync(setupStore, threadId);

            var stream = BuildSseStream(
                JsonSerializer.Serialize(new
                {
                    type = "response.created",
                    response = new { id = "resp-native-multi-agent-1" },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_text.delta",
                    delta = "OK",
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.output_item.done",
                    item = new
                    {
                        type = "message",
                        role = "assistant",
                        content = new object[]
                        {
                            new { type = "output_text", text = "OK" },
                        },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    type = "response.completed",
                    response = new { id = "resp-native-multi-agent-1" },
                }));

            var handler = new CapturingHandler(stream);
            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };

            var inputJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "turn/start",
                @params = new
                {
                    threadId,
                    input = new[]
                    {
                        new { text = "请开始处理" },
                    },
                },
            });

            var threadStore = new KernelThreadStore(storePath);
            var reader = new StringReader(KernelAppServerTestProtocol.WithInitialize(inputJson));
            var writer = new StringWriter();
            var server = new AppHostServer(reader, writer, threadStore, httpClient: httpClient);

            await server.RunAsync(CancellationToken.None);

            Assert.Single(handler.RequestBodies);
            using var requestJson = JsonDocument.Parse(handler.RequestBodies[0]);
            return requestJson.RootElement.GetProperty("tools")
                .EnumerateArray()
                .Where(static tool => tool.TryGetProperty("name", out _))
                .Select(static tool => tool.GetProperty("name").GetString())
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray();
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalApiKey);
            Environment.SetEnvironmentVariable("TIANSHU_HOME", originalTianShuHome);
            DeleteDirectory(root);
        }
    }

    private sealed class CapturingHandler(string responseBody) : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "text/event-stream"),
            };
        }
    }

    private static string BuildSseStream(params string[] jsonEvents)
    {
        var builder = new StringBuilder();
        foreach (var ev in jsonEvents)
        {
            builder.Append("data: ");
            builder.AppendLine(ev);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "TianShuNativeResponsesToolsTests", Guid.NewGuid().ToString("N"));
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

    private static async Task MaterializeThreadRolloutAsync(KernelThreadStore threadStore, string threadId)
    {
        var record = await threadStore.GetThreadAsync(threadId, CancellationToken.None)
                     ?? throw new InvalidOperationException($"线程不存在：{threadId}");
        var snapshot = record.ConfigSnapshot;
        if (snapshot is null)
        {
            snapshot = BuildTestThreadConfigSnapshot(record.Cwd ?? Environment.CurrentDirectory);
            record.ConfigSnapshot = snapshot.DeepClone();
            record = await threadStore.UpsertThreadAsync(record, CancellationToken.None);
        }

        await threadStore.RolloutRecorder.EnsureSessionMetaAsync(
            threadId,
            KernelRolloutStateMapper.ToRolloutThreadRecord(record, snapshot),
            CancellationToken.None);
        await threadStore.RolloutRecorder.CloseAllThreadWritersAsync(CancellationToken.None);
    }

    private static KernelThreadConfigSnapshot BuildTestThreadConfigSnapshot(string cwd)
    {
        var session = new KernelThreadSessionState(
            Model: "gpt-5",
            ModelProvider: "openai",
            ServiceTier: null,
            Cwd: cwd,
            ApprovalPolicy: KernelApprovalPolicy.OnRequest,
            SandboxPolicy: JsonSerializer.SerializeToElement(new { type = "readOnly" }),
            SandboxMode: "readOnly",
            SessionSource: KernelSessionSource.VsCode);
        return KernelThreadConfigSnapshotFactory.FromSession(session);
    }

    private static Task WriteModelsCacheAsync(string cachePath, params object[] models)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        return File.WriteAllTextAsync(
            cachePath,
            JsonSerializer.Serialize(new
            {
                fetched_at = DateTimeOffset.UtcNow.ToString("O"),
                models,
            }));
    }

    private static void RegisterProviderTools(KernelToolRegistry registry, ITianShuToolProvider provider)
    {
        var registrationContext = new TianShuToolRegistrationContext();
        var activationContext = new TianShuToolActivationContext();
        foreach (var descriptor in provider.DescribeTools(registrationContext))
        {
            registry.Register(new KernelContractToolHandlerAdapter(provider.CreateHandler(descriptor.Key, activationContext)));
        }
    }
}
