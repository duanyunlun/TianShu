using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Configuration;

/// <summary>
/// 面向用户和宿主消费的配置分类。
/// Human-facing configuration category for hosts and projections.
/// </summary>
public enum ConfigurationCategoryKind
{
    Foundation = 0,
    ConnectivityModel = 1,
    AgentBehavior = 2,
    SecurityGovernance = 3,
    CapabilitiesTools = 4,
    IdentityMemory = 5,
    Workspace = 6,
    DiagnosticsState = 7,
    Experience = 8,
    ExtensionsImports = 9,
    KernelCore = 10,
    ExecutionRuntime = 11,
    ModulePlane = 12,
}

/// <summary>
/// 配置值类型，用于 UI 和外部宿主选择合适的编辑器。
/// Configuration value kind used by hosts to choose a proper editor.
/// </summary>
public enum ConfigurationValueKind
{
    String = 0,
    Boolean = 1,
    Integer = 2,
    Number = 3,
    Path = 4,
    Enum = 5,
    Array = 6,
    Object = 7,
    Duration = 8,
    SecretReference = 9,
}

/// <summary>
/// 配置字段的消费侧编辑能力。
/// Consumer-side edit capability for a configuration field.
/// </summary>
public enum ConfigurationFieldEditMode
{
    ReadOnly = 0,
    Editable = 1,
    RequiresPreview = 2,
    SecretReferenceOnly = 3,
}

/// <summary>
/// 配置来源层类型。
/// Configuration source layer kind.
/// </summary>
public enum ConfigurationSourceKind
{
    BuiltIn = 0,
    System = 1,
    User = 2,
    Project = 3,
    WorkingDirectory = 4,
    Session = 5,
    CommandLine = 6,
    Environment = 7,
    Imported = 8,
}

/// <summary>
/// 配置问题严重级别。
/// Severity of a configuration issue.
/// </summary>
public enum ConfigurationIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// 配置变更操作。
/// Configuration change operation.
/// </summary>
public enum ConfigurationChangeOperation
{
    Set = 0,
    Unset = 1,
    ResetToDefault = 2,
}

/// <summary>
/// 标准配置分类 id。
/// Standard configuration category identifiers.
/// </summary>
public static class ConfigurationCategoryIds
{
    public const string Foundation = "foundation";
    public const string ConnectivityModel = "connectivity_model";
    public const string AgentBehavior = "agent_behavior";
    public const string SecurityGovernance = "security_governance";
    public const string CapabilitiesTools = "capabilities_tools";
    public const string IdentityMemory = "identity_memory";
    public const string Workspace = "workspace";
    public const string DiagnosticsState = "diagnostics_state";
    public const string Experience = "experience";
    public const string ExtensionsImports = "extensions_imports";
    public const string KernelCore = "kernel_core";
    public const string ExecutionRuntime = "execution_runtime";
    public const string ModulePlane = "module_plane";
}

/// <summary>
/// 标准配置问题 code。
/// Standard configuration issue codes.
/// </summary>
public static class ConfigurationIssueCodes
{
    public const string FieldUnmapped = "config.field.unmapped";
    public const string EnumInvalid = "config.field.enum_invalid";
    public const string ValueKindInvalid = "config.field.value_kind_invalid";
    public const string BudgetNegative = "config.field.budget_negative";
    public const string RequiredFieldMissing = "config.field.required_missing";
    public const string SecretPlaintextForbidden = "config.secret.plaintext_forbidden";
    public const string SecretReferenceMissing = "config.secret.reference_missing";
    public const string MutualExclusionConflict = "config.field.mutual_exclusion";
    public const string FormalFactsRejectedUnmapped = "config.facts.unmapped_rejected";
}

/// <summary>
/// 配置分类描述。
/// Configuration category descriptor.
/// </summary>
public sealed record ConfigurationCategoryDescriptor
{
    public required string Id { get; init; }

    public required ConfigurationCategoryKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public int Order { get; init; }
}

/// <summary>
/// 配置字段分组描述。
/// Configuration field group descriptor.
/// </summary>
public sealed record ConfigurationGroupDescriptor
{
    public required string Id { get; init; }

    public required string CategoryId { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public int Order { get; init; }
}

/// <summary>
/// 枚举或受限值候选项。
/// Allowed value option for enum-like fields.
/// </summary>
public sealed record ConfigurationAllowedValue
{
    public required StructuredValue Value { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }
}

/// <summary>
/// 配置字段描述。
/// Configuration field descriptor.
/// </summary>
public sealed record ConfigurationFieldDescriptor
{
    public required string Key { get; init; }

    public required string GroupId { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required ConfigurationValueKind ValueKind { get; init; }

    public ConfigurationFieldEditMode EditMode { get; init; } = ConfigurationFieldEditMode.RequiresPreview;

    public StructuredValue? DefaultValue { get; init; }

    public bool IsSecret { get; init; }

    public bool IsAdvanced { get; init; }

    public IReadOnlyList<ConfigurationAllowedValue> AllowedValues { get; init; } = Array.Empty<ConfigurationAllowedValue>();
}

/// <summary>
/// 配置来源层描述。
/// Configuration source layer descriptor.
/// </summary>
public sealed record ConfigurationSourceLayer
{
    public required string Id { get; init; }

    public required ConfigurationSourceKind Kind { get; init; }

    public required string DisplayName { get; init; }

    public string? Path { get; init; }

    public int Order { get; init; }

    public bool Exists { get; init; } = true;

    public bool IsWritable { get; init; }
}

/// <summary>
/// 单个配置字段的有效值投影。
/// Effective value projection for one configuration field.
/// </summary>
public sealed record ConfigurationFieldValue
{
    public required string Key { get; init; }

    public StructuredValue? Value { get; init; }

    public StructuredValue? DefaultValue { get; init; }

    public string? SourceLayerId { get; init; }

    public bool IsConfigured { get; init; }

    public bool IsSensitive { get; init; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// 配置校验或投影问题。
/// Configuration validation or projection issue.
/// </summary>
public sealed record ConfigurationIssue
{
    public required ConfigurationIssueSeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? FieldKey { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// 面向所有消费层的配置投影。
/// Configuration projection consumed by all presentation hosts.
/// </summary>
public sealed record ConfigurationProjection
{
    public int SchemaVersion { get; init; } = 1;

    public string? Profile { get; init; }

    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ConfigurationCategoryDescriptor> Categories { get; init; } = Array.Empty<ConfigurationCategoryDescriptor>();

    public IReadOnlyList<ConfigurationGroupDescriptor> Groups { get; init; } = Array.Empty<ConfigurationGroupDescriptor>();

    public IReadOnlyList<ConfigurationFieldDescriptor> Fields { get; init; } = Array.Empty<ConfigurationFieldDescriptor>();

    public IReadOnlyList<ConfigurationFieldValue> Values { get; init; } = Array.Empty<ConfigurationFieldValue>();

    public IReadOnlyList<ConfigurationSourceLayer> Sources { get; init; } = Array.Empty<ConfigurationSourceLayer>();

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// 单条配置变更。
/// Single configuration change.
/// </summary>
public sealed record ConfigurationChange
{
    public required ConfigurationChangeOperation Operation { get; init; }

    public required string Key { get; init; }

    public StructuredValue? Value { get; init; }
}

/// <summary>
/// 配置变更集。
/// Configuration change set.
/// </summary>
public sealed record ConfigurationChangeSet
{
    public string? TargetPath { get; init; }

    public string? Profile { get; init; }

    public IReadOnlyList<ConfigurationChange> Changes { get; init; } = Array.Empty<ConfigurationChange>();
}

/// <summary>
/// 配置变更预览结果。
/// Preview result for a configuration change set.
/// </summary>
public sealed record ConfigurationChangePreview
{
    public required ConfigurationProjection Before { get; init; }

    public required ConfigurationProjection After { get; init; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();

    public bool CanApply => Issues.All(static issue => issue.Severity != ConfigurationIssueSeverity.Error);
}

/// <summary>
/// 配置应用结果。
/// Configuration apply result.
/// </summary>
public sealed record ConfigurationApplyResult
{
    public required bool Applied { get; init; }

    public string? TargetPath { get; init; }

    public ConfigurationProjection? Projection { get; init; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// 已解析配置层快照，供非 GUI 消费层构造投影。
/// Resolved configuration layer snapshot used to build projections.
/// </summary>
public sealed record ConfigurationLayerSnapshot
{
    public required ConfigurationSourceLayer Source { get; init; }

    public IReadOnlyDictionary<string, StructuredValue> Values { get; init; } =
        new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 配置投影构造请求。
/// Configuration projection build request.
/// </summary>
public sealed record ConfigurationProjectionRequest
{
    public string? Profile { get; init; }

    public IReadOnlyList<ConfigurationLayerSnapshot> Layers { get; init; } = Array.Empty<ConfigurationLayerSnapshot>();
}

/// <summary>
/// Kernel 可消费的只读配置事实。
/// Read-only configuration facts consumable by Kernel.
/// </summary>
public sealed record KernelConfigurationFacts
{
    public bool Enabled { get; init; } = true;

    public string? DefaultGraphId { get; init; }

    public bool AdaptiveOrchestrationEnabled { get; init; }

    public IReadOnlyList<string> AllowedKernelTools { get; init; } = Array.Empty<string>();

    public int? MaxProposalsPerTurn { get; init; }

    public string? StrategyDefaultRegistry { get; init; }

    public string? StrategyPromotionGate { get; init; }

    public int? StrategyTrialRuns { get; init; }

    public long? TokenBudget { get; init; }

    public long? TimeBudgetMs { get; init; }

    public decimal? CostBudget { get; init; }

    public int? RetryBudget { get; init; }

    public int? ToolCallBudget { get; init; }

    public bool FailClosedValidation { get; init; } = true;

    public bool RequireGovernanceEnvelope { get; init; } = true;

    public bool RequireTracePolicy { get; init; } = true;

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Execution Runtime profile 配置事实。
/// Execution Runtime profile configuration facts.
/// </summary>
public sealed record ExecutionRuntimeProfileConfigurationFacts
{
    public required string ProfileId { get; init; }

    public long? TimeoutMs { get; init; }

    public long? StreamIdleTimeoutMs { get; init; }

    public int? RetryBudget { get; init; }

    public int? MaxParallelism { get; init; }

    public bool RequireSourceIds { get; init; } = true;

    public bool RequirePermissionEnvelope { get; init; } = true;

    public bool RequireTracePolicy { get; init; } = true;

    public bool DiagnosticsRefRequired { get; init; } = true;

    public bool RuntimeTraceRefRequired { get; init; } = true;

    public string SideEffectCeiling { get; init; } = "read_only";

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Execution Runtime 可消费的只读配置事实。
/// Read-only configuration facts consumable by Execution Runtime.
/// </summary>
public sealed record ExecutionConfigurationFacts
{
    public string DefaultProfile { get; init; } = "default";

    public IReadOnlyList<ExecutionRuntimeProfileConfigurationFacts> Profiles { get; init; } =
        Array.Empty<ExecutionRuntimeProfileConfigurationFacts>();

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 单个 Module 配置事实。
/// Configuration facts for one Module Plane entry.
/// </summary>
public sealed record ModuleConfigurationEntryFacts
{
    public required string ModuleArea { get; init; }

    public required string ModuleId { get; init; }

    public bool Enabled { get; init; } = true;

    public string? DescriptorRef { get; init; }

    public string? TrustLevel { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public string? HealthCheck { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Provider 静态配置事实。
/// Static configuration facts for one Provider Module.
/// </summary>
public sealed record ProviderConfigurationFacts
{
    public required string ProviderId { get; init; }

    public string? DisplayName { get; init; }

    public string? Kind { get; init; }

    public string? Transport { get; init; }

    public string? Endpoint { get; init; }

    public string? DefaultProtocol { get; init; }

    public IReadOnlyList<string> ProtocolCapabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ModelCatalogConfigurationFacts> ModelCatalog { get; init; } =
        Array.Empty<ModelCatalogConfigurationFacts>();

    public IReadOnlyList<string> SecretReferences { get; init; } = Array.Empty<string>();

    public bool SupportsStreaming { get; init; } = true;

    public bool SupportsWebSockets { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Provider model catalog 配置事实。
/// Configuration facts for one model catalog entry.
/// </summary>
public sealed record ModelCatalogConfigurationFacts
{
    public required string ModelId { get; init; }

    public string? ProviderId { get; init; }

    public string? NativeName { get; init; }

    public string? DisplayName { get; init; }

    public string? Family { get; init; }

    public long? ContextWindow { get; init; }

    public IReadOnlyList<string> ProtocolCapabilities { get; init; } = Array.Empty<string>();

    public bool Hidden { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Tool 静态配置事实。
/// Static configuration facts for one Tool Module.
/// </summary>
public sealed record ToolConfigurationFacts
{
    public required string ToolId { get; init; }

    public bool Enabled { get; init; } = true;

    public string? ProviderId { get; init; }

    public string? ImplementationBinding { get; init; }

    public string? ImplementationKind { get; init; }

    public ToolPermissionDeclarationFacts PermissionDeclaration { get; init; } = new();

    public ToolSideEffectProfileFacts SideEffectProfile { get; init; } = new();

    public ToolAuditProfileFacts AuditProfile { get; init; } = new();

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Tool permission declaration 配置事实。
/// Configuration facts for tool permission declaration.
/// </summary>
public sealed record ToolPermissionDeclarationFacts
{
    public string? ApprovalPolicy { get; init; }

    public bool? WriteRequiresApproval { get; init; }
}

/// <summary>
/// Tool side effect profile 配置事实。
/// Configuration facts for tool side-effect profile.
/// </summary>
public sealed record ToolSideEffectProfileFacts
{
    public string? Fallback { get; init; }

    public int? TimeoutSeconds { get; init; }

    public int? MaxReadBytes { get; init; }
}

/// <summary>
/// Tool audit profile 配置事实。
/// Configuration facts for tool audit profile.
/// </summary>
public sealed record ToolAuditProfileFacts
{
    public int? Priority { get; init; }

    public string? WorkingDirectory { get; init; }

    public string? EnvironmentPolicy { get; init; }
}

/// <summary>
/// Memory / Identity Module 静态配置事实。
/// Static configuration facts for Memory / Identity modules.
/// </summary>
public sealed record MemoryConfigurationFacts
{
    public bool Enabled { get; init; } = true;

    public string? DefaultProfile { get; init; }

    public IReadOnlyList<MemorySpaceConfigurationFacts> Spaces { get; init; } =
        Array.Empty<MemorySpaceConfigurationFacts>();

    public IReadOnlyList<MemoryProviderConfigurationFacts> Providers { get; init; } =
        Array.Empty<MemoryProviderConfigurationFacts>();

    public IReadOnlyList<MemoryBindingConfigurationFacts> Bindings { get; init; } =
        Array.Empty<MemoryBindingConfigurationFacts>();

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Memory space 静态配置事实。
/// Static configuration facts for one memory space.
/// </summary>
public sealed record MemorySpaceConfigurationFacts
{
    public required string SpaceId { get; init; }

    public string? Scope { get; init; }

    public string? ProviderId { get; init; }

    public bool ReadOnly { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Memory provider 静态配置事实。
/// Static configuration facts for one memory provider.
/// </summary>
public sealed record MemoryProviderConfigurationFacts
{
    public required string ProviderId { get; init; }

    public bool Enabled { get; init; } = true;

    public string? Kind { get; init; }

    public string? DisplayName { get; init; }

    public string? Mode { get; init; }

    public string? Root { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SecretReferences { get; init; } = Array.Empty<string>();

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Memory binding 静态配置事实。
/// Static configuration facts for one memory binding.
/// </summary>
public sealed record MemoryBindingConfigurationFacts
{
    public required string BindingId { get; init; }

    public string? SpaceId { get; init; }

    public string? ProviderId { get; init; }

    public string? Mode { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Diagnostics Module 静态配置事实。
/// Static configuration facts for Diagnostics modules.
/// </summary>
public sealed record DiagnosticsConfigurationFacts
{
    public bool Enabled { get; init; } = true;

    public string DefaultLevel { get; init; } = "stats";

    public string LogLevel { get; init; } = "info";

    public bool TraceEnabled { get; init; } = true;

    public bool RedactSecrets { get; init; } = true;

    public string? EventsJsonl { get; init; }

    public bool TelemetryEnabled { get; init; }

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Workspace / Environment Module 静态配置事实。
/// Static configuration facts for Workspace / Environment modules.
/// </summary>
public sealed record WorkspaceConfigurationFacts
{
    public IReadOnlyList<WorkspaceProfileConfigurationFacts> Profiles { get; init; } =
        Array.Empty<WorkspaceProfileConfigurationFacts>();

    public IReadOnlyList<ProjectTrustConfigurationFacts> Projects { get; init; } =
        Array.Empty<ProjectTrustConfigurationFacts>();

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Workspace profile 静态配置事实。
/// Static configuration facts for one workspace profile.
/// </summary>
public sealed record WorkspaceProfileConfigurationFacts
{
    public required string ProfileId { get; init; }

    public IReadOnlyList<string> RootMarkers { get; init; } = Array.Empty<string>();

    public string? DefaultWorkspace { get; init; }

    public string? TrustPolicy { get; init; }

    public string? ArtifactRoot { get; init; }

    public string? StateRoot { get; init; }

    public string? Model { get; init; }

    public string? ModelLock { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Project trust 静态配置事实。
/// Static configuration facts for one project trust entry.
/// </summary>
public sealed record ProjectTrustConfigurationFacts
{
    public required string ProjectId { get; init; }

    public string? Path { get; init; }

    public string? Trust { get; init; }

    public string? TrustLevel { get; init; }

    public string? Profile { get; init; }

    public bool? ConfigAllowed { get; init; }

    public string? SourceLayerId { get; init; }
}

/// <summary>
/// Module Plane 可消费的只读配置事实。
/// Read-only configuration facts consumable by Module Plane.
/// </summary>
public sealed record ModuleConfigurationFacts
{
    public IReadOnlyList<string> DiscoveryRoots { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ModuleConfigurationEntryFacts> Entries { get; init; } =
        Array.Empty<ModuleConfigurationEntryFacts>();

    public IReadOnlyList<ProviderConfigurationFacts> Providers { get; init; } =
        Array.Empty<ProviderConfigurationFacts>();

    public IReadOnlyList<ToolConfigurationFacts> Tools { get; init; } =
        Array.Empty<ToolConfigurationFacts>();

    public MemoryConfigurationFacts Memory { get; init; } = new();

    public DiagnosticsConfigurationFacts Diagnostics { get; init; } = new();

    public WorkspaceConfigurationFacts Workspace { get; init; } = new();

    public IReadOnlyList<string> SourceLayerIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 正式配置事实聚合，只包含 schema catalog 覆盖的字段。
/// Formal configuration facts aggregate, containing only schema-covered fields.
/// </summary>
public sealed record ConfigurationFacts
{
    public required KernelConfigurationFacts Kernel { get; init; }

    public required ExecutionConfigurationFacts Execution { get; init; }

    public required ModuleConfigurationFacts Modules { get; init; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// Prompt 配置段合并模式。
/// Prompt section merge mode.
/// </summary>
public enum PromptConfigurationSectionMergeMode
{
    Replace = 0,
    Append = 1,
    Prepend = 2,
}

/// <summary>
/// Prompt 配置文件描述。
/// Prompt configuration file descriptor.
/// </summary>
public sealed record PromptConfigurationFileDescriptor
{
    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public string? Version { get; init; }

    public string? MinTianShuVersion { get; init; }

    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public string LoadStatus { get; init; } = "available";

    public string? UnavailableReason { get; init; }

    public bool IsSelected { get; init; }
}

/// <summary>
/// Prompt 配置段描述。
/// Prompt configuration section descriptor.
/// </summary>
public sealed record PromptConfigurationSectionDescriptor
{
    public required string Key { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public bool SupportsEnabled { get; init; } = true;

    public bool SupportsMode { get; init; } = true;
}

/// <summary>
/// Prompt 配置段当前值。
/// Current value of a prompt configuration section.
/// </summary>
public sealed record PromptConfigurationSectionValue
{
    public required string Key { get; init; }

    public bool Enabled { get; init; } = true;

    public PromptConfigurationSectionMergeMode Mode { get; init; } = PromptConfigurationSectionMergeMode.Append;

    public string? Text { get; init; }

    public string? File { get; init; }

    public bool IsConfigured { get; init; }

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// Prompt 配置投影。
/// Prompt configuration projection.
/// </summary>
public sealed record PromptConfigurationProjection
{
    public required string RootDirectory { get; init; }

    public string? SelectedFilePath { get; init; }

    public IReadOnlyList<PromptConfigurationFileDescriptor> Files { get; init; } = Array.Empty<PromptConfigurationFileDescriptor>();

    public IReadOnlyList<PromptConfigurationSectionDescriptor> Sections { get; init; } = Array.Empty<PromptConfigurationSectionDescriptor>();

    public IReadOnlyList<PromptConfigurationSectionValue> Values { get; init; } = Array.Empty<PromptConfigurationSectionValue>();

    public IReadOnlyList<ConfigurationIssue> Issues { get; init; } = Array.Empty<ConfigurationIssue>();
}

/// <summary>
/// Prompt 配置段变更。
/// Prompt configuration section change.
/// </summary>
public sealed record PromptConfigurationSectionChange
{
    public required string SectionKey { get; init; }

    public bool Enabled { get; init; } = true;

    public PromptConfigurationSectionMergeMode Mode { get; init; } = PromptConfigurationSectionMergeMode.Append;

    public string? Text { get; init; }
}
