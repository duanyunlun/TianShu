using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;

namespace TianShu.Contracts.Modules.Tests;

public sealed class ModuleCompositionContractTests
{
    [Fact]
    public async Task ComposeAsync_ShouldRegisterHealthyBuiltInCandidateWithSharedIsolation()
    {
        var root = Root(ModuleDiscoverySourceKind.BuiltIn, ModuleTrustLevel.BuiltIn);
        var descriptor = Descriptor("diagnostics", ModuleKind.Diagnostics, ModuleTrustLevel.BuiltIn, ModuleHealthStatus.Healthy);
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.Selected);
        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.RegisteredRecords);
        Assert.Equal(ModuleLoadStatus.Registered, record.Status);
        Assert.Equal(ModuleIsolationKind.BuiltInShared, record.IsolationBoundary?.Kind);
        var registration = Assert.Single(record.ServiceRegistrations);
        Assert.Equal("diagnostics", registration.ServiceId);
        Assert.Contains("diagnostics.capability", registration.CapabilityIds);
        Assert.Empty(plan.Diagnostics);
    }

    [Fact]
    public async Task ComposeAsync_ShouldRejectSelectedCandidateWithoutDescriptor()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var candidate = Candidate(root, descriptor: null, status: ModuleDiscoveryCandidateStatus.Selected, moduleId: "tool.missing");

        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Rejected, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.descriptor_missing");
        Assert.Empty(plan.RegisteredRecords);
    }

    [Fact]
    public async Task ComposeAsync_ShouldRequireExplicitAllowListForThirdPartyModule()
    {
        var root = Root(ModuleDiscoverySourceKind.ThirdPartyDirectory, ModuleTrustLevel.ThirdParty);
        var descriptor = Descriptor("provider.thirdparty", ModuleKind.Provider, ModuleTrustLevel.ThirdParty, ModuleHealthStatus.Healthy);
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.Selected);

        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Rejected, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.third_party_not_allowed");
    }

    [Fact]
    public async Task ComposeAsync_ShouldRegisterAllowedThirdPartyModuleWithDirectoryIsolation()
    {
        var root = Root(ModuleDiscoverySourceKind.ThirdPartyDirectory, ModuleTrustLevel.ThirdParty);
        var descriptor = Descriptor("provider.thirdparty", ModuleKind.Provider, ModuleTrustLevel.ThirdParty, ModuleHealthStatus.Healthy);
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.Selected);
        var policy = new ModuleLoadingPolicy(
            "0.6.0",
            new HashSet<string>(["provider.thirdparty"], StringComparer.Ordinal));

        var plan = await ComposeAsync([root], [candidate], policy);

        var record = Assert.Single(plan.RegisteredRecords);
        Assert.Equal(ModuleIsolationKind.DirectoryAssemblyLoadContext, record.IsolationBoundary?.Kind);
        Assert.True(record.IsolationBoundary?.Collectible);
        Assert.Single(record.ServiceRegistrations);
    }

    [Theory]
    [InlineData(ModuleHealthStatus.Unknown)]
    [InlineData(ModuleHealthStatus.Disabled)]
    [InlineData(ModuleHealthStatus.Unavailable)]
    public async Task ComposeAsync_ShouldNotRegisterUnhealthyModules(ModuleHealthStatus healthStatus)
    {
        var root = Root(ModuleDiscoverySourceKind.BuiltIn, ModuleTrustLevel.BuiltIn);
        var descriptor = Descriptor("memory.identity", ModuleKind.MemoryIdentity, ModuleTrustLevel.BuiltIn, healthStatus);
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.Selected);

        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Unavailable, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.health_not_healthy");
        Assert.Empty(plan.RegisteredRecords);
    }

    [Fact]
    public async Task ComposeAsync_ShouldSkipCandidatesNotSelectedByDiscovery()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor("tool.duplicate", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy);
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.DuplicateRejected);

        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Skipped, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.candidate_not_selected");
        Assert.Empty(plan.RegisteredRecords);
    }

    [Fact]
    public async Task ComposeAsync_ShouldRejectVersionIncompatibleModule()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor("tool.future", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy, minimumVersion: "99.0.0");
        var candidate = Candidate(root, descriptor, status: ModuleDiscoveryCandidateStatus.Selected);

        var plan = await ComposeAsync([root], [candidate]);

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Rejected, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.version_incompatible");
    }

    [Fact]
    public async Task ComposeAsync_VersionCompatibility_ShouldRegisterOldManifestWithoutOptionalContractVersions()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor("tool.legacy", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy);
        var candidate = Candidate(
            root,
            descriptor,
            status: ModuleDiscoveryCandidateStatus.Selected,
            sdkContractVersion: null,
            capabilitySchemaVersion: null);

        var plan = await ComposeAsync([root], [candidate], new ModuleLoadingPolicy("1.0.0"));

        var record = Assert.Single(plan.RegisteredRecords);
        Assert.Equal("tool.legacy", record.Candidate.Manifest.ModuleId);
        Assert.Empty(record.Diagnostics);
    }

    [Fact]
    public async Task ComposeAsync_VersionCompatibility_ShouldRejectUnknownMajorSdkContractVersion()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor("tool.future-sdk", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy);
        var candidate = Candidate(
            root,
            descriptor,
            status: ModuleDiscoveryCandidateStatus.Selected,
            sdkContractVersion: "2.0.0",
            capabilitySchemaVersion: "1.0.0");

        var plan = await ComposeAsync([root], [candidate], new ModuleLoadingPolicy("1.0.0"));

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Rejected, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.sdk_contract_version_incompatible");
        Assert.Empty(plan.RegisteredRecords);
    }

    [Fact]
    public async Task ComposeAsync_VersionCompatibility_ShouldRejectUnknownMajorCapabilitySchemaVersion()
    {
        var root = Root(ModuleDiscoverySourceKind.UserHome, ModuleTrustLevel.UserInstalled);
        var descriptor = Descriptor("tool.future-schema", ModuleKind.Tool, ModuleTrustLevel.UserInstalled, ModuleHealthStatus.Healthy);
        var candidate = Candidate(
            root,
            descriptor,
            status: ModuleDiscoveryCandidateStatus.Selected,
            sdkContractVersion: "1.0.0",
            capabilitySchemaVersion: "2.0.0");

        var plan = await ComposeAsync([root], [candidate], new ModuleLoadingPolicy("1.0.0"));

        var record = Assert.Single(plan.Records);
        Assert.Equal(ModuleLoadStatus.Rejected, record.Status);
        Assert.Contains(record.Diagnostics, static diagnostic => diagnostic.Code == "module_load.capability_schema_version_incompatible");
        Assert.Empty(plan.RegisteredRecords);
    }

    private static async ValueTask<ModuleAssemblyPlan> ComposeAsync(
        IReadOnlyList<ModuleDiscoveryRoot> roots,
        IReadOnlyList<ModuleDiscoveryCandidate> candidates,
        ModuleLoadingPolicy? policy = null)
    {
        var root = new DefaultModuleCompositionRoot();
        var snapshot = new ModuleDiscoverySnapshot(roots, candidates);
        return await root.ComposeAsync(new ModuleCompositionRootContext(snapshot, policy ?? new ModuleLoadingPolicy("0.6.0")), CancellationToken.None);
    }

    private static ModuleDiscoveryRoot Root(ModuleDiscoverySourceKind sourceKind, ModuleTrustLevel trustLevel)
        => new(sourceKind.ToString().ToLowerInvariant(), Path.Combine(Path.GetTempPath(), sourceKind.ToString()), sourceKind, trustLevel);

    private static ModuleDiscoveryCandidate Candidate(
        ModuleDiscoveryRoot root,
        ModuleDescriptor? descriptor,
        ModuleDiscoveryCandidateStatus status,
        string? moduleId = null,
        string? sdkContractVersion = "0.6.0",
        string? capabilitySchemaVersion = "0.6.0")
    {
        var id = moduleId ?? descriptor!.ModuleId;
        var kind = descriptor?.Kind ?? ModuleKind.Tool;
        var manifest = new ModuleManifestProjection(
            id,
            kind,
            id,
            "1.0.0",
            new ModuleManifestSource(Path.Combine(root.Path, id, "module.toml"), root),
            sdkContractVersion: sdkContractVersion,
            minimumTianShuVersion: descriptor?.MinimumTianShuVersion,
            capabilitySchemaVersion: capabilitySchemaVersion);

        return new ModuleDiscoveryCandidate(manifest, descriptor, status);
    }

    private static ModuleDescriptor Descriptor(
        string moduleId,
        ModuleKind kind,
        ModuleTrustLevel trustLevel,
        ModuleHealthStatus healthStatus,
        string minimumVersion = "0.6.0")
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
            minimumTianShuVersion: minimumVersion,
            health: new ModuleHealthProbe(healthStatus),
            implementationBinding: new ModuleImplementationBinding("TianShu.Tests.Modules", $"{moduleId}.Module"));
}
