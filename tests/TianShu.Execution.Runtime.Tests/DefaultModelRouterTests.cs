using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using TianShu.Provider.Abstractions;

namespace TianShu.Execution.Runtime.Tests;

public sealed class DefaultModelRouterTests
{
    [Fact]
    public void Resolve_WhenPreferredCandidateAvailable_AlwaysSelectsPreferred()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"),
            ("fallback", "fallback-model", ProviderWireApi.OpenAiChatCompletions, "FALLBACK_KEY"));
        var context = CreateContext(
            config,
            readEnvironmentVariable: static name => name is "PRIMARY_KEY" or "FALLBACK_KEY" ? "secret" : null);

        var first = DefaultModelRouter.Instance.Resolve(context);
        var second = DefaultModelRouter.Instance.Resolve(context);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("primary", first.Result!.ProviderId);
        Assert.Equal("primary-model", first.Result.Model);
        Assert.Equal(0, first.Result.CandidateIndex);
        Assert.Equal("PRIMARY_KEY", first.Result.ApiKeyEnvironmentVariable);
        Assert.Equal("primary", second.Result!.ProviderId);
        Assert.Empty(first.Result.FilteredCandidates);
    }

    [Fact]
    public void Resolve_WhenPreferredSecretMissing_FallsBackToNextCandidate()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"),
            ("fallback", "fallback-model", ProviderWireApi.OpenAiChatCompletions, "FALLBACK_KEY"));
        var context = CreateContext(
            config,
            readEnvironmentVariable: static name => name == "FALLBACK_KEY" ? "secret" : null);

        var outcome = DefaultModelRouter.Instance.Resolve(context);

        Assert.True(outcome.Succeeded);
        Assert.Equal("fallback", outcome.Result!.ProviderId);
        Assert.Equal("fallback-model", outcome.Result.Model);
        Assert.Equal(1, outcome.Result.CandidateIndex);
        Assert.True(outcome.Result.UsedFallbackCandidate);
        Assert.Equal("provider_missing_secret", Assert.Single(outcome.Result.FilteredCandidates).ReasonCode);
    }

    [Fact]
    public void Resolve_WhenPreferredProviderDisabled_FallsBackToNextCandidate()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"),
            ("fallback", "fallback-model", ProviderWireApi.OpenAiChatCompletions, "FALLBACK_KEY"));
        var providers = (Dictionary<string, object?>)config["providers"]!;
        var primary = (Dictionary<string, object?>)providers["primary"]!;
        primary["enabled"] = false;

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name is "PRIMARY_KEY" or "FALLBACK_KEY" ? "secret" : null));

        Assert.True(outcome.Succeeded);
        Assert.Equal("fallback", outcome.Result!.ProviderId);
        Assert.Equal("provider_disabled", Assert.Single(outcome.Result.FilteredCandidates).ReasonCode);
    }

    [Fact]
    public void Resolve_WhenPreferredProtocolUnavailable_FallsBackToNextCandidate()
    {
        var config = CreateConfig(
            ("primary", "primary-model", "not_a_protocol", "PRIMARY_KEY"),
            ("fallback", "fallback-model", ProviderWireApi.OpenAiChatCompletions, "FALLBACK_KEY"));
        var context = CreateContext(
            config,
            readEnvironmentVariable: static name => name is "PRIMARY_KEY" or "FALLBACK_KEY" ? "secret" : null);

        var outcome = DefaultModelRouter.Instance.Resolve(context);

        Assert.True(outcome.Succeeded);
        Assert.Equal("fallback", outcome.Result!.ProviderId);
        Assert.Equal("protocol_unavailable", Assert.Single(outcome.Result.FilteredCandidates).ReasonCode);
        Assert.Equal(1, outcome.Result.CandidateIndex);
    }

    [Fact]
    public void Resolve_WhenNoCandidateAvailable_ReturnsStructuredFailure()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, null),
            ("fallback", "fallback-model", ProviderWireApi.OpenAiChatCompletions, null));
        var context = CreateContext(config);

        var outcome = DefaultModelRouter.Instance.Resolve(context);

        Assert.False(outcome.Succeeded);
        Assert.Equal("model_route_no_available_candidate", outcome.Failure!.ReasonCode);
        Assert.Equal(["provider_missing_secret", "provider_missing_secret"], outcome.Failure.FilteredCandidates.Select(static item => item.ReasonCode).ToArray());
        Assert.NotNull(outcome.Failure.DiagnosticsCorrelationId);
    }

    [Fact]
    public void Resolve_WhenCatalogMissing_DoesNotUseLegacyModelProviderFallback()
    {
        var config = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model"] = "legacy-model",
            ["provider"] = "legacy-provider",
            ["model_route_set"] = "missing",
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(config, routeSetId: "missing"));

        Assert.False(outcome.Succeeded);
        Assert.Equal("model_route_not_found", outcome.Failure!.ReasonCode);
        Assert.Equal("missing", outcome.Failure.RouteSetId);
        Assert.Empty(outcome.Failure.FilteredCandidates);
    }

    [Fact]
    public void Resolve_WhenRouteKindIsNotRegistered_FailsBeforeReadingRouteSet()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"));

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            registeredRouteKinds: [ModelRouteKind.Default]));

        Assert.False(outcome.Succeeded);
        Assert.Equal("model_route_kind_unregistered", outcome.Failure!.ReasonCode);
        Assert.Equal("coding", outcome.Failure.RouteKind.Value);
    }

    [Fact]
    public void Resolve_WhenRequestedRouteMissing_DoesNotFallbackToDefaultRoute()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"));
        var routeSets = (Dictionary<string, object?>)config["model_route_sets"]!;
        var workbench = (Dictionary<string, object?>)routeSets["workbench"]!;
        workbench["routes"] = new List<object?>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = "default",
                ["candidates"] = new List<object?>
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["provider"] = "primary",
                        ["model"] = "default-model",
                        ["protocol"] = ProviderWireApi.OpenAiChatCompletions,
                    },
                },
            },
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name == "PRIMARY_KEY" ? "secret" : null,
            registeredRouteKinds: [ModelRouteKind.Default, ModelRouteKind.Coding]));

        Assert.False(outcome.Succeeded);
        Assert.Equal("model_route_not_found", outcome.Failure!.ReasonCode);
        Assert.Contains("coding", outcome.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_WhenProviderReasoningConfigured_PinsReasoningOptions()
    {
        var config = CreateConfig(
            ("primary", "primary-model", ProviderWireApi.OpenAiChatCompletions, "PRIMARY_KEY"));
        var providers = (Dictionary<string, object?>)config["providers"]!;
        var primary = (Dictionary<string, object?>)providers["primary"]!;
        primary["reasoning"] = new Dictionary<string, object?>
        {
            ["effort"] = "high",
            ["summary"] = "auto",
            ["verbosity"] = "medium",
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name == "PRIMARY_KEY" ? "secret" : null));

        Assert.True(outcome.Succeeded);
        Assert.Equal("high", outcome.Result!.ReasoningEffort);
        Assert.Equal("auto", outcome.Result.ReasoningSummary);
        Assert.Equal("medium", outcome.Result.Verbosity);
    }

    [Fact]
    public void Resolve_WhenProviderRuleDefinesProtocolPriority_SelectsFirstProtocol()
    {
        var config = CreateConfig(
            ("primary", "openai-compatible-default", "auto", "PRIMARY_KEY"));
        var providers = (Dictionary<string, object?>)config["providers"]!;
        var primary = (Dictionary<string, object?>)providers["primary"]!;
        primary["default_protocol"] = "auto";
        primary["protocol_rules"] = new List<object?>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["match"] = "deepseek-*",
                ["protocols"] = new List<object?>
                {
                    ProviderWireApi.AnthropicMessages,
                    ProviderWireApi.OpenAiChatCompletions,
                },
            },
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name == "PRIMARY_KEY" ? "secret" : null,
            routeSetId: "workbench"));

        Assert.True(outcome.Succeeded);
        Assert.Equal(ProviderWireApi.AnthropicMessages, outcome.Result!.Protocol);
        Assert.Equal("openai-compatible-default", outcome.Result.Model);
    }

    [Fact]
    public void Resolve_WhenDefaultProtocolRuleMatches_UsesRuleSetBeforeBuiltInHeuristic()
    {
        var config = CreateConfig(
            ("primary", "openai-compatible-default", "auto", "PRIMARY_KEY"));
        config["model_protocol_rule_set"] = "default";
        config["model_protocol_rule_sets"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["default"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rules"] = new List<object?>
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["match"] = "deepseek-*",
                        ["protocols"] = new List<object?>
                        {
                            ProviderWireApi.AnthropicMessages,
                            ProviderWireApi.OpenAiChatCompletions,
                        },
                    },
                },
            },
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name == "PRIMARY_KEY" ? "secret" : null,
            routeSetId: "workbench"));

        Assert.True(outcome.Succeeded);
        Assert.Equal(ProviderWireApi.AnthropicMessages, outcome.Result!.Protocol);
        Assert.Equal("openai-compatible-default", outcome.Result.Model);
    }

    [Fact]
    public void Resolve_WhenProviderRuleMatches_OverridesDefaultProtocolRuleSet()
    {
        var config = CreateConfig(
            ("primary", "qwen-plus", "auto", "PRIMARY_KEY"));
        config["model_protocol_rule_set"] = "default";
        config["model_protocol_rule_sets"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["default"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rules"] = new List<object?>
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["match"] = "qwen*",
                        ["protocols"] = new List<object?> { ProviderWireApi.AnthropicMessages },
                    },
                },
            },
        };
        var providers = (Dictionary<string, object?>)config["providers"]!;
        var primary = (Dictionary<string, object?>)providers["primary"]!;
        primary["protocol_rules"] = new List<object?>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["match"] = "qwen*",
                ["protocols"] = new List<object?> { ProviderWireApi.OpenAiChatCompletions },
            },
        };

        var outcome = DefaultModelRouter.Instance.Resolve(CreateContext(
            config,
            readEnvironmentVariable: static name => name == "PRIMARY_KEY" ? "secret" : null,
            routeSetId: "workbench"));

        Assert.True(outcome.Succeeded);
        Assert.Equal(ProviderWireApi.OpenAiChatCompletions, outcome.Result!.Protocol);
        Assert.Equal("qwen-plus", outcome.Result.Model);
    }

    private static DefaultModelRouteResolutionContext CreateContext(
        Dictionary<string, object?> config,
        Func<string, string?>? readEnvironmentVariable = null,
        string routeSetId = "workbench",
        IReadOnlyList<ModelRouteKind>? registeredRouteKinds = null)
        => new(
            StructuredValue.FromPlainObject(config),
            new ModelRouteResolutionRequest(
                routeSetId,
                ModelRouteKind.Coding,
                workspacePath: "D:/repo",
                threadId: "thread-1",
                registeredRouteKinds: registeredRouteKinds),
            DiagnosticsCorrelationId: "route-test",
            ReadEnvironmentVariable: readEnvironmentVariable,
            RequireEnvironmentSecretValue: true);

    private static Dictionary<string, object?> CreateConfig(
        params (string Provider, string Model, string Protocol, string? ApiKeyEnv)[] candidates)
    {
        var providers = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var provider = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["base_url"] = $"https://{candidate.Provider}.example.invalid/v1",
                ["protocol"] = candidate.Protocol,
            };
            if (!string.IsNullOrWhiteSpace(candidate.ApiKeyEnv))
            {
                provider["api_key_env"] = candidate.ApiKeyEnv;
            }

            providers[candidate.Provider] = provider;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["model_route_set"] = "workbench",
            ["providers"] = providers,
            ["model_route_sets"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["workbench"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["routes"] = new List<object?>
                    {
                        new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["kind"] = "coding",
                            ["candidates"] = candidates
                                .Select(candidate => new Dictionary<string, object?>(StringComparer.Ordinal)
                                {
                                    ["provider"] = candidate.Provider,
                                    ["model"] = candidate.Model,
                                    ["protocol"] = candidate.Protocol,
                                })
                                .Cast<object?>()
                                .ToList(),
                        },
                    },
                },
            },
        };
    }
}
