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
        Assert.Equal(ModuleHealthStatus.Unknown, descriptor.Health.Status);
        Assert.True(descriptor.Audit.Required);
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
}
