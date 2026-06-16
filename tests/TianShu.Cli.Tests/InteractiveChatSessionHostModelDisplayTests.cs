using TianShu.Cli.Interaction.Host;
using TianShu.Configuration;

namespace TianShu.Cli.Tests;

public sealed class InteractiveChatSessionHostModelDisplayTests
{
    [Fact]
    public void ResolveCurrentModelForDisplay_WhenRuntimeModelSet_UsesRuntimeModel()
    {
        var config = CreateConfig("catalog-model");
        var options = new ChatCommandOptions { RuntimeModel = "runtime-model" };

        var model = InteractiveChatSessionHost.ResolveCurrentModelForDisplay(options, config);

        Assert.Equal("runtime-model", model);
    }

    [Fact]
    public void ResolveCurrentModelForDisplay_WhenRootModelSet_IgnoresRootModelAndUsesRouteSet()
    {
        var config = CreateConfig("catalog-model", rootModel: "root-model");
        var options = new ChatCommandOptions();

        var model = InteractiveChatSessionHost.ResolveCurrentModelForDisplay(options, config);

        Assert.Equal("catalog-model", model);
    }

    [Fact]
    public void ResolveCurrentModelForDisplay_WhenModelOnlyComesFromRouteSet_UsesDefaultRoutePreferredCandidate()
    {
        var config = CreateConfig("openai-compatible-default");
        var options = new ChatCommandOptions();

        var model = InteractiveChatSessionHost.ResolveCurrentModelForDisplay(options, config);

        Assert.Equal("openai-compatible-default", model);
    }

    [Fact]
    public void ResolveCurrentModelDockSummary_WhenModelComesFromRouteSet_IncludesRouteContext()
    {
        var config = CreateConfig("openai-compatible-default", protocol: ProviderWireApi.OpenAiChatCompletions);
        var options = new ChatCommandOptions();

        var summary = InteractiveChatSessionHost.ResolveCurrentModelDockSummary(options, config);

        Assert.Equal("openai-compatible-default", summary.Model);
        Assert.Equal("openai-compatible", summary.Provider);
        Assert.Equal("default", summary.Route);
        Assert.Equal(ProviderWireApi.OpenAiChatCompletions, summary.Protocol);
    }

    [Fact]
    public void ResolveCurrentModelDockSummary_WhenRouteProtocolIsAuto_ResolvesProtocolFromDefaultRules()
    {
        var config = CreateConfig(
            "openai-compatible-default",
            protocol: ProviderWireApi.OpenAiChatCompletions,
            includeDeepSeekProtocolRule: true);
        var options = new ChatCommandOptions();

        var summary = InteractiveChatSessionHost.ResolveCurrentModelDockSummary(options, config);

        Assert.Equal("openai-compatible-default", summary.Model);
        Assert.Equal("openai-compatible", summary.Provider);
        Assert.Equal("default", summary.Route);
        Assert.Equal(ProviderWireApi.OpenAiChatCompletions, summary.Protocol);
    }

    [Fact]
    public void ResolveCurrentModelDockSummary_WhenRouteSetMissing_DoesNotFallbackToRootModel()
    {
        var config = CreateConfigWithoutRouteSet(rootModel: "root-model", rootProvider: "root-provider", rootProtocol: ProviderWireApi.OpenAiChatCompletions);
        var options = new ChatCommandOptions();

        var summary = InteractiveChatSessionHost.ResolveCurrentModelDockSummary(options, config);

        Assert.Equal("<config>", summary.Model);
        Assert.Null(summary.Provider);
        Assert.Null(summary.Route);
        Assert.Null(summary.Protocol);
    }

    private static ResolvedTianShuConfig CreateConfig(
        string routeSetModel,
        string? rootModel = null,
        string? protocol = null,
        bool includeDeepSeekProtocolRule = false)
    {
        var rawConfig = new Dictionary<string, object?>
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
                                    ["model"] = routeSetModel,
                                    ["protocol"] = includeDeepSeekProtocolRule ? "auto" : protocol ?? "auto",
                                },
                            },
                        },
                    },
                },
            },
        };
        if (includeDeepSeekProtocolRule)
        {
            rawConfig["model_protocol_rule_set"] = "default";
            rawConfig["model_protocol_rule_sets"] = new Dictionary<string, object?>
            {
                ["default"] = new Dictionary<string, object?>
                {
                    ["rules"] = new object?[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["match"] = "deepseek*",
                            ["protocols"] = new object?[]
                            {
                                ProviderWireApi.AnthropicMessages,
                                ProviderWireApi.OpenAiChatCompletions,
                            },
                        },
                    },
                },
            };
        }

        return new()
        {
            Model = rootModel,
            ModelProvider = "openai-compatible",
            ProviderWireApi = protocol,
            RawConfig = rawConfig,
        };
    }

    private static ResolvedTianShuConfig CreateConfigWithoutRouteSet(
        string? rootModel = null,
        string? rootProvider = null,
        string? rootProtocol = null)
        => new()
        {
            Model = rootModel,
            ModelProvider = rootProvider,
            ProviderWireApi = rootProtocol,
            RawConfig = new Dictionary<string, object?>
            {
                ["model_route_set"] = "missing",
            },
        };
}
