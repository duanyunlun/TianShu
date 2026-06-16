using TianShu.Contracts.Interactions;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;

namespace TianShu.Contracts.Host;

/// <summary>
/// 宿主表面种类，表示交互来自哪类消费层。
/// Host surface kind describing which consumer surface produced the interaction.
/// </summary>
public enum HostSurfaceKind
{
    Cli = 0,
    Vsix = 1,
    Web = 2,
    Service = 3,
    Embedded = 4,
}

/// <summary>
/// 宿主附件种类。
/// Host attachment kind.
/// </summary>
public enum HostAttachmentKind
{
    File = 0,
    Image = 1,
    Audio = 2,
    Selection = 3,
    Artifact = 4,
}

/// <summary>
/// 宿主选区上下文。
/// Host selection context.
/// </summary>
public sealed record HostSelectionContext
{
    /// <summary>
    /// 初始化宿主选区上下文。
    /// Initializes a host selection context.
    /// </summary>
    public HostSelectionContext(
        string path,
        int? startLine = null,
        int? endLine = null,
        string? selectedText = null)
    {
        if (startLine is < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "起始行号必须从 1 开始。");
        }

        if (endLine is not null && startLine is not null && endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), "结束行号不能小于起始行号。");
        }

        Path = IdentifierGuard.AgainstNullOrWhiteSpace(path, nameof(path));
        StartLine = startLine;
        EndLine = endLine;
        SelectedText = selectedText;
    }

    public string Path { get; }

    public int? StartLine { get; }

    public int? EndLine { get; }

    public string? SelectedText { get; }
}

/// <summary>
/// 宿主附件。
/// Host attachment.
/// </summary>
public sealed record HostAttachment
{
    /// <summary>
    /// 初始化宿主附件。
    /// Initializes a host attachment.
    /// </summary>
    public HostAttachment(
        HostAttachmentKind kind,
        string name,
        string location,
        string? contentType = null)
    {
        Kind = kind;
        Name = IdentifierGuard.AgainstNullOrWhiteSpace(name, nameof(name));
        Location = IdentifierGuard.AgainstNullOrWhiteSpace(location, nameof(location));
        ContentType = contentType;
    }

    public HostAttachmentKind Kind { get; }

    public string Name { get; }

    public string Location { get; }

    public string? ContentType { get; }
}

/// <summary>
/// 宿主上下文，表达输入发生时的宿主表面和工作区环境。
/// Host context describing the surface and workspace environment where the input originated.
/// </summary>
public sealed record HostContext
{
    /// <summary>
    /// 初始化宿主上下文。
    /// Initializes a host context.
    /// </summary>
    public HostContext(
        HostSurfaceKind surfaceKind,
        string surfaceId,
        string? workingDirectory = null,
        HostSelectionContext? selection = null,
        IReadOnlyList<HostAttachment>? attachments = null,
        MetadataBag? metadata = null)
    {
        SurfaceKind = surfaceKind;
        SurfaceId = IdentifierGuard.AgainstNullOrWhiteSpace(surfaceId, nameof(surfaceId));
        WorkingDirectory = workingDirectory;
        Selection = selection;
        Attachments = attachments ?? Array.Empty<HostAttachment>();
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public HostSurfaceKind SurfaceKind { get; }

    public string SurfaceId { get; }

    public string? WorkingDirectory { get; }

    public HostSelectionContext? Selection { get; }

    public IReadOnlyList<HostAttachment> Attachments { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 宿主交互包络，表达消费层输入在进入控制平面前的 typed 归一化结果。
/// Host interaction envelope representing the typed normalization result before entering the control plane.
/// </summary>
public sealed record HostInteractionEnvelope
{
    /// <summary>
    /// 初始化宿主交互包络。
    /// Initializes a host interaction envelope.
    /// </summary>
    public HostInteractionEnvelope(
        InteractionEnvelopeId interactionId,
        HostContext context,
        IReadOnlyList<InteractionItem> items,
        InteractionTarget? target = null,
        InteractionRoutingHint? routingHint = null,
        ParticipantId? initiatedByParticipantId = null,
        DateTimeOffset? createdAt = null)
    {
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("宿主交互包至少需要一个输入项。", nameof(items));
        }

        InteractionId = interactionId;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Items = items;
        Target = target;
        RoutingHint = routingHint;
        InitiatedByParticipantId = initiatedByParticipantId;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public InteractionEnvelopeId InteractionId { get; }

    public HostContext Context { get; }

    public IReadOnlyList<InteractionItem> Items { get; }

    public InteractionTarget? Target { get; }

    public InteractionRoutingHint? RoutingHint { get; }

    public ParticipantId? InitiatedByParticipantId { get; }

    public DateTimeOffset CreatedAt { get; }
}

/// <summary>
/// 宿主操作种类，由 Host Gateway 交给 Control Plane 归一化分类。
/// Host operation kind normalized by Host Gateway before Control Plane classification.
/// </summary>
public enum HostOperationKind
{
    Unspecified = 0,
    Query = 1,
    Control = 2,
    State = 3,
    Governance = 4,
    CoreIntent = 5,
}

/// <summary>
/// 宿主操作结果状态；默认 Unspecified 不表示成功。
/// Host operation result status; default Unspecified does not mean success.
/// </summary>
public enum HostOperationStatus
{
    Unspecified = 0,
    Accepted = 1,
    Rejected = 2,
    Completed = 3,
    Failed = 4,
}

/// <summary>
/// 宿主诊断引用。
/// Host diagnostic reference.
/// </summary>
public sealed record HostDiagnosticRef
{
    public HostDiagnosticRef(string diagnosticId, string? severity = null, string? message = null)
    {
        DiagnosticId = IdentifierGuard.AgainstNullOrWhiteSpace(diagnosticId, nameof(diagnosticId));
        Severity = severity;
        Message = message;
    }

    public string DiagnosticId { get; }

    public string? Severity { get; }

    public string? Message { get; }
}

/// <summary>
/// 宿主操作请求，是 Experience 进入 Host Gateway 的统一 typed 入口。
/// Host operation request, the unified typed entry from Experience into Host Gateway.
/// </summary>
public sealed record HostOperationRequest
{
    public HostOperationRequest(
        string operationId,
        string hostId,
        HostOperationKind operationKind,
        StructuredValue payload,
        HostContext? context = null,
        MetadataBag? metadata = null)
    {
        OperationId = IdentifierGuard.AgainstNullOrWhiteSpace(operationId, nameof(operationId));
        HostId = IdentifierGuard.AgainstNullOrWhiteSpace(hostId, nameof(hostId));
        OperationKind = operationKind;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Context = context;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string OperationId { get; }

    public string HostId { get; }

    public HostOperationKind OperationKind { get; }

    public StructuredValue Payload { get; }

    public HostContext? Context { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 宿主操作结果，是 Host Gateway 返回给 Experience 的统一 typed 包络。
/// Host operation result, the unified typed envelope returned from Host Gateway to Experience.
/// </summary>
public sealed record HostOperationResult
{
    public HostOperationResult(
        string operationId,
        HostOperationStatus status,
        StructuredValue? projection = null,
        IReadOnlyList<HostDiagnosticRef>? diagnostics = null,
        string? message = null)
    {
        OperationId = IdentifierGuard.AgainstNullOrWhiteSpace(operationId, nameof(operationId));
        Status = status;
        Projection = projection;
        Diagnostics = diagnostics ?? Array.Empty<HostDiagnosticRef>();
        Message = message;
    }

    public string OperationId { get; }

    public HostOperationStatus Status { get; }

    public StructuredValue? Projection { get; }

    public IReadOnlyList<HostDiagnosticRef> Diagnostics { get; }

    public string? Message { get; }
}

/// <summary>
/// 宿主能力协商结果，表达消费层 UI / 交互能力上限。
/// Host capability negotiation result describing the UI and interaction capability ceiling of the consumer surface.
/// </summary>
public sealed record HostCapabilityNegotiation(
    bool SupportsStreamingText = true,
    bool SupportsInterrupt = true,
    bool SupportsInputQueue = true,
    bool SupportsPlanSelector = false,
    bool SupportsAgentRoster = false,
    bool SupportsRichArtifacts = false,
    bool SupportsApprovalCards = true);

/// <summary>
/// 宿主通知种类。
/// Host notification kind.
/// </summary>
public enum HostNotificationKind
{
    Info = 0,
    Warning = 1,
    Error = 2,
    ApprovalRequested = 3,
    UserInputRequested = 4,
    PermissionRequested = 5,
    TurnCompleted = 6,
}

/// <summary>
/// 宿主权限授予范围。
/// Host permission-grant scope.
/// </summary>
public enum HostPermissionScope
{
    Turn = 0,
    Session = 1,
}

/// <summary>
/// 宿主通知，面向消费层表达非持久化的临时提示。
/// Host notification used to deliver transient non-persistent hints to consumer surfaces.
/// </summary>
public sealed record HostNotification
{
    /// <summary>
    /// 初始化宿主通知。
    /// Initializes a host notification.
    /// </summary>
    public HostNotification(
        string code,
        HostNotificationKind kind,
        string title,
        string? message = null,
        CallId? relatedCallId = null,
        string? payloadKind = null,
        StructuredValue? payload = null)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Kind = kind;
        Title = IdentifierGuard.AgainstNullOrWhiteSpace(title, nameof(title));
        Message = message;
        RelatedCallId = relatedCallId;
        PayloadKind = payloadKind;
        Payload = payload;
    }

    public string Code { get; }

    public HostNotificationKind Kind { get; }

    public string Title { get; }

    public string? Message { get; }

    public CallId? RelatedCallId { get; }

    /// <summary>
    /// 宿主通知附带的结构化载荷种类。
    /// Structured payload kind carried by the notification.
    /// </summary>
    public string? PayloadKind { get; }

    /// <summary>
    /// 宿主通知附带的结构化载荷。
    /// Structured payload carried by the notification.
    /// </summary>
    public StructuredValue? Payload { get; }
}

/// <summary>
/// 宿主视图更新，向消费层推送只读投影视图的增量或重置事件。
/// Host view update that pushes read-model deltas or reset events to consumer surfaces.
/// </summary>
public sealed record HostViewUpdate
{
    /// <summary>
    /// 初始化宿主视图更新。
    /// Initializes a host view update.
    /// </summary>
    public HostViewUpdate(ProjectionDelta? delta = null, ProjectionReset? reset = null, KernelProjection? kernelProjection = null)
    {
        var payloadCount = (delta is null ? 0 : 1)
                           + (reset is null ? 0 : 1)
                           + (kernelProjection is null ? 0 : 1);
        if (payloadCount == 0)
        {
            throw new ArgumentException("宿主视图更新必须包含增量、重置或 Kernel 投影。");
        }

        if (payloadCount > 1)
        {
            throw new ArgumentException("宿主视图更新不能同时包含多种载荷。");
        }

        Delta = delta;
        Reset = reset;
        KernelProjection = kernelProjection;
    }

    public ProjectionDelta? Delta { get; }

    public ProjectionReset? Reset { get; }

    public KernelProjection? KernelProjection { get; }
}

/// <summary>
/// 宿主快照请求。
/// Host snapshot request.
/// </summary>
public sealed record HostSnapshotRequest
{
    public HostSnapshotRequest(string hostId, ProjectionScopeKind scopeKind, string scopeId, MetadataBag? metadata = null)
    {
        HostId = IdentifierGuard.AgainstNullOrWhiteSpace(hostId, nameof(hostId));
        ScopeKind = scopeKind;
        ScopeId = IdentifierGuard.AgainstNullOrWhiteSpace(scopeId, nameof(scopeId));
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string HostId { get; }

    public ProjectionScopeKind ScopeKind { get; }

    public string ScopeId { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// 宿主快照，只承载 Host Gateway 可公开的只读投影。
/// Host snapshot carrying only read-only projections that Host Gateway may expose.
/// </summary>
public sealed record HostSnapshot
{
    public HostSnapshot(
        string snapshotId,
        ProjectionScopeKind scopeKind,
        string scopeId,
        IReadOnlyList<ProjectionPayload>? projections = null,
        IReadOnlyList<KernelProjection>? kernelProjections = null,
        DateTimeOffset generatedAt = default)
    {
        SnapshotId = IdentifierGuard.AgainstNullOrWhiteSpace(snapshotId, nameof(snapshotId));
        ScopeKind = scopeKind;
        ScopeId = IdentifierGuard.AgainstNullOrWhiteSpace(scopeId, nameof(scopeId));
        Projections = projections ?? Array.Empty<ProjectionPayload>();
        KernelProjections = kernelProjections ?? Array.Empty<KernelProjection>();
        GeneratedAt = generatedAt == default ? DateTimeOffset.UtcNow : generatedAt;
    }

    public string SnapshotId { get; }

    public ProjectionScopeKind ScopeKind { get; }

    public string ScopeId { get; }

    public IReadOnlyList<ProjectionPayload> Projections { get; }

    public IReadOnlyList<KernelProjection> KernelProjections { get; }

    public DateTimeOffset GeneratedAt { get; }
}
