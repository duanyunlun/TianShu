using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.ControlPlane.Abstractions.Operations;

/// <summary>
/// Control Plane 归一化后的操作类别。
/// Normalized operation category owned by Control Plane.
/// </summary>
public enum ControlOperationKind
{
    Unspecified = 0,
    Query = 1,
    Control = 2,
    State = 3,
    Governance = 4,
    CoreIntent = 5,
}

/// <summary>
/// Control Plane 操作处理状态。
/// Processing status for a control operation.
/// </summary>
public enum ControlOperationStatus
{
    Unspecified = 0,
    Accepted = 1,
    Completed = 2,
    Rejected = 3,
}

/// <summary>
/// Control Plane 操作关联的会话、线程和工作流主体。
/// Session, thread, and workflow subject associated with a control operation.
/// </summary>
public sealed record ControlOperationSubject
{
    public ControlOperationSubject(SessionId sessionId, ThreadId threadId, WorkflowId? workflowId = null, TurnId? turnId = null)
    {
        SessionId = sessionId;
        ThreadId = threadId;
        WorkflowId = workflowId;
        TurnId = turnId;
    }

    public SessionId SessionId { get; }

    public ThreadId ThreadId { get; }

    public WorkflowId? WorkflowId { get; }

    public TurnId? TurnId { get; }

    public KernelSubjectRef ToKernelSubjectRef() => new(SessionId, ThreadId, WorkflowId, TurnId);
}

/// <summary>
/// Control Plane 创建治理信封所需的最小请求。
/// Minimum request used by Control Plane to create a governance envelope.
/// </summary>
public sealed record ControlOperationGovernanceRequest
{
    public ControlOperationGovernanceRequest(
        string envelopeId,
        IReadOnlyList<string>? policyIds = null,
        IReadOnlyList<string>? allowedToolIds = null,
        IReadOnlyList<string>? allowedModuleIds = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.Unspecified,
        bool requiresHumanGate = true)
    {
        EnvelopeId = IdentifierGuard.AgainstNullOrWhiteSpace(envelopeId, nameof(envelopeId));
        PolicyIds = NormalizeList(policyIds);
        AllowedToolIds = NormalizeList(allowedToolIds);
        AllowedModuleIds = NormalizeList(allowedModuleIds);
        MaxSideEffectLevel = maxSideEffectLevel;
        RequiresHumanGate = requiresHumanGate;
    }

    public string EnvelopeId { get; }

    public IReadOnlyList<string> PolicyIds { get; }

    public IReadOnlyList<string> AllowedToolIds { get; }

    public IReadOnlyList<string> AllowedModuleIds { get; }

    public SideEffectLevel MaxSideEffectLevel { get; }

    public bool RequiresHumanGate { get; }

    public GovernanceEnvelope ToGovernanceEnvelope()
        => new(
            EnvelopeId,
            PolicyIds,
            AllowedToolIds,
            AllowedModuleIds,
            MaxSideEffectLevel,
            RequiresHumanGate);

    private static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
        => values is null
            ? Array.Empty<string>()
            : values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

/// <summary>
/// Host Gateway 进入 Control Plane 的统一操作请求。
/// Unified operation request entering Control Plane from Host Gateway.
/// </summary>
public sealed record ControlOperationRequest
{
    public ControlOperationRequest(
        string operationId,
        string operationName,
        ControlOperationSubject? subject = null,
        ControlOperationGovernanceRequest? governance = null,
        StructuredValue? payload = null,
        MetadataBag? metadata = null)
    {
        OperationId = IdentifierGuard.AgainstNullOrWhiteSpace(operationId, nameof(operationId));
        OperationName = IdentifierGuard.AgainstNullOrWhiteSpace(operationName, nameof(operationName));
        Subject = subject;
        Governance = governance;
        Payload = payload ?? StructuredValue.Null;
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string OperationId { get; }

    public string OperationName { get; }

    public ControlOperationSubject? Subject { get; }

    public ControlOperationGovernanceRequest? Governance { get; }

    public StructuredValue Payload { get; }

    public MetadataBag Metadata { get; }
}

/// <summary>
/// Control Plane 操作拒绝或降级时返回的可审计问题。
/// Auditable issue returned when Control Plane rejects or downgrades an operation.
/// </summary>
public sealed record ControlOperationIssue
{
    public ControlOperationIssue(string code, string message)
    {
        Code = IdentifierGuard.AgainstNullOrWhiteSpace(code, nameof(code));
        Message = IdentifierGuard.AgainstNullOrWhiteSpace(message, nameof(message));
    }

    public string Code { get; }

    public string Message { get; }
}

/// <summary>
/// Control Plane 归一化后的统一结果。
/// Unified result after Control Plane normalization.
/// </summary>
public sealed record ControlOperationResult
{
    public ControlOperationResult(
        string operationId,
        ControlOperationKind operationKind,
        ControlOperationStatus status,
        StructuredValue? typedResult = null,
        CoreIntent? coreIntent = null,
        GovernanceEnvelope? governanceEnvelope = null,
        IReadOnlyList<ControlOperationIssue>? issues = null,
        MetadataBag? metadata = null)
    {
        OperationId = IdentifierGuard.AgainstNullOrWhiteSpace(operationId, nameof(operationId));
        OperationKind = operationKind;
        Status = status;
        TypedResult = typedResult ?? StructuredValue.Null;
        CoreIntent = coreIntent;
        GovernanceEnvelope = governanceEnvelope;
        Issues = issues ?? Array.Empty<ControlOperationIssue>();
        Metadata = metadata ?? MetadataBag.Empty;
    }

    public string OperationId { get; }

    public ControlOperationKind OperationKind { get; }

    public ControlOperationStatus Status { get; }

    public StructuredValue TypedResult { get; }

    public CoreIntent? CoreIntent { get; }

    public GovernanceEnvelope? GovernanceEnvelope { get; }

    public IReadOnlyList<ControlOperationIssue> Issues { get; }

    public MetadataBag Metadata { get; }

    public static ControlOperationResult Completed(
        ControlOperationRequest request,
        ControlOperationKind kind,
        StructuredValue typedResult,
        GovernanceEnvelope? governanceEnvelope = null)
        => new(request.OperationId, kind, ControlOperationStatus.Completed, typedResult, governanceEnvelope: governanceEnvelope, metadata: request.Metadata);

    public static ControlOperationResult Accepted(ControlOperationRequest request, ControlOperationKind kind, GovernanceEnvelope? governanceEnvelope = null)
        => new(request.OperationId, kind, ControlOperationStatus.Accepted, governanceEnvelope: governanceEnvelope, metadata: request.Metadata);

    public static ControlOperationResult CoreIntentGenerated(ControlOperationRequest request, CoreIntent intent)
        => new(
            request.OperationId,
            ControlOperationKind.CoreIntent,
            ControlOperationStatus.Accepted,
            coreIntent: intent,
            governanceEnvelope: intent.Governance,
            metadata: request.Metadata);

    public static ControlOperationResult Rejected(ControlOperationRequest request, ControlOperationKind kind, string code, string message)
        => new(
            request.OperationId,
            kind,
            ControlOperationStatus.Rejected,
            issues: [new ControlOperationIssue(code, message)],
            metadata: request.Metadata);
}

/// <summary>
/// Control Plane 的统一 operation 入口。
/// Unified operation entry point for Control Plane.
/// </summary>
public interface IControlPlane
{
    Task<ControlOperationResult> ProcessAsync(ControlOperationRequest request, CancellationToken cancellationToken);
}
