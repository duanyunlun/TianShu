using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Tools;

namespace TianShu.Contracts.Catalog.Tests;

public sealed class CatalogContractTests
{
    [Fact]
    public void ProviderProfile_RejectsBlankKey()
    {
        Assert.Throws<ArgumentException>(() => new ProviderProfile(
            " ",
            "OpenAI",
            "responses"));
    }

    [Fact]
    public void GetCapabilityCatalog_RejectsNonPositiveModelLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GetCapabilityCatalog(modelLimit: 0));
    }

    [Fact]
    public void CapabilityCatalogSnapshot_PreservesResolvedToolCatalog()
    {
        var tools = new ResolvedToolCatalogSnapshot(
        [
            new ResolvedToolCatalogItem(
                "grep_files",
                "Search files.",
                ToolImplementationKind.Managed,
                available: true,
                modelVisible: true,
                requirements:
                [
                    new ToolRuntimeRequirement("file_system", "File system"),
                ]),
        ]);

        var snapshot = new CapabilityCatalogSnapshot(tools: tools);

        var item = Assert.Single(snapshot.Tools.Items);
        Assert.Equal("grep_files", item.Name);
        Assert.Equal(ToolImplementationKind.Managed, item.ImplementationKind);
        Assert.True(item.Available);
        Assert.True(item.ModelVisible);
        Assert.Equal("file_system", Assert.Single(item.Requirements).Key);
    }

    [Fact]
    public void CapabilityCatalogSnapshot_PreservesModelRouteProjection()
    {
        var modelRoutes = new CapabilityModelRouteSet(
            "workbench",
            [
                new CapabilityModelRoute(
                    "coding",
                    [
                        new CapabilityModelRouteCandidate("openai", "gpt-5-codex", 0, protocol: "responses", capabilities: ["code"]),
                        new CapabilityModelRouteCandidate("anthropic", "claude-3-7-sonnet", 1, protocol: "anthropic_messages"),
                    ],
                    fallbackRouteKind: "default",
                    stageId: "coding"),
            ],
            displayName: "Workbench",
            isVirtual: false);

        var snapshot = new CapabilityCatalogSnapshot(modelRoutes: modelRoutes);

        Assert.NotNull(snapshot.ModelRoutes);
        Assert.Equal("workbench", snapshot.ModelRoutes!.Id);
        Assert.False(snapshot.ModelRoutes.IsVirtual);
        var route = Assert.Single(snapshot.ModelRoutes.Routes);
        Assert.Equal("coding", route.Kind);
        Assert.Equal("coding", route.StageId);
        Assert.Equal("default", route.FallbackRouteKind);
        Assert.Equal(["openai", "anthropic"], route.Candidates.Select(static item => item.ProviderId).ToArray());
        Assert.Equal([0, 1], route.Candidates.Select(static item => item.CandidateIndex).ToArray());
        Assert.Equal("code", Assert.Single(route.Candidates[0].Capabilities));
    }

    [Fact]
    public void EngineBinding_PreservesFallbackPlan()
    {
        var candidate = new EngineBindingCandidate(
            "openai",
            "gpt-5",
            "gpt-5",
            "responses",
            "websocket",
            "default");
        var binding = new EngineBinding(
            "engine-default",
            "openai",
            "gpt-5",
            "gpt-5",
            "responses",
            new CatalogStreamingPreference("websocket", preferWebsocketTransport: true, useWebsocketTransport: true),
            fallbackPlan: new[] { candidate });

        Assert.Single(binding.FallbackPlan);
        Assert.Equal("openai", binding.ProviderKey);
        Assert.Equal("responses", binding.TransportFamily);
        Assert.True(binding.Streaming.UseWebsocketTransport);
    }

    [Fact]
    public void ModelRouteSet_PreservesOrderedCandidates()
    {
        var catalog = new ModelRouteSet(
            "default",
            [
                new ModelRoute(
                    ModelRouteKind.Coding,
                    [
                        new ModelRouteCandidate("openai", "gpt-5-coding", "openai_responses"),
                        new ModelRouteCandidate("anthropic", "claude-opus", "anthropic_messages"),
                    ],
                    fallbackRouteKind: "default"),
                new ModelRoute(
                    ModelRouteKind.Default,
                    [
                        new ModelRouteCandidate("openai", "gpt-5"),
                    ]),
            ],
            displayName: "Default");

        var coding = Assert.Single(catalog.Routes, static route => route.Kind == ModelRouteKind.Coding);
        Assert.Equal(["openai", "anthropic"], coding.Candidates.Select(static candidate => candidate.ProviderId).ToArray());
        Assert.Equal(["gpt-5-coding", "claude-opus"], coding.Candidates.Select(static candidate => candidate.Model).ToArray());
        Assert.Equal("default", coding.FallbackRouteKind);
    }

    [Fact]
    public void ModelRouteContracts_RejectBlankOrEmptyRequiredValues()
    {
        Assert.Throws<ArgumentException>(() => new ModelRouteKind(" "));
        Assert.Throws<ArgumentException>(() => new ModelRouteCandidate(" ", "gpt-5"));
        Assert.Throws<ArgumentException>(() => new ModelRouteCandidate("openai", " "));
        Assert.Throws<ArgumentException>(() => new ModelRoute(ModelRouteKind.Default, []));
        Assert.Throws<ArgumentException>(() => new ModelRouteSet("default", []));
        Assert.Throws<ArgumentException>(() => new ModelRouteSet(" ", [new ModelRoute(ModelRouteKind.Default, [new ModelRouteCandidate("openai", "gpt-5")])]));
        Assert.Throws<ArgumentException>(() => new ModelRouteResolutionRequest(" ", ModelRouteKind.Default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ModelRouteResolutionResult("default", ModelRouteKind.Default, "openai", "gpt-5", -1));
        Assert.Throws<ArgumentException>(() => new ModelRouteResolutionFailure("default", ModelRouteKind.Default, " ", "no route"));
        Assert.Throws<ArgumentException>(() => new ModelRouteResolutionFailure("default", ModelRouteKind.Default, "model_route_not_found", " "));
    }

    [Fact]
    public void ModelRouteResolutionResult_ReportsFallbackCandidate()
    {
        var preferred = new ModelRouteResolutionResult(
            "default",
            ModelRouteKind.Coding,
            "openai",
            "gpt-5-coding",
            candidateIndex: 0);
        var fallback = new ModelRouteResolutionResult(
            "default",
            ModelRouteKind.Coding,
            "anthropic",
            "claude-opus",
            candidateIndex: 1,
            protocol: "anthropic_messages",
            baseUrl: "https://api.anthropic.com",
            apiKeyEnvironmentVariable: "ANTHROPIC_API_KEY",
            reasoningEffort: "high",
            reasoningSummary: "auto",
            verbosity: "medium",
            diagnosticsCorrelationId: "route-123",
            filteredCandidates:
            [
                new ModelRouteCandidateFilterReason(0, "openai", "gpt-5-coding", "provider.unavailable", "首选 provider 不可用。"),
            ]);

        Assert.False(preferred.UsedFallbackCandidate);
        Assert.True(fallback.UsedFallbackCandidate);
        Assert.Equal("anthropic_messages", fallback.Protocol);
        Assert.Equal("ANTHROPIC_API_KEY", fallback.ApiKeyEnvironmentVariable);
        Assert.Equal("high", fallback.ReasoningEffort);
        Assert.Equal("auto", fallback.ReasoningSummary);
        Assert.Equal("medium", fallback.Verbosity);
        Assert.Equal("route-123", fallback.DiagnosticsCorrelationId);
        Assert.Equal("provider.unavailable", Assert.Single(fallback.FilteredCandidates).ReasonCode);
    }

    [Fact]
    public void ModelRouteResolutionFailure_PreservesStructuredUnavailableReasons()
    {
        var failure = new ModelRouteResolutionFailure(
            "default",
            ModelRouteKind.Coding,
            "model_route_no_available_candidate",
            "没有可用候选模型。",
            diagnosticsCorrelationId: "route-456",
            filteredCandidates:
            [
                new ModelRouteCandidateFilterReason(0, "openai", "gpt-5", "provider_missing_secret", "缺少 secret env。"),
            ]);

        Assert.Equal("model_route_no_available_candidate", failure.ReasonCode);
        Assert.Equal("route-456", failure.DiagnosticsCorrelationId);
        Assert.Equal("provider_missing_secret", Assert.Single(failure.FilteredCandidates).ReasonCode);
    }

    [Fact]
    public void ControlPlaneConfigSnapshotResult_PreservesLayersAndOrigins()
    {
        var snapshot = new ControlPlaneConfigSnapshotResult
        {
            Config = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["model"] = StructuredValue.FromString("gpt-5"),
            }),
            Origins = new Dictionary<string, ControlPlaneConfigOrigin>(StringComparer.Ordinal)
            {
                ["model"] = new()
                {
                    Type = "workspace",
                    File = ".tianshu/tianshu.toml",
                    Version = "v1",
                },
            },
            Fields =
            [
                new ControlPlaneConfigField
                {
                    KeyPath = "model",
                    ValueKind = "string",
                    ValueText = "gpt-5",
                    Value = StructuredValue.FromString("gpt-5"),
                    SourceType = "workspace",
                    SourcePath = ".tianshu/tianshu.toml",
                    SourceText = "model = \"gpt-5\"",
                },
            ],
            Layers =
            [
                new ControlPlaneConfigLayer
                {
                    Name = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["type"] = StructuredValue.FromString("workspace"),
                    }),
                    Version = "v1",
                    Config = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["model"] = StructuredValue.FromString("gpt-5"),
                    }),
                },
            ],
        };

        Assert.Equal("gpt-5", snapshot.Config?.Properties["model"].StringValue);
        Assert.Equal("workspace", snapshot.Origins["model"].Type);
        Assert.Equal("gpt-5", snapshot.Fields[0].Value?.StringValue);
        Assert.Equal("workspace", snapshot.Layers[0].Name?.Properties["type"].StringValue);
        Assert.Equal("gpt-5", snapshot.Layers[0].Config?.Properties["model"].StringValue);
    }

    [Fact]
    public void ControlPlanePluginDetail_PreservesSkillAndAppReferences()
    {
        var detail = new ControlPlanePluginDetail
        {
            MarketplaceName = "official",
            MarketplacePath = ".agents/plugins/marketplace.json",
            Summary = new ControlPlanePluginSummary
            {
                Id = "plugin.demo",
                Name = "Demo Plugin",
                Installed = true,
                Enabled = true,
                InstallPolicy = "manual",
                AuthPolicy = "oauth",
                Interface = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["type"] = StructuredValue.FromString("plugin"),
                }),
            },
            Description = "插件详情",
            Skills =
            [
                new ControlPlanePluginSkillReference
                {
                    Name = "catalog.audit",
                    Description = "审计目录",
                    Path = "skills/catalog.audit/SKILL.md",
                    Interface = StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["kind"] = StructuredValue.FromString("skill"),
                    }),
                },
            ],
            Apps =
            [
                new ControlPlanePluginAppReference
                {
                    Id = "app.demo",
                    Name = "Demo App",
                    InstallUrl = "https://example.invalid/install",
                },
            ],
            McpServers = ["catalog-mcp"],
        };

        Assert.Equal("plugin.demo", detail.Summary.Id);
        Assert.Equal("plugin", detail.Summary.Interface?.Properties["type"].StringValue);
        Assert.Equal("skill", detail.Skills[0].Interface?.Properties["kind"].StringValue);
        Assert.Equal("app.demo", detail.Apps[0].Id);
        Assert.Equal("catalog-mcp", Assert.Single(detail.McpServers));
    }
}
