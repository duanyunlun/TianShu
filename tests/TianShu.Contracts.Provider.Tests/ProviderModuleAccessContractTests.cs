using TianShu.Contracts.Kernel;
using TianShu.Contracts.Provider;

namespace TianShu.Contracts.Provider.Tests;

public sealed class ProviderModuleAccessContractTests
{
    [Fact]
    public void Validate_ShouldCreateAccessDescriptorWhenManifestMatchesDescriptorAndRouteSet()
    {
        var descriptor = Descriptor();
        var manifest = Manifest();

        var result = ProviderModuleAccessValidator.Validate(manifest, descriptor, "default");

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Access);
        Assert.Equal("openai", result.Access!.Manifest.ProviderId);
        Assert.Equal("openai_responses", result.Access.ProtocolBinding.WireApi);
        Assert.Equal("gpt-5.5", Assert.Single(result.Access.ModelRouteSet.Candidates).Model);
        Assert.Equal(ProviderErrorKind.RateLimited, Assert.Single(result.Access.ErrorSpecs).Kind);
    }

    [Fact]
    public void Validate_ShouldFailClosedWhenProviderIdDiffers()
    {
        var result = ProviderModuleAccessValidator.Validate(Manifest(providerId: "other"), Descriptor(), "default");

        Assert.False(result.IsValid);
        Assert.Null(result.Access);
        Assert.Contains(result.Issues, static issue => issue.Code == "provider_access.provider_id_mismatch");
    }

    [Fact]
    public void Validate_ShouldFailClosedWhenProtocolBindingMissing()
    {
        var descriptor = Descriptor(protocolKind: ProviderProtocolKind.AnthropicMessages);
        var result = ProviderModuleAccessValidator.Validate(Manifest(), descriptor, "default");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, static issue => issue.Code == "provider_access.protocol_binding_missing");
    }

    [Fact]
    public void Validate_ShouldFailClosedWhenRouteSetHasNoEnabledCandidateForBinding()
    {
        var manifest = Manifest(candidates:
        [
            new ProviderModelRouteCandidate("openai", "gpt-5.5", "openai_responses", enabled: false),
            new ProviderModelRouteCandidate("openai", "gpt-5.5", "openai_chat_completions", enabled: true),
        ]);

        var result = ProviderModuleAccessValidator.Validate(manifest, Descriptor(), "default");

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, static issue => issue.Code == "provider_access.route_set_no_enabled_candidate");
    }

    [Fact]
    public void ProjectUsage_ShouldMarkProviderUsageAsRealAndCalculateTotal()
    {
        var usage = ProviderModuleAccessValidator.ProjectUsage(new ProviderUsage(10, 20, 3), "provider.openai.responses");

        Assert.True(usage.Available);
        Assert.False(usage.Estimated);
        Assert.Equal(10, usage.InputTokens);
        Assert.Equal(20, usage.OutputTokens);
        Assert.Equal(3, usage.ReasoningTokens);
        Assert.Equal(33, usage.TotalTokens);
        Assert.Null(usage.MissingReason);
    }

    [Fact]
    public void ProjectUsage_ShouldRecordMissingReasonWhenProviderDoesNotReturnUsage()
    {
        var usage = ProviderModuleAccessValidator.ProjectUsage(null, "provider.openai.responses");

        Assert.False(usage.Available);
        Assert.False(usage.Estimated);
        Assert.Equal("provider_usage_missing", usage.MissingReason);
    }

    [Fact]
    public void ProjectCost_ShouldRequireRealUsageAndPriceModel()
    {
        var missingUsageCost = ProviderModuleAccessValidator.ProjectCost(
            ProviderModuleAccessValidator.ProjectUsage(null, "provider.openai.responses"),
            estimatedCost: 0.01m,
            currency: "USD",
            priceModelVersion: "2026-06");
        var realUsageCost = ProviderModuleAccessValidator.ProjectCost(
            ProviderModuleAccessValidator.ProjectUsage(new ProviderUsage(10, 20), "provider.openai.responses"),
            estimatedCost: 0.01m,
            currency: "USD",
            priceModelVersion: "2026-06");

        Assert.False(missingUsageCost.Available);
        Assert.Equal("provider_usage_not_real", missingUsageCost.MissingReason);
        Assert.True(realUsageCost.Available);
        Assert.Equal(0.01m, realUsageCost.EstimatedCost);
    }

    [Fact]
    public void ProviderMetricsProjection_ShouldPreserveUsageCostAndAttribution()
    {
        var usage = ProviderModuleAccessValidator.ProjectUsage(new ProviderUsage(3, 7), "provider.openai.responses");
        var cost = ProviderModuleAccessValidator.ProjectCost(usage, 0.001m, "USD", "test-price");

        var metrics = new ProviderMetricsProjection(
            "openai",
            "gpt-5.5",
            "openai_responses",
            usage,
            cost,
            TimeSpan.FromMilliseconds(42),
            attemptIndex: 1,
            diagnosticsRefs: ["diagnostics://provider/openai/1"]);

        Assert.Equal("openai", metrics.ProviderId);
        Assert.Equal("gpt-5.5", metrics.Model);
        Assert.Equal("openai_responses", metrics.WireApi);
        Assert.Equal(10, metrics.Usage.TotalTokens);
        Assert.True(metrics.Cost.Available);
        Assert.Equal("diagnostics://provider/openai/1", Assert.Single(metrics.DiagnosticsRefs));
    }

    private static ProviderDescriptor Descriptor(ProviderProtocolKind protocolKind = ProviderProtocolKind.OpenAiResponses)
        => new(
            "openai",
            "OpenAI",
            protocolKind,
            new ProviderCapabilityProfile(SupportsStreaming: true, SupportsTools: true, SupportsReasoning: true),
            [new ProviderModelDescriptor("gpt-5.5")],
            new ProviderEndpointDescriptor("openai", protocolKind, "https://api.openai.example/v1", "OPENAI_API_KEY"),
            new PermissionEnvelope(["provider.openai.invoke"], requiresHumanGate: false),
            new SideEffectProfile(SideEffectLevel.ExternalNetwork));

    private static ProviderModuleManifest Manifest(
        string providerId = "openai",
        IReadOnlyList<ProviderModelRouteCandidate>? candidates = null)
        => new(
            providerId,
            "OpenAI",
            "1.0.0",
            "0.6.0",
            protocolBindings:
            [
                new ProviderProtocolBinding(
                    "openai_responses",
                    ProviderProtocolKind.OpenAiResponses,
                    new ProviderCapabilityProfile(SupportsStreaming: true, SupportsTools: true, SupportsReasoning: true)),
            ],
            modelRouteSets:
            [
                new ProviderModelRouteSet(
                    "default",
                    candidates ?? [new ProviderModelRouteCandidate(providerId, "gpt-5.5", "openai_responses")],
                    defaultModel: "gpt-5.5"),
            ],
            endpoint: new ProviderEndpointDescriptor(providerId, ProviderProtocolKind.OpenAiResponses, "https://api.openai.example/v1", "OPENAI_API_KEY"),
            errorSpecs:
            [
                new ProviderErrorSpec("rate_limited", ProviderErrorKind.RateLimited, retryable: true, safeForUser: true, remediation: "Retry after backoff."),
            ],
            diagnostics: ["provider.access"]);
}
