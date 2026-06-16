using TianShu.Contracts.Kernel;

namespace TianShu.Contracts.Modules;

/// <summary>
/// 内置 Module descriptor 工厂，作为当前模块家族接入 Module Plane 的公共投影入口。
/// Built-in module descriptor factory used as the common projection entry for module families.
/// </summary>
public static class BuiltInModuleDescriptors
{
    public static ModuleDescriptor MemoryIdentity(
        string moduleId = "memory.identity",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.MemoryIdentity,
            "Memory / Identity Module",
            "memory.identity.capability",
            "memory.identity.configuration",
            new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory", "identity"], reversible: false, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.IdentityMemory", "MemoryIdentityModule"));

    public static ModuleDescriptor ArtifactStateProjection(
        string moduleId = "artifact.state.projection",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.ArtifactStateProjection,
            "Artifact / State / Projection Module",
            "artifact.state.projection.capability",
            "artifact.state.projection.configuration",
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite, ["artifact", "state", "projection"], reversible: false, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.ArtifactStore", "ArtifactStateProjectionModule"));

    public static ModuleDescriptor Diagnostics(
        string moduleId = "diagnostics",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.Diagnostics,
            "Diagnostics Module",
            "diagnostics.capability",
            "diagnostics.configuration",
            new SideEffectProfile(SideEffectLevel.WorkspaceWrite, ["diagnostics", "trace"], reversible: false, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.Diagnostics", "DiagnosticsModule"));

    public static ModuleDescriptor WorkspaceEnvironment(
        string moduleId = "workspace.environment",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.WorkspaceEnvironment,
            "Workspace / Environment Module",
            "workspace.environment.capability",
            "workspace.environment.configuration",
            new SideEffectProfile(SideEffectLevel.ReadOnly, ["workspace", "environment"], reversible: true, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.RuntimeComposition", "BuiltInWorkspaceEnvironmentModule"));

    public static ModuleDescriptor Configuration(
        string moduleId = "configuration",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.Configuration,
            "Configuration Module",
            "configuration.capability",
            "configuration.configuration",
            new SideEffectProfile(SideEffectLevel.ReadOnly, ["configuration"], reversible: true, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.Configuration", "ConfigurationModule"));

    public static ModuleDescriptor SubAgent(
        string moduleId = "module.sub_agent",
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn)
        => Create(
            moduleId,
            ModuleKind.SubAgentOrchestration,
            "Sub-Agent Orchestration Module",
            "sub_agent.spawn",
            "sub_agent.configuration",
            new SideEffectProfile(SideEffectLevel.HostMutation, ["subagent", "kernel-run"], reversible: false, requiresAudit: true),
            trustLevel,
            new ModuleImplementationBinding("TianShu.SubAgent", "SubAgentOrchestrationModule"));

    private static ModuleDescriptor Create(
        string moduleId,
        ModuleKind kind,
        string displayName,
        string capabilityId,
        string configurationSchemaId,
        SideEffectProfile sideEffects,
        ModuleTrustLevel trustLevel,
        ModuleImplementationBinding implementationBinding)
    {
        var permission = new PermissionEnvelope(
            scopes: [$"module.{ModuleCapabilityDescriptor.NormalizePolicyToken(moduleId)}"],
            requiresHumanGate: false,
            reason: "Built-in module descriptor must still be invoked through Kernel-approved RuntimeStep.");

        var capability = new ModuleCapabilityDescriptor(
            capabilityId,
            displayName,
            inputSchema: new ModuleSchemaRef($"{capabilityId}.input"),
            outputSchema: new ModuleSchemaRef($"{capabilityId}.output"),
            permission: permission,
            sideEffects: sideEffects);

        return new ModuleDescriptor(
            moduleId,
            kind,
            displayName,
            version: "1.0",
            capabilities: [capability],
            configurationSchema: new ModuleSchemaRef(configurationSchemaId),
            permission: permission,
            sideEffects: sideEffects,
            audit: new ModuleAuditProfile(required: true, eventKinds: [$"{moduleId}.invoked"], redactSensitiveValues: true),
            trustLevel: trustLevel,
            health: new ModuleHealthProbe(ModuleHealthStatus.Unknown),
            implementationBinding: implementationBinding);
    }
}
