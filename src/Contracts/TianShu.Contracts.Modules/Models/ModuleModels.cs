using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Modules;

/// <summary>
/// Module 家族分类，用于 Module Plane 发现、投影和治理。
/// Module family kind used for Module Plane discovery, projection, and governance.
/// </summary>
public enum ModuleKind
{
    Unspecified = 0,
    Provider = 1,
    Tool = 2,
    MemoryIdentity = 3,
    ArtifactStateProjection = 4,
    Diagnostics = 5,
    WorkspaceEnvironment = 6,
    Configuration = 7,
    SubAgentOrchestration = 8,
    Custom = 100,
}

/// <summary>
/// Module 信任等级；默认 Unspecified 必须由装配层拒绝或降级。
/// Module trust level; default Unspecified must be rejected or degraded by composition.
/// </summary>
public enum ModuleTrustLevel
{
    Unspecified = 0,
    BuiltIn = 1,
    WorkspaceTrusted = 2,
    UserInstalled = 3,
    ThirdParty = 4,
    Untrusted = 5,
}

/// <summary>
/// Module 健康状态。
/// Module health status.
/// </summary>
public enum ModuleHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unavailable = 3,
    Disabled = 4,
}

/// <summary>
/// Module schema 引用，避免通用 Module 契约依赖 Tool 专用 schema 类型。
/// Module schema reference that keeps the generic module contract independent from tool-specific schema types.
/// </summary>
public sealed record ModuleSchemaRef
{
    public ModuleSchemaRef(string schemaId, string? version = null, StructuredValue? inlineSchema = null)
    {
        SchemaId = IdentifierGuard.AgainstNullOrWhiteSpace(schemaId, nameof(schemaId));
        Version = version;
        InlineSchema = inlineSchema;
    }

    public string SchemaId { get; }

    public string? Version { get; }

    public StructuredValue? InlineSchema { get; }
}

/// <summary>
/// Module 审计画像。
/// Module audit profile.
/// </summary>
public sealed record ModuleAuditProfile
{
    public ModuleAuditProfile(bool required = true, IReadOnlyList<string>? eventKinds = null, bool redactSensitiveValues = true)
    {
        Required = required;
        EventKinds = eventKinds ?? Array.Empty<string>();
        RedactSensitiveValues = redactSensitiveValues;
    }

    public bool Required { get; }

    public IReadOnlyList<string> EventKinds { get; }

    public bool RedactSensitiveValues { get; }
}

/// <summary>
/// Module 能力描述。
/// Module capability descriptor.
/// </summary>
public sealed record ModuleCapabilityDescriptor
{
    public ModuleCapabilityDescriptor(
        string capabilityId,
        string displayName,
        ModuleSchemaRef? inputSchema = null,
        ModuleSchemaRef? outputSchema = null,
        PermissionEnvelope? permission = null,
        SideEffectProfile? sideEffects = null,
        MetadataBag? metadata = null)
    {
        CapabilityId = IdentifierGuard.AgainstNullOrWhiteSpace(capabilityId, nameof(capabilityId));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        InputSchema = inputSchema;
        OutputSchema = outputSchema;
        Permission = permission ?? CreateDefaultPermission(capabilityId);
        SideEffects = sideEffects ?? new SideEffectProfile(SideEffectLevel.Unspecified);
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string CapabilityId { get; }

    public string DisplayName { get; }

    public ModuleSchemaRef? InputSchema { get; }

    public ModuleSchemaRef? OutputSchema { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffects { get; }

    public MetadataBag Metadata { get; }

    private static PermissionEnvelope CreateDefaultPermission(string capabilityId)
        => new(
            scopes: [$"module.capability.{NormalizePolicyToken(capabilityId)}"],
            requiresHumanGate: true,
            reason: "Module capability must be approved by Kernel and executed through Execution Runtime.");

    internal static string NormalizePolicyToken(string value)
    {
        var chars = value.Select(static character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '.').ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("..", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("..", ".", StringComparison.Ordinal);
        }

        return normalized.Trim('.');
    }
}

/// <summary>
/// Module 实现绑定，供装配层定位实现但不暴露 SDK 私有类型。
/// Module implementation binding used by composition without exposing private SDK types.
/// </summary>
public sealed record ModuleImplementationBinding
{
    public ModuleImplementationBinding(string projectName, string? typeName = null, string? packageId = null)
    {
        ProjectName = IdentifierGuard.AgainstNullOrWhiteSpace(projectName, nameof(projectName));
        TypeName = typeName;
        PackageId = packageId;
    }

    public string ProjectName { get; }

    public string? TypeName { get; }

    public string? PackageId { get; }
}

/// <summary>
/// Module descriptor 是 Module Plane 的统一发现、治理和健康投影。
/// Module descriptor is the unified discovery, governance, and health projection for the Module Plane.
/// </summary>
public sealed record ModuleDescriptor
{
    public ModuleDescriptor(
        string moduleId,
        ModuleKind kind,
        string displayName,
        string version,
        IReadOnlyList<ModuleCapabilityDescriptor>? capabilities = null,
        ModuleSchemaRef? configurationSchema = null,
        PermissionEnvelope? permission = null,
        SideEffectProfile? sideEffects = null,
        ModuleAuditProfile? audit = null,
        ModuleTrustLevel trustLevel = ModuleTrustLevel.Unspecified,
        ModuleHealthProbe? health = null,
        ModuleImplementationBinding? implementationBinding = null,
        MetadataBag? metadata = null)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        Kind = kind;
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Version = IdentifierGuard.AgainstNullOrWhiteSpace(version, nameof(version));
        Capabilities = capabilities ?? Array.Empty<ModuleCapabilityDescriptor>();
        ConfigurationSchema = configurationSchema;
        Permission = permission ?? CreateDefaultPermission(moduleId);
        SideEffects = sideEffects ?? new SideEffectProfile(SideEffectLevel.Unspecified);
        Audit = audit ?? new ModuleAuditProfile(eventKinds: [$"module.{ModuleCapabilityDescriptor.NormalizePolicyToken(moduleId)}.invoked"]);
        TrustLevel = trustLevel;
        Health = health ?? new ModuleHealthProbe(ModuleHealthStatus.Unknown);
        ImplementationBinding = implementationBinding;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string ModuleId { get; }

    public ModuleKind Kind { get; }

    public string DisplayName { get; }

    public string Version { get; }

    public IReadOnlyList<ModuleCapabilityDescriptor> Capabilities { get; }

    public ModuleSchemaRef? ConfigurationSchema { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffects { get; }

    public ModuleAuditProfile Audit { get; }

    public ModuleTrustLevel TrustLevel { get; }

    public ModuleHealthProbe Health { get; }

    public ModuleImplementationBinding? ImplementationBinding { get; }

    public MetadataBag Metadata { get; }

    /// <summary>
    /// 判断模块描述符是否落在治理信封允许的模块、副作用和人工 gate 边界内。
    /// Determines whether the module descriptor fits the module, side-effect, and human-gate boundaries of the governance envelope.
    /// </summary>
    public bool IsAllowedBy(GovernanceEnvelope governance)
    {
        ArgumentNullException.ThrowIfNull(governance);

        return governance.AllowedModuleIds.Contains(ModuleId, StringComparer.Ordinal)
               && SideEffects.Level != SideEffectLevel.Unspecified
               && governance.MaxSideEffectLevel != SideEffectLevel.Unspecified
               && SideEffects.Level <= governance.MaxSideEffectLevel
               && (!Permission.RequiresHumanGate || governance.RequiresHumanGate);
    }

    private static PermissionEnvelope CreateDefaultPermission(string moduleId)
        => new(
            scopes: [$"module.{ModuleCapabilityDescriptor.NormalizePolicyToken(moduleId)}"],
            requiresHumanGate: true,
            reason: "Module must be discovered and governed before it can be invoked.");
}

/// <summary>
/// Module 健康探测投影。
/// Module health probe projection.
/// </summary>
public sealed record ModuleHealthProbe
{
    public ModuleHealthProbe(
        ModuleHealthStatus status,
        string? reason = null,
        DateTimeOffset? probedAt = null,
        IReadOnlyList<string>? checks = null)
    {
        Status = status;
        Reason = reason;
        ProbedAt = probedAt;
        Checks = checks ?? Array.Empty<string>();
    }

    public ModuleHealthStatus Status { get; }

    public string? Reason { get; }

    public DateTimeOffset? ProbedAt { get; }

    public IReadOnlyList<string> Checks { get; }
}

/// <summary>
/// Module smoke check 结果。
/// Module smoke-check result.
/// </summary>
public sealed record ModuleSmokeCheckResult
{
    public ModuleSmokeCheckResult(
        string moduleId,
        bool passed,
        ModuleHealthStatus status,
        string? reason = null,
        IReadOnlyList<string>? diagnosticsRefs = null)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        Passed = passed;
        Status = status;
        Reason = reason;
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
    }

    public string ModuleId { get; }

    public bool Passed { get; }

    public ModuleHealthStatus Status { get; }

    public string? Reason { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }
}

/// <summary>
/// Module health / smoke check 统一入口。
/// Unified module health / smoke-check entry point.
/// </summary>
public interface IModuleHealthCheck
{
    ModuleDescriptor Descriptor { get; }

    ValueTask<ModuleSmokeCheckResult> CheckAsync(CancellationToken cancellationToken);
}
