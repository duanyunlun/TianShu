using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Template.MemoryModule;

/// <summary>
/// 自定义 Memory 模块模板：演示 retrieve / form / supersede / compression-reserved 的公开接入边界。
/// Custom Memory module template showing public boundaries for retrieve, form, supersede, and compression reservation.
/// </summary>
public sealed class TemplateMemoryModule : IMemoryModule
{
    public const string ModuleId = "template.memory";
    public const string ProviderId = "template.memory.local";

    private readonly List<FactMemoryRecord> records = [];

    public ModuleDescriptor Descriptor { get; } = CreateDescriptor();

    public static MemoryModuleManifest CreateManifest()
        => new(
            ModuleId,
            "Template Memory Module",
            "1.0.0",
            "0.6.0",
            providers: [CreateProvider()],
            capabilities:
            [
                Capability("template.memory.retrieve", MemoryModuleCapabilityKind.Retrieve, MemoryProviderCapability.Filter | MemoryProviderCapability.ReadOnlyAccess, SideEffectLevel.ReadOnly, requiresHumanGate: false),
                Capability("template.memory.form", MemoryModuleCapabilityKind.Form, MemoryProviderCapability.Add | MemoryProviderCapability.Extract, SideEffectLevel.ExternalMutation, requiresHumanGate: true),
                Capability("template.memory.supersede", MemoryModuleCapabilityKind.Supersede, MemoryProviderCapability.Supersede, SideEffectLevel.ExternalMutation, requiresHumanGate: true),
                Capability("template.memory.compress", MemoryModuleCapabilityKind.CompressReserved, MemoryProviderCapability.None, SideEffectLevel.ReadOnly, requiresHumanGate: true, executable: false),
            ],
            new MemoryContextPolicyBinding(
                ContextSourceKind.MemoryRecord,
                ContextProjectionMode.ReferenceOnly,
                requireEvidenceRefs: true,
                moduleMaySliceContext: false),
            compressionReservations:
            [
                new MemoryCompressionReservation("template.memory.compress.v1", "Reserved compression interface for future context policy integration."),
            ],
            diagnostics: ["template.memory.access"]);

    public static GovernanceEnvelope CreateGovernance()
        => new(
            "template-memory-governance",
            allowedModuleIds: [ModuleId],
            maxSideEffectLevel: SideEffectLevel.ExternalMutation,
            requiresHumanGate: true);

    public static ApprovedContextPolicy CreateApprovedContextPolicy()
        => new(
            new ContextPolicy(
                policyId: "template.memory.context",
                allowedSourceKinds: [nameof(ContextSourceKind.MemoryRecord)],
                requireEvidenceRefs: true,
                sourceRules:
                [
                    new ContextSourceRule(
                        ContextSourceKind.MemoryRecord,
                        priority: 40,
                        projectionMode: ContextProjectionMode.ReferenceOnly,
                        requireEvidenceRef: true),
                ]),
            new CoreIntentId("intent-template-memory"),
            new StageGraphId("graph-template-memory"),
            new StageId("stage-template-memory"),
            new KernelOperationId("operation-template-memory"));

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ModuleSmokeCheckResult(
            ModuleId,
            passed: true,
            ModuleHealthStatus.Healthy,
            diagnosticsRefs: ["template.memory.health"]));
    }

    public ValueTask<MemoryModuleQueryResult> QueryAsync(
        MemoryModuleQueryInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(invocation.Query switch
        {
            ListMemoryProvidersModuleQuery => new MemoryModuleQueryResult(providers: [CreateProvider()]),
            ListMemorySpacesModuleQuery => new MemoryModuleQueryResult(spaces: [DefaultSpace()]),
            ResolveMemoryOverlayModuleQuery => new MemoryModuleQueryResult(overlay: new MemoryOverlay(records)),
            FilterMemoryModuleQuery => new MemoryModuleQueryResult(records: new MemoryQueryResult(records)),
            _ => new MemoryModuleQueryResult(degradedProviders: ["template.memory.unsupported_query"]),
        });
    }

    public ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        if (invocation.Mutation is not AddMemoryModuleMutation add)
        {
            return ValueTask.FromResult(new MemoryMutationResult(
                false,
                DegradedReason: "template.memory.unsupported_mutation",
                Effect: MemoryMutationEffect.Degraded));
        }

        var record = new FactMemoryRecord(
            add.Command.Key,
            add.Command.Value,
            add.Command.MemorySpaceId,
            add.Command.Confidence,
            sources: add.Command.Source is null ? [] : [add.Command.Source]);
        records.Add(record);

        return ValueTask.FromResult(new MemoryMutationResult(
            true,
            record.Id,
            record.LifecycleStatus,
            Effect: MemoryMutationEffect.Upserted));
    }

    private static MemoryProviderDescriptor CreateProvider()
        => new(
            ProviderId,
            "Template Local Memory",
            "1.0.0",
            MemoryProviderCapability.Filter
            | MemoryProviderCapability.ReadOnlyAccess
            | MemoryProviderCapability.Add
            | MemoryProviderCapability.Extract
            | MemoryProviderCapability.Supersede
            | MemoryProviderCapability.ReadWriteAccess,
            [MemoryScopeKind.User, MemoryScopeKind.Workspace],
            TrustLevel: MemoryProviderTrustLevel.Workspace,
            SupportedLifecycleStatuses: [MemoryLifecycleStatus.Active, MemoryLifecycleStatus.PendingReview, MemoryLifecycleStatus.Forgotten],
            DegradationStrategy: MemoryProviderDegradationStrategy.UnsupportedResult,
            Features: MemoryProviderFeature.SourceTracking | MemoryProviderFeature.SecretRedaction);

    private static MemoryModuleCapabilityBinding Capability(
        string capabilityId,
        MemoryModuleCapabilityKind kind,
        MemoryProviderCapability requiredCapabilities,
        SideEffectLevel sideEffectLevel,
        bool requiresHumanGate,
        bool executable = true)
        => new(
            capabilityId,
            kind,
            ProviderId,
            requiredCapabilities,
            new PermissionEnvelope([$"memory.{kind.ToString().ToLowerInvariant()}"], requiresHumanGate: requiresHumanGate),
            new SideEffectProfile(sideEffectLevel, ["memory"], reversible: sideEffectLevel <= SideEffectLevel.ReadOnly),
            requiresHumanGate,
            executable);

    private static MemorySpace DefaultSpace()
        => new(
            new MemorySpaceId("memory:template:user"),
            MemoryScopeKind.User,
            "template-user",
            "Template User Memory");

    private static ModuleDescriptor CreateDescriptor()
    {
        var manifest = CreateManifest();
        return new ModuleDescriptor(
            ModuleId,
            ModuleKind.MemoryIdentity,
            "Template Memory Module",
            "1.0.0",
            capabilities: manifest.Capabilities.Select(static capability => new ModuleCapabilityDescriptor(
                capability.CapabilityId,
                capability.Kind.ToString(),
                permission: capability.Permission,
                sideEffects: capability.SideEffects)).ToArray(),
            permission: new PermissionEnvelope(["memory.template"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false),
            audit: new ModuleAuditProfile(eventKinds: ["template.memory.invoked"]),
            trustLevel: ModuleTrustLevel.UserInstalled,
            minimumTianShuVersion: "0.6.0",
            health: new ModuleHealthProbe(ModuleHealthStatus.Healthy),
            implementationBinding: new ModuleImplementationBinding("TianShu.Template.MemoryModule", typeof(TemplateMemoryModule).FullName));
    }
}
