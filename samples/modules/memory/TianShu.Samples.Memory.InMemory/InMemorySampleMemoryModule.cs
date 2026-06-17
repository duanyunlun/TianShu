using TianShu.Contracts.Kernel;
using TianShu.Contracts.Memory;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Samples.Memory.InMemory;

/// <summary>
/// In-memory Memory 示例：演示第三方 Memory 模块的 retrieve、form、supersede 和降级边界。
/// In-memory Memory sample demonstrating retrieve, form, supersede, and degraded boundaries for a third-party Memory module.
/// </summary>
public sealed class InMemorySampleMemoryModule : IMemoryModule
{
    public const string ModuleId = "sample.memory.in_memory";
    public const string ProviderId = "sample.memory.in_memory.local";

    private readonly List<FactMemoryRecord> records = [];

    public ModuleDescriptor Descriptor { get; } = CreateDescriptor();

    public static MemoryModuleManifest CreateManifest()
        => new(
            ModuleId,
            "Sample In-Memory Memory Module",
            "1.0.0",
            "0.6.0",
            providers: [CreateProvider()],
            capabilities:
            [
                Capability("sample.memory.retrieve", MemoryModuleCapabilityKind.Retrieve, MemoryProviderCapability.Filter | MemoryProviderCapability.ReadOnlyAccess, SideEffectLevel.ReadOnly, requiresHumanGate: false),
                Capability("sample.memory.form", MemoryModuleCapabilityKind.Form, MemoryProviderCapability.Add | MemoryProviderCapability.Extract, SideEffectLevel.ExternalMutation, requiresHumanGate: true),
                Capability("sample.memory.supersede", MemoryModuleCapabilityKind.Supersede, MemoryProviderCapability.Supersede, SideEffectLevel.ExternalMutation, requiresHumanGate: true),
                Capability("sample.memory.compress", MemoryModuleCapabilityKind.CompressReserved, MemoryProviderCapability.None, SideEffectLevel.ReadOnly, requiresHumanGate: true, executable: false),
            ],
            new MemoryContextPolicyBinding(
                ContextSourceKind.MemoryRecord,
                ContextProjectionMode.ReferenceOnly,
                requireEvidenceRefs: true,
                moduleMaySliceContext: false),
            compressionReservations:
            [
                new MemoryCompressionReservation("sample.memory.compress.v1", "Reserved compression hook for future context policy integration."),
            ],
            diagnostics: ["sample.memory.in_memory.access"]);

    public static GovernanceEnvelope CreateGovernance()
        => new(
            "sample-memory-in-memory-governance",
            allowedModuleIds: [ModuleId],
            maxSideEffectLevel: SideEffectLevel.ExternalMutation,
            requiresHumanGate: true);

    public static ApprovedContextPolicy CreateApprovedContextPolicy()
        => new(
            new ContextPolicy(
                policyId: "sample.memory.context",
                allowedSourceKinds: [nameof(ContextSourceKind.MemoryRecord)],
                requireEvidenceRefs: true,
                sourceRules:
                [
                    new ContextSourceRule(
                        ContextSourceKind.MemoryRecord,
                        priority: 50,
                        projectionMode: ContextProjectionMode.ReferenceOnly,
                        requireEvidenceRef: true),
                ]),
            new CoreIntentId("intent-sample-memory"),
            new StageGraphId("graph-sample-memory"),
            new StageId("stage-sample-memory"),
            new KernelOperationId("operation-sample-memory"));

    public ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ModuleSmokeCheckResult(
            ModuleId,
            passed: true,
            ModuleHealthStatus.Healthy,
            diagnosticsRefs: ["sample.memory.in_memory.health"]));
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
            ResolveMemoryOverlayModuleQuery => new MemoryModuleQueryResult(overlay: new MemoryOverlay(ActiveRecords())),
            FilterMemoryModuleQuery => new MemoryModuleQueryResult(records: new MemoryQueryResult(ActiveRecords())),
            _ => new MemoryModuleQueryResult(degradedProviders: ["sample.memory.unsupported_query"]),
        });
    }

    public ValueTask<MemoryMutationResult> MutateAsync(
        MemoryModuleMutationInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        return invocation.Mutation switch
        {
            AddMemoryModuleMutation add => ValueTask.FromResult(Add(add.Command)),
            SupersedeMemoryModuleMutation supersede => ValueTask.FromResult(Supersede(supersede.Command)),
            _ => ValueTask.FromResult(new MemoryMutationResult(
                false,
                DegradedReason: "sample.memory.unsupported_mutation",
                Effect: MemoryMutationEffect.Degraded)),
        };
    }

    private MemoryMutationResult Add(AddMemory command)
    {
        var record = new FactMemoryRecord(
            command.Key,
            command.Value,
            command.MemorySpaceId,
            command.Confidence,
            sources: command.Source is null ? [] : [command.Source],
            formationPath: MemoryFormationPath.DirectInstruction);
        records.Add(record);

        return new MemoryMutationResult(true, record.Id, record.LifecycleStatus, Effect: MemoryMutationEffect.Upserted);
    }

    private MemoryMutationResult Supersede(SupersedeMemory command)
    {
        var oldIndex = records.FindIndex(record => record.Id.Equals(command.OldRecordId));
        if (oldIndex < 0)
        {
            return new MemoryMutationResult(
                false,
                DegradedReason: "sample.memory.supersede_target_missing",
                Effect: MemoryMutationEffect.Degraded);
        }

        records[oldIndex] = records[oldIndex].WithLifecycle(MemoryLifecycleStatus.Forgotten, DateTimeOffset.UtcNow);
        var replacement = new FactMemoryRecord(
            command.NewKey,
            command.NewValue,
            command.MemorySpaceId,
            command.Confidence,
            sources: command.Source is null ? [] : [command.Source],
            formationPath: MemoryFormationPath.DirectInstruction);
        records.Add(replacement);

        return new MemoryMutationResult(true, replacement.Id, replacement.LifecycleStatus, Effect: MemoryMutationEffect.Superseded);
    }

    private IReadOnlyList<FactMemoryRecord> ActiveRecords()
        => records.Where(static record => record.LifecycleStatus == MemoryLifecycleStatus.Active).ToArray();

    private static MemoryProviderDescriptor CreateProvider()
        => new(
            ProviderId,
            "Sample In-Memory Memory",
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
            new MemorySpaceId("memory:sample:user"),
            MemoryScopeKind.User,
            "sample-user",
            "Sample User Memory");

    private static ModuleDescriptor CreateDescriptor()
    {
        var manifest = CreateManifest();
        return new ModuleDescriptor(
            ModuleId,
            ModuleKind.MemoryIdentity,
            "Sample In-Memory Memory Module",
            "1.0.0",
            capabilities: manifest.Capabilities.Select(static capability => new ModuleCapabilityDescriptor(
                capability.CapabilityId,
                capability.Kind.ToString(),
                permission: capability.Permission,
                sideEffects: capability.SideEffects)).ToArray(),
            permission: new PermissionEnvelope(["memory.sample"], requiresHumanGate: true),
            sideEffects: new SideEffectProfile(SideEffectLevel.ExternalMutation, ["memory"], reversible: false),
            audit: new ModuleAuditProfile(eventKinds: ["sample.memory.in_memory.invoked"]),
            trustLevel: ModuleTrustLevel.UserInstalled,
            minimumTianShuVersion: "0.6.0",
            health: new ModuleHealthProbe(ModuleHealthStatus.Healthy),
            implementationBinding: new ModuleImplementationBinding("TianShu.Samples.Memory.InMemory", typeof(InMemorySampleMemoryModule).FullName));
    }
}
