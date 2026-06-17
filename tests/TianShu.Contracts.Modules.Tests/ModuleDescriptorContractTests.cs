using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Tools;
using TianShu.Provider.Anthropic;
using TianShu.Provider.Google;
using TianShu.Provider.OpenAI;
using TianShu.Provider.OpenAICompatible;

namespace TianShu.Contracts.Modules.Tests;

public sealed class ModuleDescriptorContractTests
{
    [Fact]
    public void ModuleDescriptor_DefaultsFailClosedGovernanceAndUnknownHealth()
    {
        var descriptor = new ModuleDescriptor(
            "custom.module",
            ModuleKind.Custom,
            "Custom Module",
            "1.0");

        Assert.Equal(ModuleKind.Custom, descriptor.Kind);
        Assert.Equal(ModuleTrustLevel.Unspecified, descriptor.TrustLevel);
        Assert.True(descriptor.Permission.RequiresHumanGate);
        Assert.Equal(SideEffectLevel.Unspecified, descriptor.SideEffects.Level);
        Assert.Empty(descriptor.RequiredConfiguration);
        Assert.Empty(descriptor.RuntimeDependencies);
        Assert.Equal("0.0.0", descriptor.MinimumTianShuVersion);
        Assert.Equal(ModuleHealthStatus.Unknown, descriptor.Health.Status);
        Assert.True(descriptor.Audit.Required);
    }

    [Fact]
    public void ModuleDescriptor_ShouldExposeRequiredConfigurationDependenciesAndMinimumVersion()
    {
        var configuration = new ModuleConfigurationRequirement(
            "provider.example.apiKeyEnvironmentVariable",
            "Example API key environment variable",
            required: true,
            secret: true,
            description: "Stores the environment variable name, not the secret value.");
        var dependency = new ModuleRuntimeDependency(
            "example-provider",
            "Example provider assembly",
            ModuleRuntimeDependencyKind.DotNetAssembly,
            versionRange: "[1.0.0,2.0.0)",
            required: true);

        var descriptor = new ModuleDescriptor(
            "provider.example",
            ModuleKind.Provider,
            "Example Provider",
            "1.2.3",
            requiredConfiguration: [configuration],
            runtimeDependencies: [dependency],
            minimumTianShuVersion: "0.6.0");

        Assert.Single(descriptor.RequiredConfiguration);
        Assert.True(descriptor.RequiredConfiguration[0].Required);
        Assert.True(descriptor.RequiredConfiguration[0].Secret);
        Assert.Single(descriptor.RuntimeDependencies);
        Assert.Equal(ModuleRuntimeDependencyKind.DotNetAssembly, descriptor.RuntimeDependencies[0].Kind);
        Assert.Equal("[1.0.0,2.0.0)", descriptor.RuntimeDependencies[0].VersionRange);
        Assert.Equal("0.6.0", descriptor.MinimumTianShuVersion);
    }

    [Fact]
    public void BuiltInModuleDescriptors_ShouldExposeGovernedCapabilities()
    {
        var descriptors = new[]
        {
            BuiltInModuleDescriptors.MemoryIdentity(),
            BuiltInModuleDescriptors.ArtifactStateProjection(),
            BuiltInModuleDescriptors.Diagnostics(),
            BuiltInModuleDescriptors.WorkspaceEnvironment(),
            BuiltInModuleDescriptors.Configuration(),
            BuiltInModuleDescriptors.SubAgent(),
        };

        Assert.All(descriptors, descriptor =>
        {
            Assert.NotEqual(ModuleKind.Unspecified, descriptor.Kind);
            Assert.Equal(ModuleTrustLevel.BuiltIn, descriptor.TrustLevel);
            Assert.NotEmpty(descriptor.Capabilities);
            Assert.NotNull(descriptor.ConfigurationSchema);
            Assert.NotNull(descriptor.ImplementationBinding);
            Assert.NotEqual(SideEffectLevel.Unspecified, descriptor.SideEffects.Level);
            Assert.NotEmpty(descriptor.RequiredConfiguration);
            Assert.NotEmpty(descriptor.RuntimeDependencies);
            Assert.Equal("0.6.0", descriptor.MinimumTianShuVersion);
            Assert.True(descriptor.Audit.Required);
            Assert.Equal(ModuleHealthStatus.Unknown, descriptor.Health.Status);
        });
    }

    [Fact]
    public void ModuleHealthCheck_ShouldExposeDescriptorAndSmokeCheckResult()
    {
        var method = typeof(IModuleHealthCheck).GetMethod(nameof(IModuleHealthCheck.CheckAsync));

        Assert.NotNull(typeof(IModuleHealthCheck).GetProperty(nameof(IModuleHealthCheck.Descriptor)));
        Assert.NotNull(method);
        Assert.Equal(typeof(ValueTask<ModuleSmokeCheckResult>), method!.ReturnType);
        Assert.Equal(typeof(CancellationToken), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Fact]
    public void ModuleDiscoveryResolver_ShouldPreferBuiltInCandidateForDuplicateModuleId()
    {
        var builtInRoot = new ModuleDiscoveryRoot(
            "builtin",
            "builtin://modules",
            ModuleDiscoverySourceKind.BuiltIn,
            ModuleTrustLevel.BuiltIn);
        var thirdPartyRoot = new ModuleDiscoveryRoot(
            "third-party",
            "/tmp/third-party",
            ModuleDiscoverySourceKind.ThirdPartyDirectory,
            ModuleTrustLevel.ThirdParty,
            priority: -100);
        var builtIn = DiscoveryCandidate("provider.openai", ModuleKind.Provider, builtInRoot, priority: 100);
        var duplicate = DiscoveryCandidate("provider.openai", ModuleKind.Provider, thirdPartyRoot, priority: -100);

        var snapshot = ModuleDiscoveryResolver.Resolve([builtInRoot, thirdPartyRoot], [duplicate, builtIn]);

        Assert.Equal(2, snapshot.Candidates.Count);
        Assert.Equal(ModuleDiscoveryCandidateStatus.Selected, snapshot.Candidates.Single(candidate => candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.BuiltIn).Status);
        Assert.Equal(ModuleDiscoveryCandidateStatus.DuplicateRejected, snapshot.Candidates.Single(candidate => candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.ThirdPartyDirectory).Status);
        Assert.Contains(snapshot.Issues, static issue => issue.Code == "module_discovery.duplicate_rejected");
    }

    [Fact]
    public void ModuleDiscoveryResolver_ShouldDisableManifestAndExternalDisableListBeforeSelection()
    {
        var root = new ModuleDiscoveryRoot(
            "user-home",
            "/tmp/modules",
            ModuleDiscoverySourceKind.UserHome,
            ModuleTrustLevel.UserInstalled);
        var manifestDisabled = DiscoveryCandidate("tool.disabled-by-manifest", ModuleKind.Tool, root, enabled: false);
        var policyDisabled = DiscoveryCandidate("tool.disabled-by-policy", ModuleKind.Tool, root);
        var enabled = DiscoveryCandidate("tool.enabled", ModuleKind.Tool, root);

        var snapshot = ModuleDiscoveryResolver.Resolve(
            [root],
            [manifestDisabled, policyDisabled, enabled],
            disabledModuleIds: new HashSet<string>(["tool.disabled-by-policy"], StringComparer.Ordinal));

        Assert.Equal(ModuleDiscoveryCandidateStatus.Disabled, snapshot.Candidates.Single(candidate => candidate.Manifest.ModuleId == "tool.disabled-by-manifest").Status);
        Assert.Equal(ModuleDiscoveryCandidateStatus.Disabled, snapshot.Candidates.Single(candidate => candidate.Manifest.ModuleId == "tool.disabled-by-policy").Status);
        Assert.Equal(ModuleDiscoveryCandidateStatus.Selected, snapshot.Candidates.Single(candidate => candidate.Manifest.ModuleId == "tool.enabled").Status);
        Assert.DoesNotContain(snapshot.SelectedCandidates, static candidate => candidate.Manifest.ModuleId.Contains("disabled", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleDiscoveryResolver_ShouldRejectUnspecifiedKindAsInvalidManifest()
    {
        var root = new ModuleDiscoveryRoot(
            "user-home",
            "/tmp/modules",
            ModuleDiscoverySourceKind.UserHome,
            ModuleTrustLevel.UserInstalled);
        var candidate = DiscoveryCandidate("custom.invalid", ModuleKind.Unspecified, root);

        var snapshot = ModuleDiscoveryResolver.Resolve([root], [candidate]);

        var resolved = Assert.Single(snapshot.Candidates);
        Assert.Equal(ModuleDiscoveryCandidateStatus.ManifestInvalid, resolved.Status);
        Assert.Empty(snapshot.SelectedCandidates);
        Assert.Contains(snapshot.Issues, static issue => issue.Code == "module_manifest.kind_unspecified");
    }

    [Fact]
    public void ModuleDescriptor_IsAllowedByGovernanceEnvelopeOnlyInsidePolicyBoundary()
    {
        var descriptor = new ModuleDescriptor(
            "memory.identity",
            ModuleKind.MemoryIdentity,
            "Memory",
            "1.0",
            permission: new PermissionEnvelope(["module.memory.identity"], requiresHumanGate: false),
            sideEffects: new SideEffectProfile(SideEffectLevel.ReadOnly),
            trustLevel: ModuleTrustLevel.BuiltIn);
        var allowed = new GovernanceEnvelope(
            "governance-module",
            allowedModuleIds: ["memory.identity"],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);
        var denied = new GovernanceEnvelope(
            "governance-module-denied",
            allowedModuleIds: ["diagnostics"],
            maxSideEffectLevel: SideEffectLevel.ReadOnly,
            requiresHumanGate: false);

        Assert.True(descriptor.IsAllowedBy(allowed));
        Assert.False(descriptor.IsAllowedBy(denied));
    }

    [Fact]
    public void ModuleDescriptor_IsAllowedByGovernanceEnvelopeRequiresHumanGateWhenDeclared()
    {
        var descriptor = new ModuleDescriptor(
            "artifact.state.projection",
            ModuleKind.ArtifactStateProjection,
            "Artifact",
            "1.0",
            permission: new PermissionEnvelope(["module.artifact"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.WorkspaceWrite),
            trustLevel: ModuleTrustLevel.BuiltIn);

        Assert.False(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-module-without-gate",
            allowedModuleIds: ["artifact.state.projection"],
            maxSideEffectLevel: SideEffectLevel.WorkspaceWrite,
            requiresHumanGate: false)));
        Assert.True(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-module-with-gate",
            allowedModuleIds: ["artifact.state.projection"],
            maxSideEffectLevel: SideEffectLevel.WorkspaceWrite,
            requiresHumanGate: true)));
    }

    [Fact]
    public void SubAgentDescriptor_ShouldRequireExplicitHostMutationGovernance()
    {
        var descriptor = BuiltInModuleDescriptors.SubAgent();

        Assert.Equal("module.sub_agent", descriptor.ModuleId);
        Assert.Equal(ModuleKind.SubAgentOrchestration, descriptor.Kind);
        Assert.Equal(SideEffectLevel.HostMutation, descriptor.SideEffects.Level);
        Assert.Contains(descriptor.Capabilities, capability => string.Equals(capability.CapabilityId, "sub_agent.spawn", StringComparison.Ordinal));
        Assert.False(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-subagent-denied",
            allowedModuleIds: ["module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.ExternalMutation,
            requiresHumanGate: false)));
        Assert.True(descriptor.IsAllowedBy(new GovernanceEnvelope(
            "governance-subagent-allowed",
            allowedModuleIds: ["module.sub_agent"],
            maxSideEffectLevel: SideEffectLevel.HostMutation,
            requiresHumanGate: false)));
    }

    [Fact]
    public void ProviderDescriptors_ShouldExposeModuleDescriptors()
    {
        var descriptors = new[]
        {
            OpenAiProviderModuleDescriptor.ModuleDescriptor,
            OpenAiCompatibleProviderModuleDescriptor.ModuleDescriptor,
            AnthropicProviderModuleDescriptor.ModuleDescriptor,
            GoogleProviderModuleDescriptor.ModuleDescriptor,
        };

        Assert.All(descriptors, descriptor =>
        {
            Assert.Equal(ModuleKind.Provider, descriptor.Kind);
            Assert.Equal(ModuleTrustLevel.BuiltIn, descriptor.TrustLevel);
            Assert.NotEmpty(descriptor.Capabilities);
            Assert.Equal(SideEffectLevel.ExternalNetwork, descriptor.SideEffects.Level);
            Assert.Contains("network", descriptor.SideEffects.AffectedResources);
            Assert.NotEmpty(descriptor.RequiredConfiguration);
            Assert.Contains(descriptor.RequiredConfiguration, requirement => requirement.Required);
            Assert.NotEmpty(descriptor.RuntimeDependencies);
            Assert.Equal("0.6.0", descriptor.MinimumTianShuVersion);
            Assert.NotNull(descriptor.ImplementationBinding);
        });
    }

    [Fact]
    public void ToolDescriptor_ShouldProjectToModuleDescriptor()
    {
        var tool = new ToolDescriptor(
            "filesystem.read",
            "Filesystem Read",
            "Read workspace files.",
            capabilities: [new ToolCapability("read")],
            concurrencyClass: ToolConcurrencyClass.SharedReadOnly,
            inputSchemaRef: new JsonSchemaRef("tool.filesystem.read.input"),
            outputSchemaRef: new JsonSchemaRef("tool.filesystem.read.output"),
            implementationBinding: new ToolImplementationBinding(
                "filesystem.read",
                ToolImplementationKind.Managed,
                "TianShu.Tools.FileSystem.ReadTool"));

        var descriptor = tool.ToModuleDescriptor();

        Assert.Equal(ModuleKind.Tool, descriptor.Kind);
        Assert.Equal("filesystem.read", descriptor.ModuleId);
        Assert.Equal(ModuleTrustLevel.BuiltIn, descriptor.TrustLevel);
        Assert.Single(descriptor.Capabilities);
        Assert.Equal(SideEffectLevel.ReadOnly, descriptor.SideEffects.Level);
        Assert.Single(descriptor.RequiredConfiguration);
        Assert.False(descriptor.RequiredConfiguration[0].Required);
        Assert.Single(descriptor.RuntimeDependencies);
        Assert.Equal("0.6.0", descriptor.MinimumTianShuVersion);
        Assert.True(descriptor.Audit.Required);
        Assert.NotNull(descriptor.ImplementationBinding);
    }

    [Fact]
    public void ModuleProjects_ShouldNotReferenceUpperPlanesOrExecutionRuntimeImplementations()
    {
        var repoRoot = FindRepoRoot();
        var projectFiles = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "src", "Provider"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "src", "Tools"), "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(repoRoot, "src", "Contracts", "TianShu.Contracts.Modules", "TianShu.Contracts.Modules.csproj"))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(projectFiles);
        foreach (var projectFile in projectFiles)
        {
            var source = File.ReadAllText(projectFile);
            Assert.DoesNotContain("Presentations", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.HostGateway", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.ControlPlane.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Kernel.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Kernel.Adaptive.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.Execution.Runtime.csproj", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TianShu.AppHost", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TianShu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static ModuleDiscoveryCandidate DiscoveryCandidate(
        string moduleId,
        ModuleKind kind,
        ModuleDiscoveryRoot root,
        int priority = 0,
        bool enabled = true)
        => new(new ModuleManifestProjection(
            moduleId,
            kind,
            moduleId,
            "1.0.0",
            new ModuleManifestSource(Path.Combine(root.Path, moduleId, "module.toml"), root),
            enabled: enabled,
            priority: priority));
}
