using TianShu.Contracts.Catalog;
using TianShu.Execution.Runtime;

namespace TianShu.Execution.Integration.Tests;

public sealed class ExecutionProviderCatalogResolverTests
{
    [Fact]
    public void BuildCapabilityCatalog_ShouldPromoteConfiguredProviderAndAggregateCapabilities()
    {
        var config = StructuredValueTestHelper.FromJson(
            """
            {
              "model": "gpt-5.4",
              "provider": "demo",
              "providers": {
                "demo": {
                  "name": "Demo Provider",
                  "base_url": "https://example.invalid/v1",
                  "api_key_env": "DEMO_API_KEY",
                  "default_protocol": "responses",
                  "supports_websockets": true
                }
              }
            }
            """);
        var models = new ControlPlaneModelCatalogResult
        {
            Items =
            [
                new ControlPlaneModelCatalogItem
                {
                    Id = "gpt-5.4",
                    Model = "gpt-5.4",
                    DisplayName = "GPT-5.4",
                    DefaultReasoningEffort = "medium",
                    SupportedReasoningEfforts = ["low", "medium", "high"],
                    InputModalities = ["text", "image"],
                    SupportsParallelToolCalls = true,
                    SupportsReasoningSummaries = true,
                    DefaultReasoningSummary = "auto",
                    SupportsVerbosity = true,
                    DefaultVerbosity = "medium",
                    PreferWebsocketTransport = true,
                    Description = "旗舰模型",
                },
            ],
        };

        var result = ExecutionProviderCatalogResolver.BuildCapabilityCatalog(config, models, includeHiddenModels: true);

        Assert.Equal("demo", result.ActiveProviderKey);
        Assert.Equal("gpt-5.4", result.ActiveModel);
        var provider = Assert.Single(result.Providers);
        Assert.Equal("demo", provider.Key);
        Assert.Equal("Demo Provider", provider.DisplayName);
        Assert.Equal("https://example.invalid/v1", provider.BaseUrl);
        Assert.Equal("DEMO_API_KEY", provider.ApiKeyEnvironmentVariable);
        Assert.Equal("responses", provider.TransportFamily);
        Assert.True(provider.SupportsWebsockets);
        Assert.Equal(["responses.http", "responses.websocket"], provider.TransportModes);
        Assert.Contains(provider.SupportedCapabilities, static item => item.Name == "websocket_transport" && item.Supported);
        var model = Assert.Single(provider.Models);
        Assert.Equal("gpt-5.4", model.Key);
        Assert.Equal("gpt-5.4", model.Model);
        Assert.True(model.SupportsParallelToolCalls);
        Assert.True(model.SupportsReasoningSummaries);
        Assert.True(model.SupportsVerbosity);
        Assert.True(model.PreferWebsocketTransport);
    }

    [Fact]
    public void BuildCapabilityCatalog_ShouldExposeActiveModelRouteProjection()
    {
        var config = StructuredValueTestHelper.FromJson(
            """
            {
              "profile": "work",
              "model": "gpt-5",
              "provider": "openai",
              "profiles": {
                "work": {
                  "model_route_set": "workbench"
                }
              },
              "model_route_sets": {
                "workbench": {
                  "display_name": "Workbench Catalog",
                  "routes": [
                    {
                      "kind": "coding",
                      "fallback": "default",
                      "candidates": [
                        {
                          "provider": "openai",
                          "model": "gpt-5-codex",
                          "protocol": "responses",
                          "capabilities": ["code"]
                        },
                        {
                          "provider": "anthropic",
                          "model": "claude-3-7-sonnet",
                          "protocol": "anthropic_messages"
                        }
                      ]
                    },
                    {
                      "kind": "review",
                      "candidates": [
                        {
                          "provider": "anthropic",
                          "model": "claude-3-7-sonnet"
                        }
                      ]
                    }
                  ]
                }
              }
            }
            """);
        var models = new ControlPlaneModelCatalogResult();

        var result = ExecutionProviderCatalogResolver.BuildCapabilityCatalog(config, models, includeHiddenModels: true);

        Assert.NotNull(result.ModelRoutes);
        Assert.Equal("workbench", result.ModelRoutes!.Id);
        Assert.Equal("Workbench Catalog", result.ModelRoutes.DisplayName);
        Assert.False(result.ModelRoutes.IsVirtual);
        Assert.Equal(["coding", "review"], result.ModelRoutes.Routes.Select(static route => route.Kind).ToArray());

        var coding = result.ModelRoutes.Routes[0];
        Assert.Equal("coding", coding.StageId);
        Assert.Equal("default", coding.FallbackRouteKind);
        Assert.Equal(["openai", "anthropic"], coding.Candidates.Select(static item => item.ProviderId).ToArray());
        Assert.Equal(["gpt-5-codex", "claude-3-7-sonnet"], coding.Candidates.Select(static item => item.Model).ToArray());
        Assert.Equal([0, 1], coding.Candidates.Select(static item => item.CandidateIndex).ToArray());
        Assert.Equal("responses", coding.Candidates[0].Protocol);
        Assert.Equal("code", Assert.Single(coding.Candidates[0].Capabilities));

        var review = result.ModelRoutes.Routes[1];
        Assert.Equal("review", review.StageId);
        Assert.Equal("claude-3-7-sonnet", review.Candidates[0].Model);
        Assert.Equal(coding.Candidates[1].Model, review.Candidates[0].Model);
    }

    [Fact]
    public void BuildCapabilityCatalog_WhenCatalogMissing_ShouldNotExposeVirtualDefaultRoutes()
    {
        var config = StructuredValueTestHelper.FromJson(
            """
            {
              "model": "gpt-5-mini",
              "provider": "demo"
            }
            """);
        var models = new ControlPlaneModelCatalogResult();

        var result = ExecutionProviderCatalogResolver.BuildCapabilityCatalog(config, models, includeHiddenModels: true);

        Assert.Null(result.ModelRoutes);
    }

    [Fact]
    public void ResolveEngineBinding_ShouldResolveRequestedProviderAndEmitFallbackCandidates()
    {
        var config = StructuredValueTestHelper.FromJson(
            """
            {
              "model": "gpt-5.4",
              "provider": "demo",
              "model_reasoning_effort": "medium",
              "model_reasoning_summary": "auto",
              "model_verbosity": "medium",
              "providers": {
                "demo": {
                  "base_url": "https://example.invalid/v1",
                  "api_key_env": "DEMO_API_KEY",
                  "default_protocol": "responses",
                  "supports_websockets": true
                },
                "openai": {
                  "base_url": "https://api.openai.com/v1",
                  "api_key_env": "OPENAI_API_KEY",
                  "default_protocol": "responses",
                  "supports_websockets": false
                }
              }
            }
            """);
        var models = new ControlPlaneModelCatalogResult
        {
            Items =
            [
                new ControlPlaneModelCatalogItem
                {
                    Id = "gpt-5.4",
                    Model = "gpt-5.4",
                    DisplayName = "GPT-5.4",
                    DefaultReasoningEffort = "medium",
                    SupportedReasoningEfforts = ["low", "medium", "high"],
                    InputModalities = ["text", "image"],
                    SupportsParallelToolCalls = true,
                    SupportsReasoningSummaries = true,
                    DefaultReasoningSummary = "auto",
                    SupportsVerbosity = true,
                    DefaultVerbosity = "medium",
                    PreferWebsocketTransport = false,
                },
            ],
        };

        var result = ExecutionProviderCatalogResolver.ResolveEngineBinding(
            config,
            models,
            new ResolveEngineBinding(
                PreferredProviderKey: "demo",
                PreferredModelKey: "gpt-5.4",
                ReasoningEffort: "high",
                PreferWebsocketTransport: true));

        Assert.NotNull(result.Binding);
        var binding = result.Binding!;
        Assert.Equal("kernel", binding.EngineKey);
        Assert.Equal("demo", binding.ProviderKey);
        Assert.Equal("gpt-5.4", binding.Model);
        Assert.Equal("responses", binding.TransportFamily);
        Assert.Equal("https://example.invalid/v1", binding.BaseUrl);
        Assert.Equal("DEMO_API_KEY", binding.ApiKeyEnvironmentVariable);
        Assert.True(binding.SupportsWebsockets);
        Assert.Equal("high", binding.Reasoning.Effort);
        Assert.Equal("auto", binding.Reasoning.Summary);
        Assert.Equal("medium", binding.Reasoning.Verbosity);
        Assert.Equal("responses.websocket", binding.Streaming.TransportMode);
        Assert.True(binding.Streaming.PreferWebsocketTransport);
        Assert.True(binding.Streaming.UseWebsocketTransport);
        Assert.Single(binding.FallbackPlan);
        Assert.Equal("openai", binding.FallbackPlan[0].ProviderKey);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("demo", result.Candidates[0].ProviderKey);
        Assert.True(result.Candidates[0].IsSelected);
        Assert.Equal("fallback", result.Candidates[1].SelectionReason);
    }
}
