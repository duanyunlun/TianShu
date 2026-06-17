using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;

namespace TianShu.Contracts.Modules.Tests;

public sealed class ModuleIntegrationMatrixTests
{
    [Fact]
    public async Task ModuleIntegrationMatrix_ShouldCoverDiscoveryAndCompositionOutcomes()
    {
        var builtInRoot = Root("builtin", ModuleDiscoverySourceKind.BuiltIn, ModuleTrustLevel.BuiltIn);
        var thirdPartyRoot = Root("third-party", ModuleDiscoverySourceKind.ThirdPartyDirectory, ModuleTrustLevel.ThirdParty);
        var userRoot = Root("user-home", ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);

        var builtIn = Candidate(
            builtInRoot,
            Descriptor("module.builtin.diagnostics", ModuleKind.Diagnostics, ModuleTrustLevel.BuiltIn, ModuleHealthStatus.Healthy));
        var thirdPartyFake = Candidate(
            thirdPartyRoot,
            Descriptor("module.fake.provider", ModuleKind.Provider, ModuleTrustLevel.ThirdParty, ModuleHealthStatus.Healthy));
        var duplicateBuiltIn = Candidate(
            builtInRoot,
            Descriptor("module.duplicate", ModuleKind.Tool, ModuleTrustLevel.BuiltIn, ModuleHealthStatus.Healthy));
        var duplicateThirdParty = Candidate(
            thirdPartyRoot,
            Descriptor("module.duplicate", ModuleKind.Tool, ModuleTrustLevel.ThirdParty, ModuleHealthStatus.Healthy),
            priority: -100);
        var damagedManifest = Candidate(
            userRoot,
            descriptor: null,
            "module.damaged.manifest",
            ModuleKind.Unspecified);
        var missingConfig = Candidate(
            userRoot,
            Descriptor(
                "module.missing.config",
                ModuleKind.Provider,
                ModuleTrustLevel.UserInstalled,
                ModuleHealthStatus.Healthy,
                requiredConfiguration:
                [
                    new ModuleConfigurationRequirement(
                        "module.missing.config.apiKeyEnvironmentVariable",
                        "Missing API key environment variable",
                        required: true,
                        secret: true),
                ]));
        var disabled = Candidate(
            userRoot,
            Descriptor("module.disabled", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy),
            enabled: false);
        var healthFailed = Candidate(
            builtInRoot,
            Descriptor("module.health.failed", ModuleKind.MemoryIdentity, ModuleTrustLevel.BuiltIn, ModuleHealthStatus.Unavailable));

        var discovery = ModuleDiscoveryResolver.Resolve(
            [builtInRoot, thirdPartyRoot, userRoot],
            [builtIn, thirdPartyFake, duplicateThirdParty, duplicateBuiltIn, damagedManifest, missingConfig, disabled, healthFailed]);
        var plan = await new DefaultModuleCompositionRoot().ComposeAsync(
            new ModuleCompositionRootContext(
                discovery,
                new ModuleLoadingPolicy(
                    "0.6.0",
                    explicitlyAllowedModuleIds: new HashSet<string>(["module.fake.provider"], StringComparer.Ordinal))),
            CancellationToken.None);

        AssertDiscovery(discovery, "module.builtin.diagnostics", ModuleDiscoveryCandidateStatus.Selected);
        AssertDiscovery(discovery, "module.fake.provider", ModuleDiscoveryCandidateStatus.Selected);
        AssertDiscovery(discovery, "module.damaged.manifest", ModuleDiscoveryCandidateStatus.ManifestInvalid, "module_manifest.kind_unspecified");
        AssertDiscovery(discovery, "module.disabled", ModuleDiscoveryCandidateStatus.Disabled, "module_discovery.disabled");
        AssertDiscovery(discovery, "module.health.failed", ModuleDiscoveryCandidateStatus.Selected);

        var duplicateRecords = discovery.Candidates
            .Where(static candidate => candidate.Manifest.ModuleId == "module.duplicate")
            .ToArray();
        Assert.Equal(ModuleDiscoveryCandidateStatus.Selected, duplicateRecords.Single(static candidate => candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.BuiltIn).Status);
        Assert.Equal(ModuleDiscoveryCandidateStatus.DuplicateRejected, duplicateRecords.Single(static candidate => candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.ThirdPartyDirectory).Status);

        AssertLoad(plan, "module.builtin.diagnostics", ModuleLoadStatus.Registered);
        AssertLoad(plan, "module.fake.provider", ModuleLoadStatus.Registered);
        AssertLoad(plan, "module.duplicate", ModuleLoadStatus.Registered);
        AssertLoad(plan, "module.damaged.manifest", ModuleLoadStatus.Skipped, "module_load.candidate_not_selected");
        AssertLoad(plan, "module.disabled", ModuleLoadStatus.Skipped, "module_load.candidate_not_selected");
        AssertLoad(plan, "module.missing.config", ModuleLoadStatus.Rejected, "module_load.required_configuration_missing");
        AssertLoad(plan, "module.health.failed", ModuleLoadStatus.Unavailable, "module_load.health_not_healthy");

        var registeredIds = plan.RegisteredRecords.Select(static record => record.Candidate.Manifest.ModuleId).ToArray();
        Assert.Contains("module.builtin.diagnostics", registeredIds);
        Assert.Contains("module.fake.provider", registeredIds);
        Assert.DoesNotContain("module.missing.config", registeredIds);
        Assert.DoesNotContain("module.disabled", registeredIds);
    }

    [Fact]
    public async Task ComposeAsync_ShouldRegisterModuleWhenRequiredConfigurationIsBound()
    {
        var root = Root("user-home", ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor(
            "module.configured.provider",
            ModuleKind.Provider,
            ModuleTrustLevel.UserInstalled,
            ModuleHealthStatus.Healthy,
            requiredConfiguration:
            [
                new ModuleConfigurationRequirement(
                    "module.configured.provider.apiKeyEnvironmentVariable",
                    "Configured API key environment variable",
                    required: true,
                    secret: true),
            ]);
        var candidate = Candidate(root, descriptor);
        var discovery = ModuleDiscoveryResolver.Resolve([root], [candidate]);
        var plan = await new DefaultModuleCompositionRoot().ComposeAsync(
            new ModuleCompositionRootContext(
                discovery,
                new ModuleLoadingPolicy(
                    "0.6.0",
                    requireExplicitAllowForThirdParty: true,
                    boundConfigurationKeys: new HashSet<string>(["module.configured.provider.apiKeyEnvironmentVariable"], StringComparer.Ordinal))),
            CancellationToken.None);

        var record = Assert.Single(plan.RegisteredRecords);
        Assert.Equal("module.configured.provider", record.Candidate.Manifest.ModuleId);
        Assert.Empty(record.Diagnostics);
    }

    private static ModuleDiscoveryRoot Root(string rootId, ModuleDiscoverySourceKind sourceKind, ModuleTrustLevel trustLevel)
        => new(rootId, Path.Combine(Path.GetTempPath(), "tianshu-module-matrix", rootId), sourceKind, trustLevel);

    private static ModuleDiscoveryCandidate Candidate(
        ModuleDiscoveryRoot root,
        ModuleDescriptor? descriptor,
        string? moduleId = null,
        ModuleKind? manifestKind = null,
        int priority = 0,
        bool enabled = true)
    {
        var id = moduleId ?? descriptor!.ModuleId;
        var kind = manifestKind ?? descriptor?.Kind ?? ModuleKind.Unspecified;
        var manifest = new ModuleManifestProjection(
            id,
            kind,
            id,
            "1.0.0",
            new ModuleManifestSource(Path.Combine(root.Path, id, "module.toml"), root),
            enabled: enabled,
            priority: priority,
            minimumTianShuVersion: descriptor?.MinimumTianShuVersion,
            implementationBinding: descriptor?.ImplementationBinding);

        return new ModuleDiscoveryCandidate(manifest, descriptor);
    }

    private static ModuleDescriptor Descriptor(
        string moduleId,
        ModuleKind kind,
        ModuleTrustLevel trustLevel,
        ModuleHealthStatus healthStatus,
        IReadOnlyList<ModuleConfigurationRequirement>? requiredConfiguration = null)
        => new(
            moduleId,
            kind,
            moduleId,
            "1.0.0",
            capabilities:
            [
                new ModuleCapabilityDescriptor(
                    $"{moduleId}.capability",
                    $"{moduleId} capability",
                    sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly)),
            ],
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            trustLevel: trustLevel,
            requiredConfiguration: requiredConfiguration,
            minimumTianShuVersion: "0.6.0",
            health: new ModuleHealthProbe(healthStatus),
            implementationBinding: new ModuleImplementationBinding("TianShu.Tests.Modules", $"{moduleId}.Module"));

    private static void AssertDiscovery(
        ModuleDiscoverySnapshot discovery,
        string moduleId,
        ModuleDiscoveryCandidateStatus status,
        string? reason = null)
    {
        var candidate = discovery.Candidates.Single(candidate => string.Equals(candidate.Manifest.ModuleId, moduleId, StringComparison.Ordinal));
        Assert.Equal(status, candidate.Status);
        if (reason is not null)
        {
            Assert.Equal(reason, candidate.StatusReason);
        }
    }

    private static void AssertLoad(
        ModuleAssemblyPlan plan,
        string moduleId,
        ModuleLoadStatus status,
        string? diagnosticCode = null)
    {
        var record = plan.Records
            .Where(record => string.Equals(record.Candidate.Manifest.ModuleId, moduleId, StringComparison.Ordinal))
            .Single(record => status != ModuleLoadStatus.Registered
                              || record.Candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.BuiltIn
                              || moduleId != "module.duplicate");
        Assert.Equal(status, record.Status);
        if (diagnosticCode is not null)
        {
            Assert.Contains(record.Diagnostics, diagnostic => string.Equals(diagnostic.Code, diagnosticCode, StringComparison.Ordinal));
        }
    }
}
