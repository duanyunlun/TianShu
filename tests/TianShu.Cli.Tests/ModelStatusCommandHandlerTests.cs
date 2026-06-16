using TianShu.AppHost.Catalog;
using TianShu.Cli.Interaction.Commands.ModelStatus;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Configuration;
using TianShu.Provider.Abstractions;

namespace TianShu.Cli.Tests;

public sealed class ModelStatusCommandHandlerTests
{
    [Fact]
    public void BuildProbeJobs_DevelopmentMode_UsesRouteStagePreferredModelsAndResolvedProtocol()
    {
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "local",
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "coding",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "local",
                                        ["model"] = "custom-claude",
                                        ["protocol"] = "auto",
                                    },
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "local",
                                        ["model"] = "fallback-claude",
                                        ["protocol"] = "auto",
                                    },
                                },
                            },
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "review",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "local",
                                        ["model"] = "review-gpt",
                                        ["protocol"] = ProviderWireApi.OpenAiChatCompletions,
                                    },
                                },
                            },
                        },
                    },
                },
                ["providers"] = new Dictionary<string, object?>
                {
                    ["local"] = new Dictionary<string, object?>
                    {
                        ["model_overrides"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["model"] = "custom-claude",
                                ["protocol"] = ProviderWireApi.AnthropicMessages,
                            },
                        },
                    },
                },
            },
        };
        var handler = CreateHandler(config);
        var snapshot = CreateSnapshot(config, provider: "local");
        var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(config.RawConfig);

        var jobs = handler.BuildProbeJobs(routeSet, snapshot, ModelStatusMode.Development);

        Assert.Equal(2, jobs.Length);
        Assert.Equal("coding", jobs[0].RouteKind);
        Assert.Equal("local", jobs[0].Provider);
        Assert.Equal("custom-claude", jobs[0].Model);
        Assert.Equal(ProviderWireApi.AnthropicMessages, jobs[0].Protocol.Id);
        Assert.Equal(ProviderWireApi.AnthropicMessages, jobs[0].Protocol.ConfigValue);
        Assert.Equal("review", jobs[1].RouteKind);
        Assert.Equal("review-gpt", jobs[1].Model);
        Assert.DoesNotContain(jobs, static job => job.Model == "fallback-claude");
    }

    [Fact]
    public void BuildProbeJobs_MatrixMode_ReturnsEveryProtocolCombinationForStageModels()
    {
        var config = new ResolvedTianShuConfig
        {
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "default",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "openai-compatible",
                                        ["model"] = "deepseek-chat",
                                    },
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "openai-compatible",
                                        ["model"] = "deepseek-fallback",
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
        var handler = CreateHandler(config);
        var snapshot = CreateSnapshot(config);
        var routeSet = TianShuModelRouteSetDefaults.ResolveRouteSet(config.RawConfig);

        var jobs = handler.BuildProbeJobs(routeSet, snapshot, ModelStatusMode.Matrix);

        Assert.Equal(4, jobs.Length);
        Assert.All(jobs, static job => Assert.Equal("deepseek-chat", job.Model));
        Assert.Equal(
            [
                ProviderWireApi.OpenAiChatCompletions,
                ProviderWireApi.OpenAiResponses,
                ProviderWireApi.AnthropicMessages,
                ProviderWireApi.GoogleGenerative,
            ],
            jobs.Select(static job => job.Protocol.Id).ToArray());
    }

    [Fact]
    public void BuildProbeGroups_WhenRouteStagesShareProviderModelAndProtocol_UsesOneRealProbeTarget()
    {
        var jobs = new[]
        {
            new ModelStatusProbeJob(
                1,
                "default",
                "openai-compatible",
                "openai-compatible-default",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
            new ModelStatusProbeJob(
                2,
                "planning",
                "openai-compatible",
                "openai-compatible-default",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
            new ModelStatusProbeJob(
                3,
                "coding",
                "openai-compatible",
                "openai-compatible-default",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
        };

        var groups = ModelStatusCommandHandler.BuildProbeGroups(jobs);

        var group = Assert.Single(groups);
        Assert.Equal("openai-compatible", group.Key.Provider);
        Assert.Equal("openai-compatible-default", group.Key.Model);
        Assert.Equal(ProviderWireApi.AnthropicMessages, group.Key.ProtocolConfigValue);
        Assert.Equal(["default", "planning", "coding"], group.Jobs.Select(static job => job.RouteKind).ToArray());
    }

    [Fact]
    public void BuildProbeGroups_WhenRouteStagesUseDifferentModels_KeepsSeparateRealProbeTargets()
    {
        var jobs = new[]
        {
            new ModelStatusProbeJob(
                1,
                "default",
                "openai-compatible",
                "openai-compatible-default",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
            new ModelStatusProbeJob(
                2,
                "planning",
                "openai-compatible",
                "deepseek-v4",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
            new ModelStatusProbeJob(
                3,
                "coding",
                "openai-compatible",
                "deepseek-coder",
                new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages)),
        };

        var groups = ModelStatusCommandHandler.BuildProbeGroups(jobs);

        Assert.Equal(3, groups.Length);
        Assert.Equal(
            ["openai-compatible-default", "deepseek-v4", "deepseek-coder"],
            groups.Select(static group => group.Key.Model).ToArray());
    }

    [Fact]
    public async Task ResolveSnapshotAsync_WhenRootFieldsDiffer_UsesRouteSetPreferredCandidate()
    {
        var config = new ResolvedTianShuConfig
        {
            Model = "root-model",
            ModelProvider = "root-provider",
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "default",
                ["model_route_sets"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["routes"] = new object?[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["kind"] = "default",
                                ["candidates"] = new object?[]
                                {
                                    new Dictionary<string, object?>
                                    {
                                        ["provider"] = "route-provider",
                                        ["model"] = "route-model",
                                        ["protocol"] = ProviderWireApi.AnthropicMessages,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
        var handler = CreateHandler(config);

        var snapshot = await handler.ResolveSnapshotAsync(new CliConsumerFakeRuntime(), new ChatCommandOptions(), CancellationToken.None);

        Assert.Equal("route-model", snapshot.Model);
        Assert.Equal("route-provider", snapshot.Provider);
        Assert.Equal(ProviderWireApi.AnthropicMessages, snapshot.Protocol);
    }

    [Fact]
    public async Task ResolveSnapshotAsync_WhenRouteSetMissing_DoesNotFallbackToRootFields()
    {
        var config = new ResolvedTianShuConfig
        {
            Model = "root-model",
            ModelProvider = "root-provider",
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "missing",
            },
        };
        var handler = CreateHandler(config);

        var snapshot = await handler.ResolveSnapshotAsync(new CliConsumerFakeRuntime(), new ChatCommandOptions(), CancellationToken.None);

        Assert.Equal("<config>", snapshot.Model);
        Assert.Equal("<config>", snapshot.Provider);
        Assert.Equal("<default>", snapshot.Protocol);
    }

    [Fact]
    public async Task ProbeItemAsync_WhenTransientServerFailureSucceedsOnRetry_ReturnsSuccess()
    {
        var calls = 0;
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderWireApi = ProviderWireApi.AnthropicMessages,
            ProviderBaseUrl = "http://127.0.0.1:3001",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
            ProviderStreamMaxRetries = 2,
        };
        var handler = CreateHandler(
            config,
            probeExecutor: (_, _, _, _) =>
            {
                calls++;
                var item = calls == 1
                    ? ProviderModelConnectivityProbeItem.Failed("openai-compatible-default", "/v1/messages", 500, "internal server error")
                    : ProviderModelConnectivityProbeItem.Success("openai-compatible-default", "/v1/messages", 200);
                return Task.FromResult(new ProviderModelConnectivityProbeResult(
                    "openai-compatible",
                    ProviderWireApi.AnthropicMessages,
                    "http://127.0.0.1:3001",
                    "TIANSHU_TEST_KEY",
                    [item]));
            });
        var snapshot = CreateSnapshot(
            config,
            model: "openai-compatible-default",
            provider: "openai-compatible",
            protocol: ProviderWireApi.AnthropicMessages);
        var job = new ModelStatusProbeJob(
            1,
            "default",
            "openai-compatible",
            "openai-compatible-default",
            new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages));

        var (item, _) = await handler.ProbeItemAsync(snapshot, job, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.True(item?.Succeeded, item?.Reason);
    }

    [Fact]
    public async Task ProbeItemAsync_WhenRetryCountIsNotConfigured_DoesNotRetryTransientFailure()
    {
        var calls = 0;
        var config = new ResolvedTianShuConfig
        {
            ModelProvider = "openai-compatible",
            ProviderWireApi = ProviderWireApi.AnthropicMessages,
            ProviderBaseUrl = "http://127.0.0.1:3001",
            ProviderEnvKey = "TIANSHU_TEST_KEY",
        };
        var handler = CreateHandler(
            config,
            probeExecutor: (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult(new ProviderModelConnectivityProbeResult(
                    "openai-compatible",
                    ProviderWireApi.AnthropicMessages,
                    "http://127.0.0.1:3001",
                    "TIANSHU_TEST_KEY",
                    [ProviderModelConnectivityProbeItem.Failed("openai-compatible-default", "/v1/messages", 500, "internal server error")]));
            });
        var snapshot = CreateSnapshot(
            config,
            model: "openai-compatible-default",
            provider: "openai-compatible",
            protocol: ProviderWireApi.AnthropicMessages);
        var job = new ModelStatusProbeJob(
            1,
            "default",
            "openai-compatible",
            "openai-compatible-default",
            new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages));

        var (item, _) = await handler.ProbeItemAsync(snapshot, job, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.False(item?.Succeeded);
    }

    [Fact]
    public void ShouldRetryStatusProbe_RetriesOnlyTransientFailures()
    {
        Assert.True(ModelStatusCommandHandler.ShouldRetryStatusProbe(
            ProviderModelConnectivityProbeItem.Failed("model", "/v1/messages", 500, "server error")));
        Assert.True(ModelStatusCommandHandler.ShouldRetryStatusProbe(
            ProviderModelConnectivityProbeItem.Failed("model", null, null, "HttpRequestException: reset")));
        Assert.False(ModelStatusCommandHandler.ShouldRetryStatusProbe(
            ProviderModelConnectivityProbeItem.Failed("model", "/v1/messages", 400, "bad request")));
        Assert.False(ModelStatusCommandHandler.ShouldRetryStatusProbe(
            ProviderModelConnectivityProbeItem.Failed("model", "/v1/messages", 401, "unauthorized")));
    }

    [Fact]
    public void IsReasoningProbeRequested_UsesProtocolAndModelCapabilities()
    {
        var config = new ResolvedTianShuConfig
        {
            ModelReasoningEnabled = true,
            ProviderWireApi = ProviderWireApi.OpenAiChatCompletions,
        };
        var handler = CreateHandler(config);
        var snapshot = CreateSnapshot(config);

        Assert.True(handler.IsReasoningProbeRequested(
            snapshot,
            new ProviderProbeProtocol(ProviderWireApi.OpenAiChatCompletions, ProviderWireApi.OpenAiChatCompletions),
            "deepseek-chat"));
        Assert.False(handler.IsReasoningProbeRequested(
            snapshot,
            new ProviderProbeProtocol(ProviderWireApi.GoogleGenerative, ProviderWireApi.GoogleGenerative),
            "gemini-2.5-flash"));
        Assert.True(handler.IsReasoningProbeRequested(
            snapshot,
            new ProviderProbeProtocol(ProviderWireApi.AnthropicMessages, ProviderWireApi.AnthropicMessages),
            "claude-sonnet-4"));
    }

    [Fact]
    public void ResolveProbeOutcome_ClassifiesProviderProbeResults()
    {
        Assert.Equal(
            ModelStatusProbeOutcome.Succeeded,
            ModelStatusCommandHandler.ResolveProbeOutcome(ProviderModelConnectivityProbeItem.Success("gpt-5.1", "/v1/responses", 200)));
        Assert.Equal(
            ModelStatusProbeOutcome.Unavailable,
            ModelStatusCommandHandler.ResolveProbeOutcome(ProviderModelConnectivityProbeItem.Failed("model", "/v1/messages", 501, "unavailable")));
        Assert.Equal(
            ModelStatusProbeOutcome.Error,
            ModelStatusCommandHandler.ResolveProbeOutcome(ProviderModelConnectivityProbeItem.Failed("model", null, null, "InvalidOperationException")));
        Assert.Equal(
            ModelStatusProbeOutcome.Failed,
            ModelStatusCommandHandler.ResolveProbeOutcome(ProviderModelConnectivityProbeItem.Failed("model", "/v1/chat/completions", 400, "bad request")));
    }

    [Fact]
    public void FormatReasoningSignal_ReportsObservedRequestedAndMissingSignals()
    {
        Assert.Equal(
            "可见",
            ModelStatusCommandHandler.FormatReasoningSignal(
                ProviderModelConnectivityProbeItem.Success("gpt-5.1", "/v1/responses", 200, hasText: true, hasReasoning: true),
                reasoningRequested: true));
        Assert.Equal(
            "已请求",
            ModelStatusCommandHandler.FormatReasoningSignal(
                ProviderModelConnectivityProbeItem.Success("deepseek-chat", "/v1/chat/completions", 200, hasText: true, hasReasoning: false),
                reasoningRequested: true));
        Assert.Equal(
            "未观测",
            ModelStatusCommandHandler.FormatReasoningSignal(
                ProviderModelConnectivityProbeItem.Success("gpt-4o-mini", "/v1/chat/completions", 200, hasText: true, hasReasoning: false),
                reasoningRequested: false));
        Assert.Equal(
            "-",
            ModelStatusCommandHandler.FormatReasoningSignal(
                ProviderModelConnectivityProbeItem.Failed("model", null, null, "network"),
                reasoningRequested: true));
    }

    [Fact]
    public void ModelStatusCommandHandler_Source_UsesRouteSetCandidatesInsteadOfEndpointModelList()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Presentations",
            "TianShu.Cli",
            "Interaction",
            "Commands",
            "ModelStatus",
            "ModelStatusCommandHandler.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("TianShuModelRouteSetDefaults.ResolveRouteSet(snapshot.Config.RawConfig)", source, StringComparison.Ordinal);
        Assert.Contains("ModelRouteRuntimeComposition.BuildRegisteredRouteKinds(config.RawConfig)", source, StringComparison.Ordinal);
        Assert.Contains("?.Candidates.FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListEndpointModelNamesAsync", source, StringComparison.Ordinal);
    }

    private static ModelStatusCommandHandler CreateHandler(
        ResolvedTianShuConfig config,
        ModelStatusProviderProbeExecutor? probeExecutor = null)
        => new(
            new ModelStatusTableRenderer(static () => 120, static () => true),
            _ => config,
            static () => null,
            probeExecutor);

    private static ModelStatusSnapshot CreateSnapshot(
        ResolvedTianShuConfig config,
        string model = "deepseek-chat",
        string provider = "openai-compatible",
        string protocol = ProviderWireApi.OpenAiChatCompletions)
        => new(
            model,
            provider,
            protocol,
            "https://example.invalid/v1",
            "TIANSHU_TEST_KEY",
            null,
            config,
            TianShuModelRouteSetDefaults.DefaultRouteKinds);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
