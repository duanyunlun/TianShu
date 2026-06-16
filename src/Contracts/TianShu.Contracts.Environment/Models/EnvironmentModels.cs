using TianShu.Contracts.Kernel;
using TianShu.Contracts.Modules;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Environment;

/// <summary>
/// 宿主环境种类，表示当前环境来自哪类运行宿主。
/// Host-environment kind describing which runtime host produced the current environment.
/// </summary>
public enum EnvironmentHostKind
{
    Cli = 0,
    Vsix = 1,
    Web = 2,
    Service = 3,
    Embedded = 4,
}

/// <summary>
/// 能力可用性，表示当前能力面是否可直接使用或已被降级。
/// Capability availability indicating whether the capability surface is directly usable or degraded.
/// </summary>
public enum CapabilityAvailability
{
    Available = 0,
    Degraded = 1,
    Denied = 2,
}

/// <summary>
/// 能力不可用原因，表示平台、权限或宿主层面的限制来源。
/// Capability denial reason indicating which platform, permission, or host constraint caused unavailability.
/// </summary>
public enum CapabilityDenialReason
{
    UnsupportedPlatform = 0,
    MissingPermission = 1,
    HostPolicy = 2,
    MissingDependency = 3,
    NotInteractive = 4,
    Sandboxed = 5,
    DisabledByUser = 6,
}

/// <summary>
/// 输入能力快照。
/// Input capability snapshot.
/// </summary>
public sealed record InputCapabilities(
    bool SupportsText,
    bool SupportsImage,
    bool SupportsAudio,
    bool SupportsSelection,
    bool SupportsFollowUpQueue);

/// <summary>
/// 输出能力快照。
/// Output capability snapshot.
/// </summary>
public sealed record OutputCapabilities(
    bool SupportsStreamingText,
    bool SupportsRichText,
    bool SupportsImages,
    bool SupportsNotifications,
    bool SupportsArtifacts);

/// <summary>
/// 执行能力快照。
/// Execution capability snapshot.
/// </summary>
public sealed record ExecutionCapabilities(
    bool SupportsShell,
    bool SupportsInterrupt,
    bool SupportsResume,
    bool SupportsBackgroundJobs,
    bool SupportsRealtimeStreaming);

/// <summary>
/// 自动化能力快照。
/// Automation capability snapshot.
/// </summary>
public sealed record AutomationCapabilities(
    bool SupportsScheduling,
    bool SupportsFileWatchers,
    bool SupportsWebhooks,
    bool SupportsBrowserAutomation);

/// <summary>
/// UI 能力快照。
/// UI capability snapshot.
/// </summary>
public sealed record UiCapabilities(
    bool SupportsPlanSelector,
    bool SupportsAgentRoster,
    bool SupportsApprovalCards,
    bool SupportsProjectionTimeline,
    bool SupportsRichPreview);

/// <summary>
/// 宿主环境画像，表达当前 TianShu 所运行的宿主与平台信息。
/// Host-environment profile describing the host and platform where TianShu is currently running.
/// </summary>
public sealed record HostEnvironmentProfile
{
    /// <summary>
    /// 初始化宿主环境画像。
    /// Initializes a host-environment profile.
    /// </summary>
    public HostEnvironmentProfile(
        string environmentKey,
        EnvironmentHostKind hostKind,
        string platform,
        string runtimeName,
        string? workingDirectory = null)
    {
        EnvironmentKey = IdentifierGuard.AgainstNullOrWhiteSpace(environmentKey, nameof(environmentKey));
        HostKind = hostKind;
        Platform = IdentifierGuard.AgainstNullOrWhiteSpace(platform, nameof(platform));
        RuntimeName = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeName, nameof(runtimeName));
        WorkingDirectory = workingDirectory;
    }

    public string EnvironmentKey { get; }

    public EnvironmentHostKind HostKind { get; }

    public string Platform { get; }

    public string RuntimeName { get; }

    public string? WorkingDirectory { get; }
}

/// <summary>
/// 能力快照，收口输入、输出、执行、自动化和 UI 五个维度的能力面。
/// Capability snapshot that gathers input, output, execution, automation, and UI surfaces into a single view.
/// </summary>
public sealed record CapabilitySnapshot
{
    /// <summary>
    /// 初始化能力快照。
    /// Initializes a capability snapshot.
    /// </summary>
    public CapabilitySnapshot(
        InputCapabilities input,
        OutputCapabilities output,
        ExecutionCapabilities execution,
        AutomationCapabilities automation,
        UiCapabilities ui,
        CapabilityAvailability availability = CapabilityAvailability.Available,
        IReadOnlyList<CapabilityDenialReason>? denialReasons = null)
    {
        if (availability != CapabilityAvailability.Available && (denialReasons is null || denialReasons.Count == 0))
        {
            throw new ArgumentException("能力降级或拒绝时必须提供原因。", nameof(denialReasons));
        }

        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Execution = execution ?? throw new ArgumentNullException(nameof(execution));
        Automation = automation ?? throw new ArgumentNullException(nameof(automation));
        Ui = ui ?? throw new ArgumentNullException(nameof(ui));
        Availability = availability;
        DenialReasons = denialReasons ?? Array.Empty<CapabilityDenialReason>();
    }

    public InputCapabilities Input { get; }

    public OutputCapabilities Output { get; }

    public ExecutionCapabilities Execution { get; }

    public AutomationCapabilities Automation { get; }

    public UiCapabilities Ui { get; }

    public CapabilityAvailability Availability { get; }

    public IReadOnlyList<CapabilityDenialReason> DenialReasons { get; }
}

/// <summary>
/// 环境绑定，表达当前控制平面选中的环境画像及默认执行约束。
/// Environment binding that expresses the selected environment profile and default execution constraints.
/// </summary>
public sealed record EnvironmentBinding
{
    /// <summary>
    /// 初始化环境绑定。
    /// Initializes an environment binding.
    /// </summary>
    public EnvironmentBinding(
        string bindingKey,
        HostEnvironmentProfile environment,
        CapabilitySnapshot snapshot,
        string? defaultExecutionProfile = null,
        MetadataBag? metadata = null)
    {
        BindingKey = IdentifierGuard.AgainstNullOrWhiteSpace(bindingKey, nameof(bindingKey));
        Environment = environment ?? throw new ArgumentNullException(nameof(environment));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        DefaultExecutionProfile = defaultExecutionProfile;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string BindingKey { get; }

    public HostEnvironmentProfile Environment { get; }

    public CapabilitySnapshot Snapshot { get; }

    public string? DefaultExecutionProfile { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Workspace fact 类型，表达 workspace resolver 发现的只读事实类别。
/// Workspace-fact kind describing the read-only fact category discovered by a workspace resolver.
/// </summary>
public enum WorkspaceFactKind
{
    Unspecified = 0,
    WorkspaceRoot = 1,
    RootMarker = 2,
    ProjectFile = 3,
    LanguageMarker = 4,
    FrameworkMarker = 5,
    TrustPolicy = 6,
    ArtifactRoot = 7,
    StateRoot = 8,
    ReadOnlyNotice = 9,
}

/// <summary>
/// Workspace 解析状态；Rejected 表示 fail closed，DegradedReadOnly 表示只读降级提示。
/// Workspace resolution status; Rejected means fail closed, DegradedReadOnly means read-only degraded notice.
/// </summary>
public enum WorkspaceResolutionStatus
{
    Unspecified = 0,
    Resolved = 1,
    DegradedReadOnly = 2,
    Rejected = 3,
}

/// <summary>
/// Workspace fact 来源引用。
/// Workspace-fact source reference.
/// </summary>
public sealed record WorkspaceFactSource
{
    public WorkspaceFactSource(
        string sourceId,
        string sourceKind,
        string? sourcePath = null,
        string? packageId = null,
        string? resolverId = null,
        MetadataBag? metadata = null)
    {
        SourceId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceId, nameof(sourceId));
        SourceKind = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKind, nameof(sourceKind));
        SourcePath = sourcePath;
        PackageId = packageId;
        ResolverId = resolverId;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string SourceId { get; }

    public string SourceKind { get; }

    public string? SourcePath { get; }

    public string? PackageId { get; }

    public string? ResolverId { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Workspace fact，是可进入 ContextPolicy 与 diagnostics 的 provider-neutral 只读事实。
/// Workspace fact, a provider-neutral read-only fact that may enter ContextPolicy and diagnostics.
/// </summary>
public sealed record WorkspaceFact
{
    public WorkspaceFact(
        string factId,
        WorkspaceFactKind kind,
        string value,
        WorkspaceFactSource source,
        string? displayName = null,
        decimal confidence = 1,
        MetadataBag? metadata = null)
    {
        FactId = IdentifierGuard.AgainstNullOrWhiteSpace(factId, nameof(factId));
        if (kind is WorkspaceFactKind.Unspecified)
        {
            throw new ArgumentException("Workspace fact kind must be specified.", nameof(kind));
        }

        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (confidence < 0 || confidence > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), "置信度必须在 0 到 1 之间。");
        }

        Kind = kind;
        DisplayName = displayName;
        Confidence = confidence;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string FactId { get; }

    public WorkspaceFactKind Kind { get; }

    public string Value { get; }

    public WorkspaceFactSource Source { get; }

    public string? DisplayName { get; }

    public decimal Confidence { get; }

    public MetadataBag Metadata { get; }

    /// <summary>
    /// 将 workspace fact 投影为 Kernel ContextPolicy 可消费的候选上下文。
    /// Projects the workspace fact into a Kernel ContextPolicy candidate.
    /// </summary>
    public ContextSourceCandidate ToContextSourceCandidate()
        => new(
            FactId,
            ContextSourceKind.WorkspaceFact,
            Value,
            estimatedTokens: Math.Max(1, Value.Length / 4),
            confidence: Confidence,
            evidenceRef: $"workspace-fact://{FactId}",
            metadata: Metadata);
}

/// <summary>
/// Workspace 解析请求，只允许读取 workspace facts，不允许声明写入。
/// Workspace resolution request that only permits reading workspace facts and never declares writes.
/// </summary>
public sealed record WorkspaceResolutionRequest
{
    public WorkspaceResolutionRequest(
        string workspacePath,
        IReadOnlyList<string>? rootMarkers = null,
        IReadOnlyList<string>? ignoreGlobs = null,
        bool failClosedWhenUntrusted = true,
        MetadataBag? metadata = null)
    {
        WorkspacePath = IdentifierGuard.AgainstNullOrWhiteSpace(workspacePath, nameof(workspacePath));
        RootMarkers = rootMarkers ?? Array.Empty<string>();
        IgnoreGlobs = ignoreGlobs ?? Array.Empty<string>();
        FailClosedWhenUntrusted = failClosedWhenUntrusted;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string WorkspacePath { get; }

    public IReadOnlyList<string> RootMarkers { get; }

    public IReadOnlyList<string> IgnoreGlobs { get; }

    public bool FailClosedWhenUntrusted { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Workspace Module 调用上下文，承载 RuntimeStep 来源追踪和治理边界。
/// Workspace Module invocation context carrying RuntimeStep source tracing and governance boundary.
/// </summary>
public sealed record WorkspaceModuleInvocationContext
{
    public WorkspaceModuleInvocationContext(
        string runtimeStepId,
        string sourceIntentId,
        string sourceGraphId,
        string sourceStageId,
        string sourceKernelOperationId,
        PermissionEnvelope permission,
        SideEffectProfile sideEffect,
        MetadataBag? metadata = null)
    {
        RuntimeStepId = IdentifierGuard.AgainstNullOrWhiteSpace(runtimeStepId, nameof(runtimeStepId));
        SourceIntentId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceIntentId, nameof(sourceIntentId));
        SourceGraphId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceGraphId, nameof(sourceGraphId));
        SourceStageId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceStageId, nameof(sourceStageId));
        SourceKernelOperationId = IdentifierGuard.AgainstNullOrWhiteSpace(sourceKernelOperationId, nameof(sourceKernelOperationId));
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
        SideEffect = sideEffect ?? throw new ArgumentNullException(nameof(sideEffect));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string RuntimeStepId { get; }

    public string SourceIntentId { get; }

    public string SourceGraphId { get; }

    public string SourceStageId { get; }

    public string SourceKernelOperationId { get; }

    public PermissionEnvelope Permission { get; }

    public SideEffectProfile SideEffect { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Workspace 解析结果，包含只读 facts、来源、诊断引用和降级原因。
/// Workspace resolution result containing read-only facts, sources, diagnostics references, and degradation reasons.
/// </summary>
public sealed record WorkspaceResolutionResult
{
    public WorkspaceResolutionResult(
        WorkspaceResolutionStatus status,
        IReadOnlyList<WorkspaceFact>? facts = null,
        IReadOnlyList<WorkspaceFactSource>? sources = null,
        IReadOnlyList<string>? diagnosticsRefs = null,
        IReadOnlyList<string>? issues = null)
    {
        if (status is WorkspaceResolutionStatus.Unspecified)
        {
            throw new ArgumentException("Workspace resolution status must be specified.", nameof(status));
        }

        Status = status;
        Facts = facts ?? Array.Empty<WorkspaceFact>();
        Sources = sources ?? Array.Empty<WorkspaceFactSource>();
        DiagnosticsRefs = diagnosticsRefs ?? Array.Empty<string>();
        Issues = issues ?? Array.Empty<string>();
    }

    public WorkspaceResolutionStatus Status { get; }

    public IReadOnlyList<WorkspaceFact> Facts { get; }

    public IReadOnlyList<WorkspaceFactSource> Sources { get; }

    public IReadOnlyList<string> DiagnosticsRefs { get; }

    public IReadOnlyList<string> Issues { get; }

    public IReadOnlyList<ContextSourceCandidate> ToContextSourceCandidates()
        => Facts.Select(static fact => fact.ToContextSourceCandidate()).ToArray();
}

/// <summary>
/// Workspace / Environment Module 统一入口，供 Execution Runtime 通过 ModuleCapabilityStep 只读调用。
/// Unified Workspace / Environment Module entry point invoked read-only by Execution Runtime through ModuleCapabilityStep.
/// </summary>
public interface IWorkspaceModule : IModuleHealthCheck
{
    ValueTask<WorkspaceResolutionResult> ResolveAsync(
        WorkspaceResolutionRequest request,
        WorkspaceModuleInvocationContext context,
        CancellationToken cancellationToken);
}
