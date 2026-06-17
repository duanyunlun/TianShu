using System.Text.Json.Serialization;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Remote;

/// <summary>
/// 远程命令幂等键，用于防止断线重试重复执行高风险操作。
/// Remote command idempotency key used to prevent repeated execution during reconnect retries.
/// </summary>
[JsonConverter(typeof(TianShuStringIdentifierJsonConverter<RemoteCommandIdempotencyKey>))]
public readonly record struct RemoteCommandIdempotencyKey
{
    public RemoteCommandIdempotencyKey(string value)
    {
        Value = IdentifierGuard.AgainstNullOrWhiteSpace(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;

    public static implicit operator string(RemoteCommandIdempotencyKey value) => value.Value;
}

/// <summary>
/// 远程命令类型。
/// Remote command kind.
/// </summary>
public enum RemoteCommandKind
{
    Unspecified = 0,
    SubmitMessage = 1,
    Steer = 2,
    Interrupt = 3,
    Resume = 4,
    ApprovalDecision = 5,
    CancelPendingOperation = 6,
}

/// <summary>
/// 远程审批决策。
/// Remote approval decision.
/// </summary>
public enum RemoteApprovalDecisionKind
{
    Unspecified = 0,
    Approve = 1,
    Deny = 2,
    Defer = 3,
}

/// <summary>
/// 远程命令受理结果。
/// Remote command admission result.
/// </summary>
public enum RemoteCommandAdmissionStatus
{
    Unknown = 0,
    Accepted = 1,
    Rejected = 2,
    DuplicateIgnored = 3,
    ScopeDenied = 4,
    Expired = 5,
    Invalid = 6,
}

/// <summary>
/// 远程命令 scope，声明一个设备当前可提交的命令集合和副作用上限。
/// Remote command scope declaring which commands a device may submit and the maximum side-effect level.
/// </summary>
public sealed record RemoteCommandScope
{
    public RemoteCommandScope(
        IReadOnlyList<RemoteCommandKind>? allowedCommands = null,
        SideEffectLevel maxSideEffectLevel = SideEffectLevel.ReadOnly,
        IReadOnlyList<string>? threadRefs = null,
        IReadOnlyList<string>? scopeRefs = null)
    {
        if (maxSideEffectLevel == SideEffectLevel.Unspecified)
        {
            throw new ArgumentException("远程命令 scope 必须声明明确的副作用上限。", nameof(maxSideEffectLevel));
        }

        AllowedCommands = allowedCommands ?? Array.Empty<RemoteCommandKind>();
        MaxSideEffectLevel = maxSideEffectLevel;
        ThreadRefs = threadRefs ?? Array.Empty<string>();
        ScopeRefs = scopeRefs ?? Array.Empty<string>();
    }

    public IReadOnlyList<RemoteCommandKind> AllowedCommands { get; }

    public SideEffectLevel MaxSideEffectLevel { get; }

    public IReadOnlyList<string> ThreadRefs { get; }

    public IReadOnlyList<string> ScopeRefs { get; }

    public bool Allows(RemoteCommandKind kind)
        => kind != RemoteCommandKind.Unspecified && AllowedCommands.Contains(kind);

    /// <summary>
    /// 判断当前 scope 是否允许某类远程命令所需的最低副作用等级。
    /// Determines whether the scope allows the minimum side-effect level required by a remote command kind.
    /// </summary>
    public bool AllowsSideEffectFor(RemoteCommandKind kind)
        => Allows(kind) && MaxSideEffectLevel >= GetRequiredSideEffectLevel(kind);

    /// <summary>
    /// 获取远程命令的最低副作用等级；只读提交消息仍由后续 Control Plane 再治理。
    /// Gets the minimum side-effect level for a remote command; read-only submit message is still governed later by Control Plane.
    /// </summary>
    public static SideEffectLevel GetRequiredSideEffectLevel(RemoteCommandKind kind)
        => kind switch
        {
            RemoteCommandKind.SubmitMessage => SideEffectLevel.ReadOnly,
            RemoteCommandKind.Steer => SideEffectLevel.HostMutation,
            RemoteCommandKind.Interrupt => SideEffectLevel.HostMutation,
            RemoteCommandKind.Resume => SideEffectLevel.HostMutation,
            RemoteCommandKind.ApprovalDecision => SideEffectLevel.HostMutation,
            RemoteCommandKind.CancelPendingOperation => SideEffectLevel.HostMutation,
            _ => SideEffectLevel.Privileged,
        };
}

/// <summary>
/// 远程命令审计上下文。
/// Remote command audit context.
/// </summary>
public sealed record RemoteAuditContext
{
    public RemoteAuditContext(
        string pairingRef,
        string actorRef,
        string? networkRef = null,
        IReadOnlyList<string>? auditRefs = null)
    {
        PairingRef = IdentifierGuard.AgainstNullOrWhiteSpace(pairingRef, nameof(pairingRef));
        ActorRef = IdentifierGuard.AgainstNullOrWhiteSpace(actorRef, nameof(actorRef));
        NetworkRef = NormalizeOptional(networkRef);
        AuditRefs = auditRefs ?? Array.Empty<string>();
    }

    public string PairingRef { get; }

    public string ActorRef { get; }

    public string? NetworkRef { get; }

    public IReadOnlyList<string> AuditRefs { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// 远程命令 payload 标记接口。
/// Marker interface for typed remote command payloads.
/// </summary>
public interface IRemoteCommandPayload
{
    RemoteCommandKind Kind { get; }
}

/// <summary>
/// 远程命令 envelope；它是 Host Gateway / Control Plane 之前的受治理输入，不是 RuntimeStep。
/// Remote command envelope; governed input before Host Gateway / Control Plane, not a RuntimeStep.
/// </summary>
public sealed record RemoteCommandEnvelope<TPayload>
    where TPayload : IRemoteCommandPayload
{
    public RemoteCommandEnvelope(
        string commandId,
        ThreadId threadId,
        DeviceId deviceId,
        SessionId? sessionId,
        TPayload payload,
        RemoteCommandScope scope,
        RemoteCommandIdempotencyKey idempotencyKey,
        RemoteAuditContext audit)
    {
        CommandId = IdentifierGuard.AgainstNullOrWhiteSpace(commandId, nameof(commandId));
        ThreadId = threadId;
        DeviceId = deviceId;
        SessionId = sessionId;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Kind = payload.Kind == RemoteCommandKind.Unspecified
            ? throw new ArgumentException("远程命令 payload 必须声明明确 kind。", nameof(payload))
            : payload.Kind;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (!Scope.Allows(Kind))
        {
            throw new ArgumentException("远程命令 kind 不在当前 scope allow-list 内。", nameof(scope));
        }

        IdempotencyKey = idempotencyKey;
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public string CommandId { get; }

    public ThreadId ThreadId { get; }

    public DeviceId DeviceId { get; }

    public SessionId? SessionId { get; }

    public RemoteCommandKind Kind { get; }

    public TPayload Payload { get; }

    public RemoteCommandScope Scope { get; }

    public RemoteCommandIdempotencyKey IdempotencyKey { get; }

    public RemoteAuditContext Audit { get; }
}

/// <summary>
/// 提交用户消息的远程命令 payload。
/// Remote command payload for submitting a user message.
/// </summary>
public sealed record RemoteSubmitMessagePayload(
    string MessageText,
    IReadOnlyList<string>? AttachmentRefs = null) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.SubmitMessage;

    public string MessageText { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(MessageText, nameof(MessageText));

    public IReadOnlyList<string> AttachmentRefs { get; } = AttachmentRefs ?? Array.Empty<string>();
}

/// <summary>
/// 远程 steer payload，用于给当前或下一轮 run 提交 steer 输入。
/// Remote steer payload used to submit steer input to the current or next run.
/// </summary>
public sealed record RemoteSteerPayload(
    string Instruction,
    string? TargetRunRef = null) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.Steer;

    public string Instruction { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Instruction, nameof(Instruction));

    public string? TargetRunRef { get; } = string.IsNullOrWhiteSpace(TargetRunRef) ? null : TargetRunRef.Trim();
}

/// <summary>
/// 远程中断 payload。
/// Remote interrupt payload.
/// </summary>
public sealed record RemoteInterruptPayload(
    string Reason,
    string? ActiveRunRef = null) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.Interrupt;

    public string Reason { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(Reason, nameof(Reason));

    public string? ActiveRunRef { get; } = string.IsNullOrWhiteSpace(ActiveRunRef) ? null : ActiveRunRef.Trim();
}

/// <summary>
/// 远程恢复 payload。
/// Remote resume payload.
/// </summary>
public sealed record RemoteResumePayload(
    string CheckpointRef,
    bool ConsumePendingSteer = true) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.Resume;

    public string CheckpointRef { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(CheckpointRef, nameof(CheckpointRef));
}

/// <summary>
/// 远程审批决策 payload。
/// Remote approval decision payload.
/// </summary>
public sealed record RemoteApprovalDecisionPayload(
    ApprovalId ApprovalId,
    RemoteApprovalDecisionKind Decision,
    string? Reason = null) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.ApprovalDecision;

    public RemoteApprovalDecisionKind Decision { get; } = Decision == RemoteApprovalDecisionKind.Unspecified
        ? throw new ArgumentException("远程审批决策必须明确。", nameof(Decision))
        : Decision;

    public string? Reason { get; } = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
}

/// <summary>
/// 取消 pending operation 的远程命令 payload。
/// Remote command payload for cancelling a pending operation.
/// </summary>
public sealed record RemoteCancelPendingOperationPayload(
    string PendingOperationRef,
    string? Reason = null) : IRemoteCommandPayload
{
    public RemoteCommandKind Kind => RemoteCommandKind.CancelPendingOperation;

    public string PendingOperationRef { get; } = IdentifierGuard.AgainstNullOrWhiteSpace(PendingOperationRef, nameof(PendingOperationRef));

    public string? Reason { get; } = string.IsNullOrWhiteSpace(Reason) ? null : Reason.Trim();
}

/// <summary>
/// 远程命令结果；P28.5 以后由 Host Gateway / Control Plane admission 生成。
/// Remote command result produced by Host Gateway / Control Plane admission after P28.5.
/// </summary>
public sealed record RemoteCommandResult
{
    public RemoteCommandResult(
        string commandId,
        RemoteCommandKind kind,
        RemoteCommandAdmissionStatus status,
        RemoteCommandIdempotencyKey idempotencyKey,
        string? acceptedOperationRef = null,
        string? failureCode = null,
        string? diagnosticsRef = null)
    {
        CommandId = IdentifierGuard.AgainstNullOrWhiteSpace(commandId, nameof(commandId));
        Kind = kind == RemoteCommandKind.Unspecified
            ? throw new ArgumentException("远程命令结果必须声明明确 kind。", nameof(kind))
            : kind;
        Status = status == RemoteCommandAdmissionStatus.Unknown
            ? throw new ArgumentException("远程命令结果必须声明明确 status。", nameof(status))
            : status;
        IdempotencyKey = idempotencyKey;
        AcceptedOperationRef = NormalizeOptional(acceptedOperationRef);
        FailureCode = NormalizeOptional(failureCode);
        DiagnosticsRef = NormalizeOptional(diagnosticsRef);
    }

    public string CommandId { get; }

    public RemoteCommandKind Kind { get; }

    public RemoteCommandAdmissionStatus Status { get; }

    public RemoteCommandIdempotencyKey IdempotencyKey { get; }

    public string? AcceptedOperationRef { get; }

    public string? FailureCode { get; }

    public string? DiagnosticsRef { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
