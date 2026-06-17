using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Tools;

/// <summary>
/// 工具审批要求。
/// Tool approval requirement.
/// </summary>
public enum ToolApprovalRequirement
{
    None = 0,
    Optional = 1,
    Required = 2,
}

/// <summary>
/// 工具并发类别。
/// Tool concurrency class.
/// </summary>
public enum ToolConcurrencyClass
{
    Sequential = 0,
    SharedReadOnly = 1,
    Exclusive = 2,
}

/// <summary>
/// 工具实现类型。
/// Tool implementation kind.
/// </summary>
public enum ToolImplementationKind
{
    Managed = 0,
    ExternalProcess = 1,
    ProviderHosted = 2,
    McpStdio = 3,
    McpHttp = 4,
    PlatformNative = 5,
    Unavailable = 6,
}

/// <summary>
/// 工具种类，用于区分 Kernel tool 与外部能力 tool。
/// Tool kind used to distinguish Kernel tools from external capability tools.
/// </summary>
public enum ToolKind
{
    Unspecified = 0,
    Kernel = 1,
    Capability = 2,
    ModuleCapability = 3,
}

/// <summary>
/// JSON schema 引用。
/// JSON schema reference.
/// </summary>
public sealed record JsonSchemaRef
{
    public JsonSchemaRef(string schemaId, string? version = null, StructuredValue? inlineSchema = null)
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
/// 工具权限声明。
/// Tool permission declaration.
/// </summary>
public sealed record PermissionDeclaration
{
    public PermissionDeclaration(
        IReadOnlyList<string>? requiredScopes = null,
        bool requiresHumanGate = true,
        string? rationale = null)
    {
        RequiredScopes = requiredScopes ?? Array.Empty<string>();
        RequiresHumanGate = requiresHumanGate;
        Rationale = rationale;
    }

    public IReadOnlyList<string> RequiredScopes { get; }

    public bool RequiresHumanGate { get; }

    public string? Rationale { get; }
}

/// <summary>
/// 工具审计画像。
/// Tool audit profile.
/// </summary>
public sealed record AuditProfile
{
    public AuditProfile(bool required = true, IReadOnlyList<string>? eventKinds = null, bool redactSensitiveValues = true)
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
/// 工具能力描述。
/// Tool capability descriptor.
/// </summary>
public sealed record ToolCapability(string Name, string? Description = null)
{
    public string Name { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Name, nameof(Name));
}

/// <summary>
/// 工具运行时依赖声明。
/// Tool runtime requirement declaration.
/// </summary>
public sealed record ToolRuntimeRequirement
{
    /// <summary>
    /// 初始化工具运行时依赖声明。
    /// Initializes a tool runtime requirement declaration.
    /// </summary>
    public ToolRuntimeRequirement(string key, string? displayName = null, string? description = null, bool required = true)
    {
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        DisplayName = displayName;
        Description = description;
        Required = required;
    }

    public string Key { get; }

    public string? DisplayName { get; }

    public string? Description { get; }

    public bool Required { get; }
}

/// <summary>
/// 工具能力探测结果。
/// Tool capability probe result.
/// </summary>
public sealed record ToolCapabilityProbe
{
    /// <summary>
    /// 初始化工具能力探测结果。
    /// Initializes a tool capability probe result.
    /// </summary>
    public ToolCapabilityProbe(bool available, string? reason = null, DateTimeOffset? probedAt = null)
    {
        Available = available;
        Reason = reason;
        ProbedAt = probedAt;
    }

    public bool Available { get; }

    public string? Reason { get; }

    public DateTimeOffset? ProbedAt { get; }
}

/// <summary>
/// 工具 fallback 策略。
/// Tool fallback policy.
/// </summary>
public sealed record ToolFallbackPolicy
{
    /// <summary>
    /// 初始化工具 fallback 策略。
    /// Initializes a tool fallback policy.
    /// </summary>
    public ToolFallbackPolicy(
        string strategy,
        IReadOnlyList<ToolImplementationKind>? preferredImplementationKinds = null,
        string? description = null)
    {
        Strategy = IdentifierGuard.AgainstNullOrWhiteSpace(strategy, nameof(strategy));
        PreferredImplementationKinds = preferredImplementationKinds ?? Array.Empty<ToolImplementationKind>();
        Description = description;
    }

    public string Strategy { get; }

    public IReadOnlyList<ToolImplementationKind> PreferredImplementationKinds { get; }

    public string? Description { get; }
}

/// <summary>
/// 平台工具 profile。
/// Platform tool profile.
/// </summary>
public sealed record PlatformToolProfile
{
    /// <summary>
    /// 初始化平台工具 profile。
    /// Initializes a platform tool profile.
    /// </summary>
    public PlatformToolProfile(
        string platform,
        IReadOnlyList<string>? enabledToolKeys = null,
        IReadOnlyList<string>? disabledToolKeys = null,
        IReadOnlyList<ToolImplementationKind>? defaultImplementationKinds = null)
    {
        Platform = IdentifierGuard.AgainstNullOrWhiteSpace(platform, nameof(platform));
        EnabledToolKeys = enabledToolKeys ?? Array.Empty<string>();
        DisabledToolKeys = disabledToolKeys ?? Array.Empty<string>();
        DefaultImplementationKinds = defaultImplementationKinds ?? Array.Empty<ToolImplementationKind>();
    }

    public string Platform { get; }

    public IReadOnlyList<string> EnabledToolKeys { get; }

    public IReadOnlyList<string> DisabledToolKeys { get; }

    public IReadOnlyList<ToolImplementationKind> DefaultImplementationKinds { get; }
}

/// <summary>
/// 工具实现绑定。
/// Tool implementation binding.
/// </summary>
public sealed record ToolImplementationBinding
{
    /// <summary>
    /// 初始化工具实现绑定。
    /// Initializes a tool implementation binding.
    /// </summary>
    public ToolImplementationBinding(
        string toolKey,
        ToolImplementationKind implementationKind,
        string? implementationId = null,
        IReadOnlyList<ToolRuntimeRequirement>? requirements = null,
        ToolCapabilityProbe? probe = null,
        ToolFallbackPolicy? fallbackPolicy = null,
        PlatformToolProfile? platformProfile = null)
    {
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        ImplementationKind = implementationKind;
        ImplementationId = implementationId;
        Requirements = requirements ?? Array.Empty<ToolRuntimeRequirement>();
        Probe = probe;
        FallbackPolicy = fallbackPolicy;
        PlatformProfile = platformProfile;
    }

    public string ToolKey { get; }

    public ToolImplementationKind ImplementationKind { get; }

    public string? ImplementationId { get; }

    public IReadOnlyList<ToolRuntimeRequirement> Requirements { get; }

    public ToolCapabilityProbe? Probe { get; }

    public ToolFallbackPolicy? FallbackPolicy { get; }

    public PlatformToolProfile? PlatformProfile { get; }
}

/// <summary>
/// 工具 custom/freeform 输入定义。
/// Tool custom/freeform input definition.
/// </summary>
public sealed record ToolCustomInputDefinition
{
    /// <summary>
    /// 初始化工具 custom/freeform 输入定义。
    /// Initializes a tool custom/freeform input definition.
    /// </summary>
    public ToolCustomInputDefinition(string description, JsonElement format)
    {
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        Format = format.Clone();
    }

    public string Description { get; }

    public JsonElement Format { get; }
}

/// <summary>
/// 工具描述符。
/// Tool descriptor.
/// </summary>
public sealed record ToolDescriptor
{
    /// <summary>
    /// 初始化工具描述符。
    /// Initializes a tool descriptor.
    /// </summary>
    public ToolDescriptor(
        string key,
        string displayName,
        string description,
        IReadOnlyList<ToolCapability>? capabilities = null,
        ToolApprovalRequirement approvalRequirement = ToolApprovalRequirement.None,
        ToolConcurrencyClass concurrencyClass = ToolConcurrencyClass.Sequential,
        ToolImplementationBinding? implementationBinding = null,
        JsonElement? inputSchema = null,
        JsonElement? outputSchema = null,
        ToolCustomInputDefinition? customInputDefinition = null,
        ToolKind kind = ToolKind.Capability,
        JsonSchemaRef? inputSchemaRef = null,
        JsonSchemaRef? outputSchemaRef = null,
        PermissionDeclaration? permissions = null,
        SideEffectProfile? sideEffects = null,
        AuditProfile? audit = null)
    {
        Key = IdentifierGuard.AgainstNullOrWhiteSpace(key, nameof(key));
        DisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(displayName, nameof(displayName));
        Description = IdentifierGuard.AgainstNullOrWhiteSpace(description, nameof(description));
        Capabilities = capabilities ?? Array.Empty<ToolCapability>();
        ApprovalRequirement = approvalRequirement;
        ConcurrencyClass = concurrencyClass;
        ImplementationBinding = implementationBinding;
        InputSchema = inputSchema?.Clone();
        OutputSchema = outputSchema?.Clone();
        CustomInputDefinition = customInputDefinition;
        Kind = kind;
        InputSchemaRef = inputSchemaRef;
        OutputSchemaRef = outputSchemaRef;
        Permissions = permissions ?? CreateDefaultPermissionDeclaration(Key, approvalRequirement);
        SideEffects = sideEffects ?? CreateDefaultSideEffectProfile(Key, approvalRequirement, concurrencyClass);
        Audit = audit ?? CreateDefaultAuditProfile(Key);
    }

    public string Key { get; }

    /// <summary>
    /// 新架构下的工具标识，与既有 Key 保持同值。
    /// Tool identifier in the new architecture, equal to the existing Key.
    /// </summary>
    public string ToolId => Key;

    public string DisplayName { get; }

    /// <summary>
    /// 新架构下的工具名称，与既有 DisplayName 保持同值。
    /// Tool name in the new architecture, equal to the existing DisplayName.
    /// </summary>
    public string Name => DisplayName;

    public string Description { get; }

    public ToolKind Kind { get; }

    public IReadOnlyList<ToolCapability> Capabilities { get; }

    public ToolApprovalRequirement ApprovalRequirement { get; }

    public ToolConcurrencyClass ConcurrencyClass { get; }

    public ToolImplementationBinding? ImplementationBinding { get; }

    public JsonElement? InputSchema { get; }

    public JsonElement? OutputSchema { get; }

    public ToolCustomInputDefinition? CustomInputDefinition { get; }

    public JsonSchemaRef? InputSchemaRef { get; }

    public JsonSchemaRef? OutputSchemaRef { get; }

    public PermissionDeclaration Permissions { get; }

    public SideEffectProfile SideEffects { get; }

    public AuditProfile Audit { get; }

    /// <summary>
    /// 判断工具描述符是否落在治理信封允许的工具、副作用和人工 gate 边界内。
    /// Determines whether the tool descriptor fits the tool, side-effect, and human-gate boundaries of the governance envelope.
    /// </summary>
    public bool IsAllowedBy(GovernanceEnvelope governance)
    {
        ArgumentNullException.ThrowIfNull(governance);

        return governance.AllowedToolIds.Contains(ToolId, StringComparer.Ordinal)
               && SideEffects.Level != SideEffectLevel.Unspecified
               && governance.MaxSideEffectLevel != SideEffectLevel.Unspecified
               && SideEffects.Level <= governance.MaxSideEffectLevel
               && (!Permissions.RequiresHumanGate || governance.RequiresHumanGate);
    }

    /// <summary>
    /// 将工具描述符投影为 Module Plane 统一描述符。
    /// Projects the tool descriptor into the unified Module Plane descriptor.
    /// </summary>
    public ModuleDescriptor ToModuleDescriptor(
        ModuleTrustLevel trustLevel = ModuleTrustLevel.BuiltIn,
        ModuleHealthProbe? health = null,
        ModuleImplementationBinding? implementationBinding = null)
    {
        var permission = new PermissionEnvelope(
            scopes: Permissions.RequiredScopes,
            requiresHumanGate: Permissions.RequiresHumanGate,
            reason: Permissions.Rationale);

        var capability = new ModuleCapabilityDescriptor(
            $"tool.{NormalizePolicyToken(Key)}",
            DisplayName,
            inputSchema: InputSchemaRef is null ? null : new ModuleSchemaRef(InputSchemaRef.SchemaId, InputSchemaRef.Version, InputSchemaRef.InlineSchema),
            outputSchema: OutputSchemaRef is null ? null : new ModuleSchemaRef(OutputSchemaRef.SchemaId, OutputSchemaRef.Version, OutputSchemaRef.InlineSchema),
            permission: permission,
            sideEffects: SideEffects);
        var configurationSchema = new ModuleSchemaRef($"tool.{NormalizePolicyToken(Key)}.configuration");
        var runtimeDependency = new ModuleRuntimeDependency(
            ImplementationBinding?.ImplementationId ?? Key,
            $"{DisplayName} implementation",
            ModuleRuntimeDependencyKind.DotNetAssembly,
            required: true);

        return new ModuleDescriptor(
            Key,
            ModuleKind.Tool,
            DisplayName,
            version: "1.0",
            capabilities: [capability],
            configurationSchema: configurationSchema,
            permission: permission,
            sideEffects: SideEffects,
            audit: new ModuleAuditProfile(Audit.Required, Audit.EventKinds, Audit.RedactSensitiveValues),
            trustLevel: trustLevel,
            requiredConfiguration:
            [
                new ModuleConfigurationRequirement(
                    $"tool.{NormalizePolicyToken(Key)}.configuration",
                    $"{DisplayName} configuration",
                    valueSchema: configurationSchema,
                    required: false)
            ],
            runtimeDependencies: [runtimeDependency],
            minimumTianShuVersion: "0.6.0",
            health: health,
            implementationBinding: implementationBinding ?? (ImplementationBinding is null
                ? null
                : new ModuleImplementationBinding(ImplementationBinding.ImplementationKind.ToString(), ImplementationBinding.ImplementationId, ImplementationBinding.ToolKey)));
    }

    private static PermissionDeclaration CreateDefaultPermissionDeclaration(string toolKey, ToolApprovalRequirement approvalRequirement)
        => new(
            requiredScopes: [$"tool.{NormalizePolicyToken(toolKey)}"],
            requiresHumanGate: approvalRequirement == ToolApprovalRequirement.Required,
            rationale: approvalRequirement == ToolApprovalRequirement.Required
                ? "Tool may produce side effects and must pass Execution Runtime governance."
                : "Tool must be invoked through Execution Runtime governance.");

    private static SideEffectProfile CreateDefaultSideEffectProfile(
        string toolKey,
        ToolApprovalRequirement approvalRequirement,
        ToolConcurrencyClass concurrencyClass)
    {
        if (IsShellLikeTool(toolKey))
        {
            return new SideEffectProfile(
                SideEffectLevel.HostMutation,
                affectedResources: ["command", "process", "workspace"],
                reversible: false,
                requiresAudit: true);
        }

        if (approvalRequirement == ToolApprovalRequirement.Required
            || concurrencyClass == ToolConcurrencyClass.Exclusive)
        {
            return new SideEffectProfile(
                SideEffectLevel.WorkspaceWrite,
                affectedResources: ["workspace"],
                reversible: false,
                requiresAudit: true);
        }

        if (concurrencyClass == ToolConcurrencyClass.SharedReadOnly)
        {
            return new SideEffectProfile(
                SideEffectLevel.ReadOnly,
                affectedResources: ["workspace"],
                reversible: true,
                requiresAudit: true);
        }

        return new SideEffectProfile(
            SideEffectLevel.None,
            affectedResources: ["runtime"],
            reversible: true,
            requiresAudit: true);
    }

    private static AuditProfile CreateDefaultAuditProfile(string toolKey)
        => new(required: true, eventKinds: [$"tool.{NormalizePolicyToken(toolKey)}.invoked"], redactSensitiveValues: true);

    private static bool IsShellLikeTool(string toolKey)
        => toolKey.Contains("shell", StringComparison.OrdinalIgnoreCase)
           || toolKey.Contains("exec", StringComparison.OrdinalIgnoreCase)
           || toolKey.Contains("stdin", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePolicyToken(string value)
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
/// 工具调用请求。
/// Tool invocation request.
/// </summary>
public sealed record ToolInvocationRequest
{
    /// <summary>
    /// 初始化工具调用请求。
    /// Initializes a tool-invocation request.
    /// </summary>
    public ToolInvocationRequest(
        CallId callId,
        string toolKey,
        string operation,
        StructuredValue input,
        MetadataBag? metadata = null)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        Operation = IdentifierGuard.AgainstNullOrWhiteSpace(operation, nameof(operation));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public string Operation { get; }

    public StructuredValue Input { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 工具流式输出项。
/// Tool stream item.
/// </summary>
public sealed record ToolStreamItem
{
    /// <summary>
    /// 初始化工具流式输出项。
    /// Initializes a tool stream item.
    /// </summary>
    public ToolStreamItem(string channel, StructuredValue payload, bool isTerminal = false)
    {
        Channel = IdentifierGuard.AgainstNullOrWhiteSpace(channel, nameof(channel));
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        IsTerminal = isTerminal;
    }

    public string Channel { get; }

    public StructuredValue Payload { get; }

    public bool IsTerminal { get; }
}

/// <summary>
/// 统一工具调用包络。
/// Unified tool invocation envelope.
/// </summary>
public sealed record ToolInvocationEnvelope
{
    public ToolInvocationEnvelope(
        CallId callId,
        string toolId,
        string operation,
        StructuredValue input,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
    {
        CallId = callId;
        ToolId = IdentifierGuard.AgainstNullOrWhiteSpace(toolId, nameof(toolId));
        Operation = IdentifierGuard.AgainstNullOrWhiteSpace(operation, nameof(operation));
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public CallId CallId { get; }

    public string ToolId { get; }

    public string Operation { get; }

    public StructuredValue Input { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 统一工具调用上下文，暴露执行层批准后的最小上下文。
/// Unified tool invocation context exposing the minimum context approved by Execution Runtime.
/// </summary>
public sealed record ToolInvocationContext
{
    public ToolInvocationContext(
        string runtimeStepId,
        string sourceIntentId,
        string sourceGraphId,
        string sourceStageId,
        string sourceKernelOperationId,
        string? workingDirectory = null,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKernelOperationId, nameof(sourceKernelOperationId));
        WorkingDirectory = workingDirectory;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public string SourceIntentId { get; }

    public string SourceGraphId { get; }

    public string SourceStageId { get; }

    public string SourceKernelOperationId { get; }

    public string? WorkingDirectory { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 宿主工具输出内容项。
/// Host tool output content item.
/// </summary>
public sealed record ToolOutputContentItem(
    string Type,
    string? Text = null,
    string? ImageUrl = null,
    string? Detail = null);

/// <summary>
/// 工具调用失败模型。
/// Tool-invocation failure model.
/// </summary>
public sealed record ToolInvocationFailure
{
    /// <summary>
    /// 初始化工具调用失败模型。
    /// Initializes a tool-invocation failure model.
    /// </summary>
    public ToolInvocationFailure(
        string code,
        string message,
        bool isRetryable = false,
        ProblemDetails? problem = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
        IsRetryable = isRetryable;
        Problem = problem;
    }

    public string Code { get; }

    public string Message { get; }

    public bool IsRetryable { get; }

    public ProblemDetails? Problem { get; }
}

/// <summary>
/// 工具调用结果。
/// Tool-invocation result.
/// </summary>
public sealed record ToolInvocationResult
{
    /// <summary>
    /// 初始化工具调用结果。
    /// Initializes a tool-invocation result.
    /// </summary>
    public ToolInvocationResult(
        CallId callId,
        string toolKey,
        IReadOnlyList<ToolStreamItem>? streamItems = null,
        ArtifactRef? outputArtifact = null,
        ToolInvocationFailure? failure = null,
        IReadOnlyList<ToolOutputContentItem>? outputContentItems = null,
        IReadOnlyList<JsonElement>? rawOutputContentItems = null)
    {
        CallId = callId;
        ToolKey = IdentifierGuard.AgainstNullOrWhiteSpace(toolKey, nameof(toolKey));
        StreamItems = streamItems ?? Array.Empty<ToolStreamItem>();
        OutputArtifact = outputArtifact;
        Failure = failure;
        OutputContentItems = outputContentItems ?? Array.Empty<ToolOutputContentItem>();
        RawOutputContentItems = rawOutputContentItems is null
            ? Array.Empty<JsonElement>()
            : rawOutputContentItems.Select(static item => item.Clone()).ToArray();
    }

    public CallId CallId { get; }

    public string ToolKey { get; }

    public IReadOnlyList<ToolStreamItem> StreamItems { get; }

    public ArtifactRef? OutputArtifact { get; }

    public ToolInvocationFailure? Failure { get; }

    public IReadOnlyList<ToolOutputContentItem> OutputContentItems { get; }

    public IReadOnlyList<JsonElement> RawOutputContentItems { get; }
}
