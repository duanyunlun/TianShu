using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Memory;

/// <summary>
/// Memory 模块公开能力类别。
/// Public capability kind exposed by a Memory module.
/// </summary>
public enum MemoryModuleCapabilityKind
{
    Retrieve = 0,
    Form = 1,
    Supersede = 2,
    CompressReserved = 3,
}

/// <summary>
/// Memory 模块 manifest；声明公开接入面，不包含具体 store、embedding 或模型实现。
/// Memory module manifest; declares the public access surface without concrete store, embedding, or model implementations.
/// </summary>
public sealed record MemoryModuleManifest
{
    public MemoryModuleManifest(
        string moduleId,
        string displayName,
        string version,
        string minimumTianShuVersion,
        IReadOnlyList<MemoryProviderDescriptor> providers,
        IReadOnlyList<MemoryModuleCapabilityBinding> capabilities,
        MemoryContextPolicyBinding contextPolicyBinding,
        IReadOnlyList<MemoryCompressionReservation>? compressionReservations = null,
        IReadOnlyList<string>? diagnostics = null)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Version = IdentifierGuard.AgainstNullOrWhiteSpace(version, nameof(version));
        MinimumTianShuVersion = IdentifierGuard.AgainstNullOrWhiteSpace(minimumTianShuVersion, nameof(minimumTianShuVersion));
        Providers = providers is { Count: > 0 } ? providers : throw new ArgumentException("Memory module manifest requires at least one provider.", nameof(providers));
        Capabilities = capabilities is { Count: > 0 } ? capabilities : throw new ArgumentException("Memory module manifest requires at least one capability binding.", nameof(capabilities));
        ContextPolicyBinding = contextPolicyBinding ?? throw new ArgumentNullException(nameof(contextPolicyBinding));
        CompressionReservations = compressionReservations ?? Array.Empty<MemoryCompressionReservation>();
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string ModuleId { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public string MinimumTianShuVersion { get; }

    public IReadOnlyList<MemoryProviderDescriptor> Providers { get; }

    public IReadOnlyList<MemoryModuleCapabilityBinding> Capabilities { get; }

    public MemoryContextPolicyBinding ContextPolicyBinding { get; }

    public IReadOnlyList<MemoryCompressionReservation> CompressionReservations { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Memory 模块能力绑定声明。
/// Memory module capability binding declaration.
/// </summary>
public sealed record MemoryModuleCapabilityBinding
{
    public MemoryModuleCapabilityBinding(
        string capabilityId,
        MemoryModuleCapabilityKind kind,
        string providerId,
        MemoryProviderCapability requiredCapabilities,
        PermissionEnvelope permission,
        SideEffectProfile sideEffects,
        bool requiresHumanGate,
        bool executable = true,
        bool enabled = true,
        IReadOnlyList<string>? diagnostics = null)
    {
        CapabilityId = IdentifierGuard.AgainstNullOrWhiteSpace(capabilityId, nameof(capabilityId));
        Kind = kind;
        ProviderId = IdentifierGuard.AgainstNullOrWhiteSpace(providerId, nameof(providerId));
        RequiredCapabilities = requiredCapabilities;
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffects = sideEffects ?? throw new ArgumentNullException(nameof(sideEffects));
        RequiresHumanGate = requiresHumanGate;
        Executable = executable;
        Enabled = enabled;
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string CapabilityId { get; }

    public MemoryModuleCapabilityKind Kind { get; }

    public string ProviderId { get; }

    public MemoryProviderCapability RequiredCapabilities { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffects { get; }

    public bool RequiresHumanGate { get; }

    public bool Executable { get; }

    public bool Enabled { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Memory 与 ContextPolicy 的接入边界。
/// Access boundary between Memory and ContextPolicy.
/// </summary>
public sealed record MemoryContextPolicyBinding
{
    public MemoryContextPolicyBinding(
        ContextSourceKind sourceKind = ContextSourceKind.MemoryRecord,
        ContextProjectionMode projectionMode = ContextProjectionMode.ReferenceOnly,
        bool requireEvidenceRefs = true,
        bool moduleMaySliceContext = false,
        IReadOnlyList<string>? diagnostics = null)
    {
        SourceKind = sourceKind;
        ProjectionMode = projectionMode;
        RequireEvidenceRefs = requireEvidenceRefs;
        ModuleMaySliceContext = moduleMaySliceContext;
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public ContextSourceKind SourceKind { get; }

    public ContextProjectionMode ProjectionMode { get; }

    public bool RequireEvidenceRefs { get; }

    public bool ModuleMaySliceContext { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Memory 压缩能力预留声明；P27 前不得作为可执行能力开放。
/// Reserved Memory compression declaration; must not be exposed as an executable capability before P27.
/// </summary>
public sealed record MemoryCompressionReservation
{
    public MemoryCompressionReservation(
        string reservationId,
        string description,
        bool reservedOnly = true,
        IReadOnlyList<string>? diagnostics = null)
    {
        ReservationId = IdentifierGuard.AgainstNullOrWhiteSpace(reservationId, nameof(reservationId));
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        ReservedOnly = reservedOnly;
        Diagnostics = diagnostics ?? Array.Empty<string>();
    }

    public string ReservationId { get; }

    public string Description { get; }

    public bool ReservedOnly { get; }

    public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Memory 模块公开接入描述；Runtime binding 只能消费 validated access。
/// Memory module public access descriptor; Runtime binding can consume only validated access.
/// </summary>
public sealed record MemoryModuleAccessDescriptor
{
    public MemoryModuleAccessDescriptor(
        MemoryModuleManifest manifest,
        GovernanceEnvelope governance,
        ApprovedContextPolicy contextPolicy)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Governance = governance ?? throw new ArgumentNullException(nameof(governance));
        ContextPolicy = contextPolicy ?? throw new ArgumentNullException(nameof(contextPolicy));
    }

    public MemoryModuleManifest Manifest { get; }

    public GovernanceEnvelope Governance { get; }

    public ApprovedContextPolicy ContextPolicy { get; }
}

/// <summary>
/// Memory 模块公开接入校验结果。
/// Memory module public access validation result.
/// </summary>
public sealed record MemoryModuleAccessValidationResult(
    MemoryModuleAccessDescriptor? Access,
    IReadOnlyList<MemoryModuleAccessIssue> Issues)
{
    public bool IsValid => Access is not null && Issues.All(static issue => issue.Severity != MemoryModuleAccessIssueSeverity.Error);
}

/// <summary>
/// Memory 模块公开接入问题严重性。
/// Memory module public access issue severity.
/// </summary>
public enum MemoryModuleAccessIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Memory 模块公开接入问题。
/// Memory module public access issue.
/// </summary>
public sealed record MemoryModuleAccessIssue(
    string Code,
    string Message,
    MemoryModuleAccessIssueSeverity Severity = MemoryModuleAccessIssueSeverity.Error)
{
    public string Code { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Code, nameof(Code));

    public string Message { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Message, nameof(Message));
}

/// <summary>
/// Memory 检索结果到 ContextPolicy 候选片段的投影。
/// Projection from Memory retrieval results to ContextPolicy candidate segments.
/// </summary>
public sealed record MemoryContextCandidateProjection(
    string PolicyId,
    IReadOnlyList<ContextSourceCandidate> Candidates,
    IReadOnlyList<string> DiagnosticsRefs)
{
    public string PolicyId { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(PolicyId, nameof(PolicyId));

    public IReadOnlyList<ContextSourceCandidate> Candidates { get; } = Candidates ?? Array.Empty<ContextSourceCandidate>();

    public IReadOnlyList<string> DiagnosticsRefs { get; } = DiagnosticsRefs ?? Array.Empty<string>();
}

/// <summary>
/// Memory 模块公开接入校验器。
/// Memory module public access validator.
/// </summary>
public static class MemoryModuleAccessValidator
{
    public static MemoryModuleAccessValidationResult Validate(
        MemoryModuleManifest manifest,
        GovernanceEnvelope governance,
        ApprovedContextPolicy contextPolicy)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(governance);
        ArgumentNullException.ThrowIfNull(contextPolicy);

        var issues = new List<MemoryModuleAccessIssue>();
        if (!governance.AllowedModuleIds.Contains(manifest.ModuleId, StringComparer.Ordinal))
        {
            issues.Add(Error("memory_access.module_not_allowed", "GovernanceEnvelope 未允许当前 Memory module。"));
        }

        ValidateContextPolicy(manifest.ContextPolicyBinding, contextPolicy, issues);
        ValidateProviders(manifest, issues);
        ValidateCapabilities(manifest, governance, issues);
        ValidateCompressionReservations(manifest, issues);

        if (issues.Any(static issue => issue.Severity == MemoryModuleAccessIssueSeverity.Error))
        {
            return new MemoryModuleAccessValidationResult(null, issues);
        }

        return new MemoryModuleAccessValidationResult(
            new MemoryModuleAccessDescriptor(manifest, governance, contextPolicy),
            issues);
    }

    public static MemoryContextCandidateProjection ProjectRecordsToContextCandidates(
        MemoryQueryResult result,
        ApprovedContextPolicy contextPolicy,
        string diagnosticsRefPrefix = "memory.context")
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(contextPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsRefPrefix);

        var candidates = result.Records
            .Select(record => new ContextSourceCandidate(
                $"memory:{record.Id.Value}",
                ContextSourceKind.MemoryRecord,
                BuildCandidateContent(record),
                EstimateTokens(record),
                record.Confidence,
                BuildEvidenceRef(record),
                metadata: new MetadataBag(new Dictionary<string, StructuredValue>
                {
                    ["memoryRecordId"] = StructuredValue.FromString(record.Id.Value),
                    ["memorySpaceId"] = StructuredValue.FromString(record.MemorySpaceId.Value),
                    ["memoryKey"] = StructuredValue.FromString(record.Key),
                })))
            .ToArray();

        return new MemoryContextCandidateProjection(
            contextPolicy.Policy.PolicyId,
            candidates,
            DiagnosticsRefs: [$"{diagnosticsRefPrefix}.projected:{candidates.Length}"]);
    }

    private static void ValidateContextPolicy(
        MemoryContextPolicyBinding binding,
        ApprovedContextPolicy contextPolicy,
        List<MemoryModuleAccessIssue> issues)
    {
        if (binding.SourceKind != ContextSourceKind.MemoryRecord)
        {
            issues.Add(Error("memory_access.context_source_mismatch", "Memory module 只能投影 MemoryRecord context source。"));
        }

        if (binding.ProjectionMode == ContextProjectionMode.Unspecified)
        {
            issues.Add(Error("memory_access.context_projection_unspecified", "Memory context projection mode 不能为 Unspecified。"));
        }

        if (binding.ModuleMaySliceContext)
        {
            issues.Add(Error("memory_access.context_slicing_owned_by_module", "Memory module 不得自行裁切上下文；裁切只能由 ContextPolicy bridge 执行。"));
        }

        if (contextPolicy.Policy.RequireEvidenceRefs && !binding.RequireEvidenceRefs)
        {
            issues.Add(Error("memory_access.evidence_requirement_weakened", "Memory module 不得弱化已批准 ContextPolicy 的 evidence requirement。"));
        }

        var allowsMemorySource = contextPolicy.Policy.SourceRules.Any(static rule => rule.SourceKind == ContextSourceKind.MemoryRecord)
                                 || contextPolicy.Policy.AllowedSourceKinds.Contains(nameof(ContextSourceKind.MemoryRecord), StringComparer.Ordinal)
                                 || contextPolicy.Policy.AllowedSourceKinds.Contains(ContextSourceKind.MemoryRecord.ToString(), StringComparer.Ordinal);
        if (!allowsMemorySource)
        {
            issues.Add(Error("memory_access.context_policy_disallows_memory", "ApprovedContextPolicy 未允许 MemoryRecord 进入上下文候选。"));
        }
    }

    private static void ValidateProviders(MemoryModuleManifest manifest, List<MemoryModuleAccessIssue> issues)
    {
        var providerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in manifest.Providers)
        {
            if (!providerIds.Add(provider.ProviderId))
            {
                issues.Add(Error("memory_access.duplicate_provider", $"Memory provider 重复：{provider.ProviderId}。"));
            }

            if (provider.TrustLevel == MemoryProviderTrustLevel.Unknown)
            {
                issues.Add(Error("memory_access.provider_trust_unknown", $"Memory provider trust level 未声明：{provider.ProviderId}。"));
            }

            if (provider.DegradationStrategy == MemoryProviderDegradationStrategy.Unknown)
            {
                issues.Add(Error("memory_access.provider_degradation_unknown", $"Memory provider degradation strategy 未声明：{provider.ProviderId}。"));
            }
        }
    }

    private static void ValidateCapabilities(
        MemoryModuleManifest manifest,
        GovernanceEnvelope governance,
        List<MemoryModuleAccessIssue> issues)
    {
        var providerById = manifest.Providers.ToDictionary(static provider => provider.ProviderId, StringComparer.Ordinal);
        var capabilityIds = new HashSet<string>(StringComparer.Ordinal);
        var enabled = manifest.Capabilities.Where(static capability => capability.Enabled).ToArray();
        foreach (var capability in enabled)
        {
            if (!capabilityIds.Add(capability.CapabilityId))
            {
                issues.Add(Error("memory_access.duplicate_capability", $"Memory capability 重复：{capability.CapabilityId}。"));
                continue;
            }

            if (!providerById.TryGetValue(capability.ProviderId, out var provider))
            {
                issues.Add(Error("memory_access.provider_missing", $"Memory capability 缺少 provider：{capability.ProviderId}。"));
                continue;
            }

            ValidateCapabilityAgainstProvider(capability, provider, issues);
            ValidateCapabilityAgainstGovernance(capability, governance, issues);
        }

        RequireEnabledKind(enabled, MemoryModuleCapabilityKind.Retrieve, "memory_access.retrieve_missing", "Memory module 必须声明可用 retrieve 能力。", issues);
        RequireEnabledKind(enabled, MemoryModuleCapabilityKind.Form, "memory_access.form_missing", "Memory module 必须声明可用 formation 能力。", issues);
        RequireEnabledKind(enabled, MemoryModuleCapabilityKind.Supersede, "memory_access.supersede_missing", "Memory module 必须声明可用 supersede 能力。", issues);
        RequireEnabledKind(enabled, MemoryModuleCapabilityKind.CompressReserved, "memory_access.compression_reserved_missing", "Memory module 必须声明压缩预留能力边界。", issues);
    }

    private static void ValidateCapabilityAgainstProvider(
        MemoryModuleCapabilityBinding capability,
        MemoryProviderDescriptor provider,
        List<MemoryModuleAccessIssue> issues)
    {
        if (capability.Kind != MemoryModuleCapabilityKind.CompressReserved
            && capability.RequiredCapabilities == MemoryProviderCapability.None)
        {
            issues.Add(Error("memory_access.required_capability_missing", $"Memory capability 未声明 provider capability：{capability.CapabilityId}。"));
        }

        if (capability.RequiredCapabilities != MemoryProviderCapability.None
            && (provider.Capabilities & capability.RequiredCapabilities) != capability.RequiredCapabilities)
        {
            issues.Add(Error("memory_access.provider_capability_missing", $"Memory provider 不具备 capability 所需能力：{capability.CapabilityId}。"));
        }

        switch (capability.Kind)
        {
            case MemoryModuleCapabilityKind.Retrieve:
                if (!capability.RequiredCapabilities.HasFlag(MemoryProviderCapability.Filter)
                    && !capability.RequiredCapabilities.HasFlag(MemoryProviderCapability.ReadOnlyAccess))
                {
                    issues.Add(Error("memory_access.retrieve_capability_invalid", "Retrieve 能力必须包含 Filter 或 ReadOnlyAccess。"));
                }

                if (capability.SideEffects.Level > SideEffectLevel.ReadOnly)
                {
                    issues.Add(Error("memory_access.retrieve_side_effect_too_high", "Retrieve 能力不能声明高于 ReadOnly 的副作用。"));
                }

                break;
            case MemoryModuleCapabilityKind.Form:
                if (!capability.RequiredCapabilities.HasFlag(MemoryProviderCapability.Add)
                    && !capability.RequiredCapabilities.HasFlag(MemoryProviderCapability.Extract))
                {
                    issues.Add(Error("memory_access.form_capability_invalid", "Formation 能力必须包含 Add 或 Extract。"));
                }

                break;
            case MemoryModuleCapabilityKind.Supersede:
                if (!capability.RequiredCapabilities.HasFlag(MemoryProviderCapability.Supersede))
                {
                    issues.Add(Error("memory_access.supersede_capability_invalid", "Supersede 能力必须包含 Supersede。"));
                }

                break;
            case MemoryModuleCapabilityKind.CompressReserved:
                if (capability.Executable)
                {
                    issues.Add(Error("memory_access.compression_reserved_executable", "Compression 预留能力在 P27 前不得 executable。"));
                }

                break;
        }
    }

    private static void ValidateCapabilityAgainstGovernance(
        MemoryModuleCapabilityBinding capability,
        GovernanceEnvelope governance,
        List<MemoryModuleAccessIssue> issues)
    {
        if (capability.SideEffects.Level == SideEffectLevel.Unspecified)
        {
            issues.Add(Error("memory_access.side_effect_unspecified", $"Memory capability 副作用等级未声明：{capability.CapabilityId}。"));
        }

        if (governance.MaxSideEffectLevel == SideEffectLevel.Unspecified
            || capability.SideEffects.Level > governance.MaxSideEffectLevel)
        {
            issues.Add(Error("memory_access.governance_side_effect_denied", $"GovernanceEnvelope 不允许 Memory capability 副作用等级：{capability.CapabilityId}。"));
        }

        if (capability.RequiresHumanGate && !governance.RequiresHumanGate)
        {
            issues.Add(Error("memory_access.governance_human_gate_missing", $"GovernanceEnvelope 缺少 Memory capability 要求的 human gate：{capability.CapabilityId}。"));
        }

        if (capability.Permission.RequiresHumanGate != capability.RequiresHumanGate)
        {
            issues.Add(Error("memory_access.permission_gate_mismatch", $"Memory capability permission human gate 与绑定声明不一致：{capability.CapabilityId}。"));
        }
    }

    private static void ValidateCompressionReservations(MemoryModuleManifest manifest, List<MemoryModuleAccessIssue> issues)
    {
        if (manifest.CompressionReservations.Count == 0)
        {
            issues.Add(Error("memory_access.compression_reservation_missing", "Memory module 必须声明压缩预留接口。"));
        }

        foreach (var reservation in manifest.CompressionReservations)
        {
            if (!reservation.ReservedOnly)
            {
                issues.Add(Error("memory_access.compression_reservation_not_reserved", $"Compression reservation 必须保持 reserved-only：{reservation.ReservationId}。"));
            }
        }
    }

    private static void RequireEnabledKind(
        IReadOnlyList<MemoryModuleCapabilityBinding> enabled,
        MemoryModuleCapabilityKind kind,
        string code,
        string message,
        List<MemoryModuleAccessIssue> issues)
    {
        if (!enabled.Any(capability => capability.Kind == kind))
        {
            issues.Add(Error(code, message));
        }
    }

    private static string BuildCandidateContent(FactMemoryRecord record)
    {
        var value = record.Value.ToPlainObject();
        var rendered = value is string text ? text : JsonSerializer.Serialize(value);
        return $"{record.Key}: {rendered}";
    }

    private static int EstimateTokens(FactMemoryRecord record)
        => Math.Max(1, BuildCandidateContent(record).Length / 4);

    private static string? BuildEvidenceRef(FactMemoryRecord record)
    {
        var source = record.Sources.FirstOrDefault();
        return source is null
            ? null
            : $"memory-source:{source.SourceKind}:{source.SourceId}";
    }

    private static MemoryModuleAccessIssue Error(string code, string message)
        => new(code, message, MemoryModuleAccessIssueSeverity.Error);
}
