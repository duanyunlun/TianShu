using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Modules;

/// <summary>
/// Module 加载状态；Registered 之前不得进入 Execution Runtime binding。
/// Module load status; modules must not enter Execution Runtime binding before Registered.
/// </summary>
public enum ModuleLoadStatus
{
    NotStarted = 0,
    Skipped = 1,
    Rejected = 2,
    Unavailable = 3,
    Activated = 4,
    Registered = 5,
}

/// <summary>
/// Module 隔离边界类型。
/// Module isolation boundary kind.
/// </summary>
public enum ModuleIsolationKind
{
    Unspecified = 0,
    BuiltInShared = 1,
    DirectoryAssemblyLoadContext = 2,
    ProcessBoundary = 3,
    RemoteBoundary = 4,
}

/// <summary>
/// Module 服务注册生命周期。
/// Module service registration lifetime.
/// </summary>
public enum ModuleServiceLifetime
{
    Singleton = 0,
    Scoped = 1,
    Transient = 2,
}

/// <summary>
/// Module 加载诊断严重性。
/// Module load diagnostic severity.
/// </summary>
public enum ModuleLoadDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// Module 加载策略；默认 fail closed。
/// Module loading policy; defaults to fail closed.
/// </summary>
public sealed record ModuleLoadingPolicy
{
    public ModuleLoadingPolicy(
        string currentTianShuVersion,
        IReadOnlySet<string>? explicitlyAllowedModuleIds = null,
        bool requireExplicitAllowForThirdParty = true,
        IReadOnlySet<string>? boundConfigurationKeys = null,
        string? currentModuleSdkContractVersion = null,
        string? currentCapabilitySchemaVersion = null)
    {
        CurrentTianShuVersion = IdentifierGuard.AgainstNullOrWhiteSpace(currentTianShuVersion, nameof(currentTianShuVersion));
        ExplicitlyAllowedModuleIds = explicitlyAllowedModuleIds ?? new HashSet<string>(StringComparer.Ordinal);
        RequireExplicitAllowForThirdParty = requireExplicitAllowForThirdParty;
        BoundConfigurationKeys = boundConfigurationKeys ?? new HashSet<string>(StringComparer.Ordinal);
        CurrentModuleSdkContractVersion = string.IsNullOrWhiteSpace(currentModuleSdkContractVersion)
            ? CurrentTianShuVersion
            : currentModuleSdkContractVersion!.Trim();
        CurrentCapabilitySchemaVersion = string.IsNullOrWhiteSpace(currentCapabilitySchemaVersion)
            ? CurrentModuleSdkContractVersion
            : currentCapabilitySchemaVersion!.Trim();
    }

    public string CurrentTianShuVersion { get; }

    public IReadOnlySet<string> ExplicitlyAllowedModuleIds { get; }

    public bool RequireExplicitAllowForThirdParty { get; }

    public IReadOnlySet<string> BoundConfigurationKeys { get; }

    public string CurrentModuleSdkContractVersion { get; }

    public string CurrentCapabilitySchemaVersion { get; }
}

/// <summary>
/// Module 隔离边界投影。
/// Module isolation boundary projection.
/// </summary>
public sealed record ModuleIsolationBoundary
{
    public ModuleIsolationBoundary(
        ModuleIsolationKind kind,
        string boundaryId,
        string? sourcePath = null,
        bool collectible = false)
    {
        Kind = kind;
        BoundaryId = IdentifierGuard.AgainstNullOrWhiteSpace(boundaryId, nameof(boundaryId));
        SourcePath = sourcePath;
        Collectible = collectible;
    }

    public ModuleIsolationKind Kind { get; }

    public string BoundaryId { get; }

    public string? SourcePath { get; }

    public bool Collectible { get; }
}

/// <summary>
/// Module 服务注册声明；它描述组合根要注册什么，不暴露具体 DI 容器。
/// Module service registration declaration; describes what the composition root registers without exposing a concrete DI container.
/// </summary>
public sealed record ModuleServiceRegistration
{
    public ModuleServiceRegistration(
        string serviceId,
        string implementationId,
        ModuleServiceLifetime lifetime = ModuleServiceLifetime.Singleton,
        IReadOnlyList<string>? capabilityIds = null)
    {
        ServiceId = IdentifierGuard.AgainstNullOrWhiteSpace(serviceId, nameof(serviceId));
        ImplementationId = IdentifierGuard.AgainstNullOrWhiteSpace(implementationId, nameof(implementationId));
        Lifetime = lifetime;
        CapabilityIds = capabilityIds ?? Array.Empty<string>();
    }

    public string ServiceId { get; }

    public string ImplementationId { get; }

    public ModuleServiceLifetime Lifetime { get; }

    public IReadOnlyList<string> CapabilityIds { get; }
}

/// <summary>
/// Module 加载诊断。
/// Module load diagnostic.
/// </summary>
public sealed record ModuleLoadDiagnostic
{
    public ModuleLoadDiagnostic(
        string code,
        string message,
        ModuleLoadDiagnosticSeverity severity = ModuleLoadDiagnosticSeverity.Error,
        string? moduleId = null,
        string? sourceRef = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        Severity = severity;
        ModuleId = moduleId;
        SourceRef = sourceRef;
    }

    public string Code { get; }

    public string Message { get; }

    public ModuleLoadDiagnosticSeverity Severity { get; }

    public string? ModuleId { get; }

    public string? SourceRef { get; }
}

/// <summary>
/// Module 加载记录；成功记录可进入后续 Runtime binding。
/// Module load record; successful records can enter later Runtime binding.
/// </summary>
public sealed record ModuleLoadRecord
{
    public ModuleLoadRecord(
        ModuleDiscoveryCandidate candidate,
        ModuleLoadStatus status,
        ModuleIsolationBoundary? isolationBoundary = null,
        IReadOnlyList<ModuleServiceRegistration>? serviceRegistrations = null,
        IReadOnlyList<ModuleLoadDiagnostic>? diagnostics = null)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        Status = status;
        IsolationBoundary = isolationBoundary;
        ServiceRegistrations = serviceRegistrations ?? Array.Empty<ModuleServiceRegistration>();
        Diagnostics = diagnostics ?? Array.Empty<ModuleLoadDiagnostic>();
    }

    public ModuleDiscoveryCandidate Candidate { get; }

    public ModuleLoadStatus Status { get; }

    public ModuleIsolationBoundary? IsolationBoundary { get; }

    public IReadOnlyList<ModuleServiceRegistration> ServiceRegistrations { get; }

    public IReadOnlyList<ModuleLoadDiagnostic> Diagnostics { get; }
}

/// <summary>
/// Module 装配计划；组合根只能注册 Registered 记录。
/// Module assembly plan; composition roots may register only Registered records.
/// </summary>
public sealed record ModuleAssemblyPlan
{
    public ModuleAssemblyPlan(IReadOnlyList<ModuleLoadRecord>? records = null)
    {
        Records = records ?? Array.Empty<ModuleLoadRecord>();
    }

    public IReadOnlyList<ModuleLoadRecord> Records { get; }

    public IReadOnlyList<ModuleLoadRecord> RegisteredRecords
        => Records
            .Where(static record => record.Status == ModuleLoadStatus.Registered)
            .ToArray();

    public IReadOnlyList<ModuleLoadDiagnostic> Diagnostics
        => Records
            .SelectMany(static record => record.Diagnostics)
            .ToArray();
}

/// <summary>
/// Module 组合根上下文；具体运行时可把它映射到自己的 DI 或 binding registry。
/// Module composition-root context; concrete runtimes can map it to their own DI or binding registry.
/// </summary>
public sealed record ModuleCompositionRootContext
{
    public ModuleCompositionRootContext(ModuleDiscoverySnapshot discoverySnapshot, ModuleLoadingPolicy policy)
    {
        DiscoverySnapshot = discoverySnapshot ?? throw new ArgumentNullException(nameof(discoverySnapshot));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public ModuleDiscoverySnapshot DiscoverySnapshot { get; }

    public ModuleLoadingPolicy Policy { get; }
}

/// <summary>
/// Module 组合根入口。
/// Module composition-root entry point.
/// </summary>
public interface IModuleCompositionRoot
{
    ValueTask<ModuleAssemblyPlan> ComposeAsync(ModuleCompositionRootContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 默认 Module 装配规划器；它只做门禁和注册计划，不执行程序集加载。
/// Default Module assembly planner; it only gates and plans registrations, and never loads assemblies.
/// </summary>
public sealed class DefaultModuleCompositionRoot : IModuleCompositionRoot
{
    public ValueTask<ModuleAssemblyPlan> ComposeAsync(ModuleCompositionRootContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var records = context.DiscoverySnapshot.Candidates
            .Select(candidate => PlanCandidate(candidate, context.Policy))
            .ToArray();

        return ValueTask.FromResult(new ModuleAssemblyPlan(records));
    }

    private static ModuleLoadRecord PlanCandidate(ModuleDiscoveryCandidate candidate, ModuleLoadingPolicy policy)
    {
        if (candidate.Status != ModuleDiscoveryCandidateStatus.Selected)
        {
            return new ModuleLoadRecord(
                candidate,
                ModuleLoadStatus.Skipped,
                diagnostics: [Diagnostic("module_load.candidate_not_selected", "未被 discovery 选中的模块不会进入装配。", ModuleLoadDiagnosticSeverity.Info, candidate)]);
        }

        if (candidate.Manifest.Source.Root.SourceKind == ModuleDiscoverySourceKind.RemoteReserved)
        {
            return Reject(candidate, "module_load.remote_reserved", "Remote Module 在当前阶段只允许作为预留边界，不得进入装配。");
        }

        if (candidate.Descriptor is null)
        {
            return Reject(candidate, "module_load.descriptor_missing", "Module candidate 缺少 ModuleDescriptor，不能装配。");
        }

        var descriptor = candidate.Descriptor;
        if (!string.Equals(descriptor.ModuleId, candidate.Manifest.ModuleId, StringComparison.Ordinal)
            || descriptor.Kind != candidate.Manifest.Kind)
        {
            return Reject(candidate, "module_load.descriptor_manifest_mismatch", "ModuleDescriptor 与 manifest 的 module id 或 kind 不一致。");
        }

        if (descriptor.TrustLevel is ModuleTrustLevel.Unspecified or ModuleTrustLevel.Untrusted)
        {
            return Reject(candidate, "module_load.trust_denied", "Module trust level 不足，不能装配。");
        }

        if (policy.RequireExplicitAllowForThirdParty
            && descriptor.TrustLevel == ModuleTrustLevel.ThirdParty
            && !policy.ExplicitlyAllowedModuleIds.Contains(descriptor.ModuleId))
        {
            return Reject(candidate, "module_load.third_party_not_allowed", "第三方模块需要显式 allow-list 才能装配。");
        }

        var versionFailure = ValidateVersion(candidate, descriptor, policy.CurrentTianShuVersion);
        if (versionFailure is not null)
        {
            return versionFailure;
        }

        var contractVersionFailure = ValidateManifestContractVersions(candidate, policy);
        if (contractVersionFailure is not null)
        {
            return contractVersionFailure;
        }

        var configurationFailure = ValidateRequiredConfiguration(candidate, descriptor, policy);
        if (configurationFailure is not null)
        {
            return configurationFailure;
        }

        if (descriptor.Health.Status is ModuleHealthStatus.Unknown or ModuleHealthStatus.Disabled or ModuleHealthStatus.Unavailable)
        {
            return new ModuleLoadRecord(
                candidate,
                ModuleLoadStatus.Unavailable,
                diagnostics: [Diagnostic("module_load.health_not_healthy", "Module health 未达到可装配状态。", ModuleLoadDiagnosticSeverity.Error, candidate)]);
        }

        if (descriptor.ImplementationBinding is null)
        {
            return Reject(candidate, "module_load.implementation_binding_missing", "ModuleDescriptor 缺少 implementation binding，不能注册服务。");
        }

        var isolation = CreateIsolationBoundary(candidate);
        var services = CreateServiceRegistrations(descriptor);
        return new ModuleLoadRecord(candidate, ModuleLoadStatus.Registered, isolation, services);
    }

    private static ModuleLoadRecord? ValidateVersion(
        ModuleDiscoveryCandidate candidate,
        ModuleDescriptor descriptor,
        string currentTianShuVersion)
    {
        if (!Version.TryParse(currentTianShuVersion, out var current))
        {
            return Reject(candidate, "module_load.current_version_invalid", "当前 TianShu 版本不是有效版本。");
        }

        var minimumText = string.IsNullOrWhiteSpace(candidate.Manifest.MinimumTianShuVersion)
            ? descriptor.MinimumTianShuVersion
            : candidate.Manifest.MinimumTianShuVersion!;
        if (!Version.TryParse(minimumText, out var minimum))
        {
            return Reject(candidate, "module_load.minimum_version_invalid", "Module minimum TianShu version 不是有效版本。");
        }

        return current < minimum
            ? Reject(candidate, "module_load.version_incompatible", "当前 TianShu 版本低于模块最低版本要求。")
            : null;
    }

    private static ModuleLoadRecord? ValidateManifestContractVersions(
        ModuleDiscoveryCandidate candidate,
        ModuleLoadingPolicy policy)
    {
        var sdkFailure = ValidateOptionalMajorVersion(
            candidate,
            candidate.Manifest.SdkContractVersion,
            policy.CurrentModuleSdkContractVersion,
            invalidCode: "module_load.sdk_contract_version_invalid",
            incompatibleCode: "module_load.sdk_contract_version_incompatible",
            invalidMessage: "Module SDK contract version 不是有效版本。",
            incompatibleMessage: "Module SDK contract 主版本与当前运行时不兼容。");
        if (sdkFailure is not null)
        {
            return sdkFailure;
        }

        return ValidateOptionalMajorVersion(
            candidate,
            candidate.Manifest.CapabilitySchemaVersion,
            policy.CurrentCapabilitySchemaVersion,
            invalidCode: "module_load.capability_schema_version_invalid",
            incompatibleCode: "module_load.capability_schema_version_incompatible",
            invalidMessage: "Module capability schema version 不是有效版本。",
            incompatibleMessage: "Module capability schema 主版本与当前运行时不兼容。");
    }

    private static ModuleLoadRecord? ValidateOptionalMajorVersion(
        ModuleDiscoveryCandidate candidate,
        string? declaredVersion,
        string currentVersion,
        string invalidCode,
        string incompatibleCode,
        string invalidMessage,
        string incompatibleMessage)
    {
        if (string.IsNullOrWhiteSpace(declaredVersion))
        {
            return null;
        }

        if (!Version.TryParse(currentVersion, out var current)
            || !Version.TryParse(declaredVersion, out var declared))
        {
            return Reject(candidate, invalidCode, invalidMessage);
        }

        return declared.Major == current.Major
            ? null
            : Reject(candidate, incompatibleCode, incompatibleMessage);
    }

    private static ModuleLoadRecord? ValidateRequiredConfiguration(
        ModuleDiscoveryCandidate candidate,
        ModuleDescriptor descriptor,
        ModuleLoadingPolicy policy)
    {
        var missing = descriptor.RequiredConfiguration
            .Where(static requirement => requirement.Required)
            .Where(requirement => !policy.BoundConfigurationKeys.Contains(requirement.Key))
            .ToArray();

        if (missing.Length == 0)
        {
            return null;
        }

        return new ModuleLoadRecord(
            candidate,
            ModuleLoadStatus.Rejected,
            diagnostics: missing
                .Select(requirement => Diagnostic(
                    "module_load.required_configuration_missing",
                    $"Module required configuration is not bound: {requirement.Key}",
                    ModuleLoadDiagnosticSeverity.Error,
                    candidate))
                .ToArray());
    }

    private static ModuleIsolationBoundary CreateIsolationBoundary(ModuleDiscoveryCandidate candidate)
    {
        var source = candidate.Manifest.Source;
        return source.Root.SourceKind switch
        {
            ModuleDiscoverySourceKind.BuiltIn => new ModuleIsolationBoundary(
                ModuleIsolationKind.BuiltInShared,
                $"builtin:{candidate.Manifest.ModuleId}",
                source.ManifestPath,
                collectible: false),
            ModuleDiscoverySourceKind.Workspace or ModuleDiscoverySourceKind.UserHome or ModuleDiscoverySourceKind.ThirdPartyDirectory or ModuleDiscoverySourceKind.Package => new ModuleIsolationBoundary(
                ModuleIsolationKind.DirectoryAssemblyLoadContext,
                $"module:{candidate.Manifest.ModuleId}",
                source.ManifestPath,
                collectible: true),
            _ => new ModuleIsolationBoundary(ModuleIsolationKind.Unspecified, $"module:{candidate.Manifest.ModuleId}", source.ManifestPath),
        };
    }

    private static IReadOnlyList<ModuleServiceRegistration> CreateServiceRegistrations(ModuleDescriptor descriptor)
    {
        var binding = descriptor.ImplementationBinding!;
        var implementationId = string.IsNullOrWhiteSpace(binding.TypeName)
            ? binding.ProjectName
            : $"{binding.ProjectName}:{binding.TypeName}";

        return
        [
            new ModuleServiceRegistration(
                descriptor.ModuleId,
                implementationId,
                ModuleServiceLifetime.Singleton,
                descriptor.Capabilities.Select(static capability => capability.CapabilityId).ToArray()),
        ];
    }

    private static ModuleLoadRecord Reject(ModuleDiscoveryCandidate candidate, string code, string message)
        => new(candidate, ModuleLoadStatus.Rejected, diagnostics: [Diagnostic(code, message, ModuleLoadDiagnosticSeverity.Error, candidate)]);

    private static ModuleLoadDiagnostic Diagnostic(
        string code,
        string message,
        ModuleLoadDiagnosticSeverity severity,
        ModuleDiscoveryCandidate candidate)
        => new(code, message, severity, candidate.Manifest.ModuleId, candidate.Manifest.Source.ManifestPath);
}
