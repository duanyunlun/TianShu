using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Remote;

/// <summary>
/// Remote Module 可使用的传输种类；默认 Unspecified 必须拒绝。
/// Transport kinds available to Remote Modules; the default Unspecified value must be rejected.
/// </summary>
public enum RemoteModuleTransportKind
{
    Unspecified = 0,
    LocalHttpPolling = 1,
    ServerSentEvents = 2,
    WebSocket = 3,
    NamedPipe = 4,
    StdioBridge = 5,
    CloudRelayAdapter = 6,
}

/// <summary>
/// Remote transport 的安全模式。
/// Security mode for a remote transport.
/// </summary>
public enum RemoteTransportSecurityMode
{
    Unspecified = 0,
    LocalOnly = 1,
    TlsRequired = 2,
    RelayManaged = 3,
}

/// <summary>
/// 远端设备信任等级。
/// Remote device trust level.
/// </summary>
public enum RemoteDeviceTrustLevel
{
    Unspecified = 0,
    ReadOnlyFollower = 1,
    InteractiveOperator = 2,
    WorkspaceOperator = 3,
}

/// <summary>
/// 远程 pairing 状态。
/// Remote pairing status.
/// </summary>
public enum RemotePairingStatus
{
    Unspecified = 0,
    Pending = 1,
    Granted = 2,
    Rejected = 3,
    Revoked = 4,
    Expired = 5,
}

/// <summary>
/// 短期 token 可访问的远程连续性面。
/// Remote continuity surfaces addressable by a short-lived token.
/// </summary>
public enum RemoteTokenAudience
{
    Unspecified = 0,
    Snapshot = 1,
    EventStream = 2,
    Command = 3,
    PairingRefresh = 4,
}

/// <summary>
/// 远程会话撤销原因。
/// Remote session revocation reason.
/// </summary>
public enum RemoteSessionRevocationReason
{
    Unspecified = 0,
    UserRevoked = 1,
    TokenExpired = 2,
    DeviceMismatch = 3,
    ScopeReduced = 4,
    PairingRevoked = 5,
    SecurityPolicy = 6,
}

/// <summary>
/// Remote Module transport 描述；它只描述可选传输，不意味着默认开启监听。
/// Remote Module transport descriptor; it describes optional transport and does not imply listening by default.
/// </summary>
public sealed record RemoteTransportDescriptor
{
    public RemoteTransportDescriptor(
        RemoteModuleTransportKind kind,
        string endpointRef,
        RemoteTransportSecurityMode securityMode = RemoteTransportSecurityMode.LocalOnly,
        string? bindAddress = null,
        bool allowsPublicNetwork = false,
        bool requiresPairing = true,
        IReadOnlyList<string>? featureRefs = null)
    {
        Kind = kind == RemoteModuleTransportKind.Unspecified
            ? throw new ArgumentException("Remote transport 必须声明明确 kind。", nameof(kind))
            : kind;
        EndpointRef = IdentifierGuard.AgainstNullOrWhiteSpace(endpointRef, nameof(endpointRef));
        SecurityMode = securityMode == RemoteTransportSecurityMode.Unspecified
            ? throw new ArgumentException("Remote transport 必须声明明确 security mode。", nameof(securityMode))
            : securityMode;
        BindAddress = NormalizeOptional(bindAddress);
        AllowsPublicNetwork = allowsPublicNetwork;
        RequiresPairing = requiresPairing;
        FeatureRefs = NormalizeList(featureRefs);

        if (!RequiresPairing)
        {
            throw new ArgumentException("Remote transport 必须要求显式 pairing。", nameof(requiresPairing));
        }

        if (IsPublicBind(BindAddress) && !AllowsPublicNetwork)
        {
            throw new ArgumentException("Remote transport 默认不得绑定公网地址。", nameof(bindAddress));
        }
    }

    public RemoteModuleTransportKind Kind { get; }

    public string EndpointRef { get; }

    public RemoteTransportSecurityMode SecurityMode { get; }

    public string? BindAddress { get; }

    public bool AllowsPublicNetwork { get; }

    public bool RequiresPairing { get; }

    public IReadOnlyList<string> FeatureRefs { get; }

    private static bool IsPublicBind(string? bindAddress)
        => string.Equals(bindAddress, "0.0.0.0", StringComparison.Ordinal)
            || string.Equals(bindAddress, "::", StringComparison.Ordinal)
            || string.Equals(bindAddress, "[::]", StringComparison.Ordinal);

    internal static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeList(IReadOnlyList<string>? values)
        => values is null
            ? Array.Empty<string>()
            : values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

/// <summary>
/// 远端 pairing grant；它绑定设备身份、信任等级、命令 scope 与撤销引用。
/// Remote pairing grant binding device identity, trust level, command scope, and revocation reference.
/// </summary>
public sealed record RemotePairingGrant
{
    public RemotePairingGrant(
        string pairingId,
        DeviceId deviceId,
        string deviceDisplayName,
        RemoteDeviceTrustLevel trustLevel,
        RemoteCommandScope scope,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string revocationRef,
        IReadOnlyList<RemoteModuleTransportKind>? allowedTransports = null)
    {
        PairingId = IdentifierGuard.AgainstNullOrWhiteSpace(pairingId, nameof(pairingId));
        DeviceId = deviceId;
        DeviceDisplayName = IdentifierGuard.AgainstNullOrWhiteSpace(deviceDisplayName, nameof(deviceDisplayName));
        TrustLevel = trustLevel == RemoteDeviceTrustLevel.Unspecified
            ? throw new ArgumentException("Remote pairing 必须声明明确 trust level。", nameof(trustLevel))
            : trustLevel;
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        IssuedAt = issuedAt;
        ExpiresAt = expiresAt <= issuedAt
            ? throw new ArgumentException("Remote pairing 过期时间必须晚于签发时间。", nameof(expiresAt))
            : expiresAt;
        RevocationRef = IdentifierGuard.AgainstNullOrWhiteSpace(revocationRef, nameof(revocationRef));
        AllowedTransports = NormalizeTransports(allowedTransports);
        Status = RemotePairingStatus.Granted;
    }

    public string PairingId { get; }

    public DeviceId DeviceId { get; }

    public string DeviceDisplayName { get; }

    public RemoteDeviceTrustLevel TrustLevel { get; }

    public RemoteCommandScope Scope { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string RevocationRef { get; }

    public IReadOnlyList<RemoteModuleTransportKind> AllowedTransports { get; }

    public RemotePairingStatus Status { get; }

    private static IReadOnlyList<RemoteModuleTransportKind> NormalizeTransports(IReadOnlyList<RemoteModuleTransportKind>? transports)
    {
        var normalized = transports is null
            ? Array.Empty<RemoteModuleTransportKind>()
            : transports
                .Where(static transport => transport != RemoteModuleTransportKind.Unspecified)
                .Distinct()
                .ToArray();
        return normalized;
    }
}

/// <summary>
/// 短期远程会话 token 描述；不保存、不返回 token 明文。
/// Short-lived remote session token descriptor; it never stores or returns raw token text.
/// </summary>
public sealed record RemoteSessionTokenDescriptor
{
    public static TimeSpan MaximumLifetime { get; } = TimeSpan.FromHours(24);

    public RemoteSessionTokenDescriptor(
        string tokenRef,
        string pairingId,
        DeviceId deviceId,
        IReadOnlyList<RemoteTokenAudience> audiences,
        RemoteCommandScope scope,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt,
        string revocationRef)
    {
        TokenRef = IdentifierGuard.AgainstNullOrWhiteSpace(tokenRef, nameof(tokenRef));
        PairingId = IdentifierGuard.AgainstNullOrWhiteSpace(pairingId, nameof(pairingId));
        DeviceId = deviceId;
        Audiences = NormalizeAudiences(audiences);
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        IssuedAt = issuedAt;
        ExpiresAt = ValidateExpiry(issuedAt, expiresAt);
        RevocationRef = IdentifierGuard.AgainstNullOrWhiteSpace(revocationRef, nameof(revocationRef));

        if (Audiences.Contains(RemoteTokenAudience.Command) && Scope.AllowedCommands.Count == 0)
        {
            throw new ArgumentException("Command token 必须携带非空命令 scope。", nameof(scope));
        }
    }

    public string TokenRef { get; }

    public string PairingId { get; }

    public DeviceId DeviceId { get; }

    public IReadOnlyList<RemoteTokenAudience> Audiences { get; }

    public RemoteCommandScope Scope { get; }

    public DateTimeOffset IssuedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public string RevocationRef { get; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    private static IReadOnlyList<RemoteTokenAudience> NormalizeAudiences(IReadOnlyList<RemoteTokenAudience>? audiences)
    {
        var normalized = audiences is null
            ? Array.Empty<RemoteTokenAudience>()
            : audiences
                .Where(static audience => audience != RemoteTokenAudience.Unspecified)
                .Distinct()
                .ToArray();

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Remote session token 必须声明至少一个 audience。", nameof(audiences));
        }

        return normalized;
    }

    private static DateTimeOffset ValidateExpiry(DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        if (expiresAt <= issuedAt)
        {
            throw new ArgumentException("Remote session token 过期时间必须晚于签发时间。", nameof(expiresAt));
        }

        if (expiresAt - issuedAt > MaximumLifetime)
        {
            throw new ArgumentException("Remote session token 必须是短期 token，最长有效期为 24 小时。", nameof(expiresAt));
        }

        return expiresAt;
    }
}

/// <summary>
/// 远程会话撤销记录。
/// Remote session revocation record.
/// </summary>
public sealed record RemoteSessionRevocation
{
    public RemoteSessionRevocation(
        string revocationId,
        string pairingId,
        DeviceId deviceId,
        string tokenRef,
        RemoteSessionRevocationReason reason,
        DateTimeOffset revokedAt,
        string auditRef)
    {
        RevocationId = IdentifierGuard.AgainstNullOrWhiteSpace(revocationId, nameof(revocationId));
        PairingId = IdentifierGuard.AgainstNullOrWhiteSpace(pairingId, nameof(pairingId));
        DeviceId = deviceId;
        TokenRef = IdentifierGuard.AgainstNullOrWhiteSpace(tokenRef, nameof(tokenRef));
        Reason = reason == RemoteSessionRevocationReason.Unspecified
            ? throw new ArgumentException("Remote session revocation 必须声明明确 reason。", nameof(reason))
            : reason;
        RevokedAt = revokedAt == default
            ? throw new ArgumentException("Remote session revocation 必须声明 revokedAt。", nameof(revokedAt))
            : revokedAt;
        AuditRef = IdentifierGuard.AgainstNullOrWhiteSpace(auditRef, nameof(auditRef));
    }

    public string RevocationId { get; }

    public string PairingId { get; }

    public DeviceId DeviceId { get; }

    public string TokenRef { get; }

    public RemoteSessionRevocationReason Reason { get; }

    public DateTimeOffset RevokedAt { get; }

    public string AuditRef { get; }
}

/// <summary>
/// Remote Module 激活上下文；用于校验 transport、pairing、token、device identity 与 scope 是否一致。
/// Remote Module activation context validating transport, pairing, token, device identity, and scope consistency.
/// </summary>
public sealed record RemoteModuleActivationContext
{
    public RemoteModuleActivationContext(
        string moduleId,
        DeviceId deviceId,
        RemoteTransportDescriptor transport,
        RemotePairingGrant pairing,
        RemoteSessionTokenDescriptor token,
        DateTimeOffset activatedAt)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        DeviceId = deviceId;
        Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        Pairing = pairing ?? throw new ArgumentNullException(nameof(pairing));
        Token = token ?? throw new ArgumentNullException(nameof(token));
        ActivatedAt = activatedAt == default
            ? throw new ArgumentException("Remote Module activation 必须声明 activatedAt。", nameof(activatedAt))
            : activatedAt;

        ValidatePairingAndToken();
    }

    public string ModuleId { get; }

    public DeviceId DeviceId { get; }

    public RemoteTransportDescriptor Transport { get; }

    public RemotePairingGrant Pairing { get; }

    public RemoteSessionTokenDescriptor Token { get; }

    public DateTimeOffset ActivatedAt { get; }

    private void ValidatePairingAndToken()
    {
        if (Pairing.DeviceId != DeviceId || Token.DeviceId != DeviceId)
        {
            throw new ArgumentException("Remote Module activation 的 device identity 必须与 pairing/token 一致。");
        }

        if (!string.Equals(Pairing.PairingId, Token.PairingId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Remote Module activation 的 pairing id 必须与 token 一致。");
        }

        if (Token.IsExpired(ActivatedAt))
        {
            throw new ArgumentException("Remote Module activation 不接受已过期 token。");
        }

        if (Pairing.AllowedTransports.Count > 0 && !Pairing.AllowedTransports.Contains(Transport.Kind))
        {
            throw new ArgumentException("Remote Module activation 的 transport 不在 pairing allow-list 内。");
        }

        if (!RemoteScopeGuard.IsSubset(Token.Scope, Pairing.Scope))
        {
            throw new ArgumentException("Remote Module activation 的 token scope 不得超过 pairing scope。");
        }
    }
}

/// <summary>
/// Remote Module 激活结果。
/// Remote Module activation result.
/// </summary>
public sealed record RemoteModuleActivationResult
{
    public RemoteModuleActivationResult(
        bool accepted,
        RemotePairingStatus pairingStatus,
        string? diagnosticsRef = null)
    {
        Accepted = accepted;
        PairingStatus = pairingStatus == RemotePairingStatus.Unspecified
            ? throw new ArgumentException("Remote Module activation result 必须声明 pairing status。", nameof(pairingStatus))
            : pairingStatus;
        DiagnosticsRef = RemoteTransportDescriptor.NormalizeOptional(diagnosticsRef);
    }

    public bool Accepted { get; }

    public RemotePairingStatus PairingStatus { get; }

    public string? DiagnosticsRef { get; }
}

/// <summary>
/// Remote Module 停用请求。
/// Remote Module deactivation request.
/// </summary>
public sealed record RemoteModuleDeactivationRequest
{
    public RemoteModuleDeactivationRequest(string moduleId, string pairingId, DeviceId deviceId, string reason)
    {
        ModuleId = IdentifierGuard.AgainstNullOrWhiteSpace(moduleId, nameof(moduleId));
        PairingId = IdentifierGuard.AgainstNullOrWhiteSpace(pairingId, nameof(pairingId));
        DeviceId = deviceId;
        Reason = IdentifierGuard.AgainstNullOrWhiteSpace(reason, nameof(reason));
    }

    public string ModuleId { get; }

    public string PairingId { get; }

    public DeviceId DeviceId { get; }

    public string Reason { get; }
}

/// <summary>
/// Remote Module 可见的连续性桥；它不暴露 HostGateway、ControlPlane、Kernel、Runtime 或 store 实现。
/// Continuity bridge visible to Remote Modules; it does not expose HostGateway, ControlPlane, Kernel, Runtime, or store implementations.
/// </summary>
public interface IRemoteContinuityBridge : IRemoteCommandIngress
{
    ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(
        RemoteThreadSnapshotQuery query,
        CancellationToken cancellationToken);

    IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
        RemoteEventSubscriptionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Remote Module 合同；模块只通过 IRemoteContinuityBridge 访问 TianShu。
/// Remote Module contract; modules access TianShu only through IRemoteContinuityBridge.
/// </summary>
public interface IRemoteContinuityModule
{
    ValueTask<RemoteModuleActivationResult> ActivateAsync(
        RemoteModuleActivationContext context,
        IRemoteContinuityBridge bridge,
        CancellationToken cancellationToken);

    ValueTask DeactivateAsync(
        RemoteModuleDeactivationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// 远程线程快照查询。
/// Remote thread snapshot query.
/// </summary>
public sealed record RemoteThreadSnapshotQuery
{
    public RemoteThreadSnapshotQuery(ThreadId threadId, DeviceId deviceId, string tokenRef)
    {
        ThreadId = threadId;
        DeviceId = deviceId;
        TokenRef = IdentifierGuard.AgainstNullOrWhiteSpace(tokenRef, nameof(tokenRef));
    }

    public ThreadId ThreadId { get; }

    public DeviceId DeviceId { get; }

    public string TokenRef { get; }
}

internal static class RemoteScopeGuard
{
    public static bool IsSubset(RemoteCommandScope candidate, RemoteCommandScope ceiling)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ceiling);

        return candidate.MaxSideEffectLevel <= ceiling.MaxSideEffectLevel
            && IsStringSubset(candidate.ThreadRefs, ceiling.ThreadRefs)
            && IsStringSubset(candidate.ScopeRefs, ceiling.ScopeRefs)
            && candidate.AllowedCommands.All(ceiling.Allows);
    }

    private static bool IsStringSubset(IReadOnlyList<string> candidate, IReadOnlyList<string> ceiling)
    {
        if (candidate.Count == 0 || ceiling.Count == 0)
        {
            return true;
        }

        var allowed = new HashSet<string>(ceiling, StringComparer.Ordinal);
        return candidate.All(allowed.Contains);
    }
}
