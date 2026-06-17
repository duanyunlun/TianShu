using System.Runtime.CompilerServices;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Remote.Local;

/// <summary>
/// 本地 Remote Continuity 示例模块；使用 in-process 方式模拟本地 HTTP polling / SSE 收发边界。
/// Local Remote Continuity sample module; it simulates local HTTP polling / SSE transport in-process.
/// </summary>
public sealed class LocalRemoteContinuityModule : IRemoteContinuityModule
{
    private readonly Func<DateTimeOffset> nowProvider;
    private readonly RemoteProjectionSecurityPolicy projectionSecurityPolicy;
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private readonly Dictionary<string, RemoteCommandResult> idempotentCommandResults = new(StringComparer.Ordinal);
    private RemoteModuleActivationContext? context;
    private IRemoteContinuityBridge? bridge;

    /// <summary>
    /// 初始化本地 Remote Module 示例。
    /// Initializes the local Remote Module sample.
    /// </summary>
    public LocalRemoteContinuityModule(
        Func<DateTimeOffset>? nowProvider = null,
        RemoteProjectionSecurityPolicy? projectionSecurityPolicy = null)
    {
        this.nowProvider = nowProvider ?? (() => DateTimeOffset.UtcNow);
        this.projectionSecurityPolicy = projectionSecurityPolicy ?? new RemoteProjectionSecurityPolicy();
    }

    /// <summary>
    /// 本地示例支持的 transport 描述符；它们只描述 loopback 形态，不主动开放监听。
    /// Transport descriptors supported by the local sample; they describe loopback surfaces without opening listeners.
    /// </summary>
    public static IReadOnlyList<RemoteTransportDescriptor> SupportedTransports { get; } =
    [
        new RemoteTransportDescriptor(
            RemoteModuleTransportKind.LocalHttpPolling,
            "remote.local.http://loopback",
            RemoteTransportSecurityMode.LocalOnly,
            bindAddress: "127.0.0.1",
            featureRefs: ["snapshot", "command"]),
        new RemoteTransportDescriptor(
            RemoteModuleTransportKind.ServerSentEvents,
            "remote.local.sse://loopback",
            RemoteTransportSecurityMode.LocalOnly,
            bindAddress: "127.0.0.1",
            featureRefs: ["event-stream"]),
    ];

    /// <summary>
    /// 当前模块是否已激活。
    /// Indicates whether the module is currently activated.
    /// </summary>
    public bool IsActivated => context is not null && bridge is not null;

    /// <inheritdoc />
    public ValueTask<RemoteModuleActivationResult> ActivateAsync(
        RemoteModuleActivationContext context,
        IRemoteContinuityBridge bridge,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bridge);
        cancellationToken.ThrowIfCancellationRequested();

        if (!SupportedTransports.Any(transport => transport.Kind == context.Transport.Kind))
        {
            return ValueTask.FromResult(new RemoteModuleActivationResult(
                accepted: false,
                RemotePairingStatus.Rejected,
                "diagnostics://remote/local/transport-not-supported"));
        }

        this.context = context;
        this.bridge = bridge;
        return ValueTask.FromResult(new RemoteModuleActivationResult(
            accepted: true,
            RemotePairingStatus.Granted,
            "diagnostics://remote/local/activated"));
    }

    /// <inheritdoc />
    public ValueTask DeactivateAsync(RemoteModuleDeactivationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (context is not null
            && string.Equals(context.ModuleId, request.ModuleId, StringComparison.Ordinal)
            && string.Equals(context.Pairing.PairingId, request.PairingId, StringComparison.Ordinal)
            && context.DeviceId == request.DeviceId)
        {
            context = null;
            bridge = null;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 读取只读线程状态快照。
    /// Reads a read-only thread snapshot.
    /// </summary>
    public ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(ThreadId threadId, CancellationToken cancellationToken)
    {
        var state = RequireActivated(RemoteTokenAudience.Snapshot, threadId);
        return GetProjectedSnapshotAsync(state, threadId, cancellationToken);
    }

    /// <summary>
    /// 在 cursor 过期或事件无法补发时刷新线程快照。
    /// Refreshes the thread snapshot when a cursor expires or events cannot be replayed.
    /// </summary>
    public ValueTask<RemoteThreadSnapshot> RefreshSnapshotAsync(
        ThreadId threadId,
        RemoteEventCursor? lastCursor,
        CancellationToken cancellationToken)
    {
        _ = lastCursor;
        var state = RequireActivated(RemoteTokenAudience.Snapshot, threadId);
        return GetProjectedSnapshotAsync(state, threadId, cancellationToken);
    }

    /// <summary>
    /// 为断线重连构建 replay 计划。
    /// Builds a replay plan for reconnect handling.
    /// </summary>
    public RemoteEventReplayPlan BuildReconnectReplayPlan(
        RemoteEventCursor? lastCursor,
        RemoteEventRetentionState retentionState,
        string? reason = null)
    {
        if (lastCursor is not null && retentionState == RemoteEventRetentionState.Available)
        {
            return new RemoteEventReplayPlan(
                RemoteEventReplayMode.FromCursor,
                retentionState,
                lastCursor,
                reason: reason);
        }

        return new RemoteEventReplayPlan(
            RemoteEventReplayMode.SnapshotThenEvents,
            retentionState,
            reason: reason ?? "snapshot refresh required");
    }

    /// <summary>
    /// 订阅本地 SSE 形态事件流。
    /// Subscribes to the local SSE-shaped event stream.
    /// </summary>
    public async IAsyncEnumerable<RemoteContinuityEvent> SubscribeServerSentEventsAsync(
        ThreadId threadId,
        RemoteEventCursor? fromCursor,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = RequireActivated(RemoteTokenAudience.EventStream, threadId);
        var replayMode = fromCursor is null
            ? RemoteEventReplayMode.SnapshotThenEvents
            : RemoteEventReplayMode.FromCursor;
        var request = new RemoteEventSubscriptionRequest(
            $"local-sse:{state.Context.DeviceId.Value}:{threadId.Value}",
            threadId,
            RemoteEventTransportKind.ServerSentEvents,
            replayMode,
            lastCursor: fromCursor,
            deviceId: state.Context.DeviceId);

        await foreach (var @event in state.Bridge.SubscribeAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return RemoteProjectionSecurityProjector.ProjectEvent(@event, projectionSecurityPolicy);
        }
    }

    /// <summary>
    /// 远程提交消息。
    /// Submits a remote message command.
    /// </summary>
    public ValueTask<RemoteCommandResult> SubmitMessageAsync(
        ThreadId threadId,
        string messageText,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? attachmentRefs = null)
        => SubmitCommandAsync(
            threadId,
            new RemoteSubmitMessagePayload(messageText, attachmentRefs),
            idempotencyKey,
            cancellationToken);

    /// <summary>
    /// 远程提交审批决策。
    /// Submits a remote approval decision.
    /// </summary>
    public ValueTask<RemoteCommandResult> SubmitApprovalDecisionAsync(
        ThreadId threadId,
        ApprovalId approvalId,
        RemoteApprovalDecisionKind decision,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken,
        string? reason = null)
        => SubmitCommandAsync(
            threadId,
            new RemoteApprovalDecisionPayload(approvalId, decision, reason),
            idempotencyKey,
            cancellationToken);

    /// <summary>
    /// 远程中断 active run。
    /// Interrupts the active run remotely.
    /// </summary>
    public ValueTask<RemoteCommandResult> InterruptAsync(
        ThreadId threadId,
        string reason,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        => SubmitCommandAsync(
            threadId,
            new RemoteInterruptPayload(reason),
            idempotencyKey,
            cancellationToken);

    /// <summary>
    /// 远程恢复 checkpoint。
    /// Resumes from a checkpoint remotely.
    /// </summary>
    public ValueTask<RemoteCommandResult> ResumeAsync(
        ThreadId threadId,
        string checkpointRef,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        => SubmitCommandAsync(
            threadId,
            new RemoteResumePayload(checkpointRef),
            idempotencyKey,
            cancellationToken);

    private async ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        ThreadId threadId,
        TPayload payload,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload
    {
        var state = RequireActivated(RemoteTokenAudience.Command, threadId);
        var commandId = BuildCommandId(state.Context, payload.Kind, idempotencyKey);
        var idempotencyCacheKey = BuildIdempotencyCacheKey(state.Context, threadId, payload.Kind, idempotencyKey);

        await commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (idempotentCommandResults.TryGetValue(idempotencyCacheKey, out var cached))
            {
                return ToDuplicateResult(cached);
            }

            var result = await SubmitCommandCoreAsync(
                state,
                threadId,
                payload,
                commandId,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
            idempotentCommandResults[idempotencyCacheKey] = result;
            return result;
        }
        finally
        {
            commandGate.Release();
        }
    }

    private async ValueTask<RemoteCommandResult> SubmitCommandCoreAsync<TPayload>(
        ActiveRemoteModuleState state,
        ThreadId threadId,
        TPayload payload,
        string commandId,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload
    {
        if (!state.Context.Token.Scope.AllowsSideEffectFor(payload.Kind))
        {
            return new RemoteCommandResult(
                commandId,
                payload.Kind,
                RemoteCommandAdmissionStatus.ScopeDenied,
                idempotencyKey,
                failureCode: "remote.local.scope_denied",
                diagnosticsRef: $"diagnostics://remote/local/{payload.Kind}/scope-or-side-effect-denied");
        }

        if (payload is RemoteApprovalDecisionPayload approvalPayload)
        {
            var approvalResult = await TryRejectExpiredApprovalAsync(
                state,
                threadId,
                approvalPayload,
                commandId,
                idempotencyKey,
                cancellationToken).ConfigureAwait(false);
            if (approvalResult is not null)
            {
                return approvalResult;
            }
        }

        var envelope = new RemoteCommandEnvelope<TPayload>(
            commandId,
            threadId,
            state.Context.DeviceId,
            sessionId: null,
            payload,
            state.Context.Token.Scope,
            idempotencyKey,
            new RemoteAuditContext(
                state.Context.Pairing.PairingId,
                state.Context.DeviceId.Value,
                state.Context.Transport.EndpointRef,
                auditRefs: ["audit://remote/local/command"]));

        return await state.Bridge.SubmitCommandAsync(envelope, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RemoteCommandResult?> TryRejectExpiredApprovalAsync(
        ActiveRemoteModuleState state,
        ThreadId threadId,
        RemoteApprovalDecisionPayload payload,
        string commandId,
        RemoteCommandIdempotencyKey idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!state.Context.Token.Audiences.Contains(RemoteTokenAudience.Snapshot))
        {
            return null;
        }

        var snapshot = await state.Bridge.GetSnapshotAsync(
            new RemoteThreadSnapshotQuery(threadId, state.Context.DeviceId, state.Context.Token.TokenRef),
            cancellationToken).ConfigureAwait(false);
        var pendingApproval = snapshot.PendingApprovals
            .FirstOrDefault(item => item.ApprovalId == payload.ApprovalId);
        if (pendingApproval is null)
        {
            return null;
        }

        if (pendingApproval.State == RemoteApprovalState.Expired
            || pendingApproval.ExpiresAt is { } expiresAt && expiresAt <= nowProvider())
        {
            return new RemoteCommandResult(
                commandId,
                payload.Kind,
                RemoteCommandAdmissionStatus.Expired,
                idempotencyKey,
                failureCode: "remote.local.approval_expired",
                diagnosticsRef: $"diagnostics://remote/local/approval/{payload.ApprovalId.Value}/expired");
        }

        if (pendingApproval.State != RemoteApprovalState.Pending)
        {
            return new RemoteCommandResult(
                commandId,
                payload.Kind,
                RemoteCommandAdmissionStatus.Invalid,
                idempotencyKey,
                failureCode: "remote.local.approval_not_pending",
                diagnosticsRef: $"diagnostics://remote/local/approval/{payload.ApprovalId.Value}/not-pending");
        }

        return null;
    }

    private async ValueTask<RemoteThreadSnapshot> GetProjectedSnapshotAsync(
        ActiveRemoteModuleState state,
        ThreadId threadId,
        CancellationToken cancellationToken)
    {
        var snapshot = await state.Bridge.GetSnapshotAsync(
            new RemoteThreadSnapshotQuery(threadId, state.Context.DeviceId, state.Context.Token.TokenRef),
            cancellationToken).ConfigureAwait(false);
        return RemoteProjectionSecurityProjector.ProjectSnapshot(snapshot, projectionSecurityPolicy);
    }

    private ActiveRemoteModuleState RequireActivated(RemoteTokenAudience audience, ThreadId threadId)
    {
        var activeContext = context;
        var activeBridge = bridge;
        if (activeContext is null || activeBridge is null)
        {
            throw new InvalidOperationException("Local Remote Module 尚未激活。");
        }

        if (!activeContext.Token.Audiences.Contains(audience))
        {
            throw new InvalidOperationException($"Local Remote Module token 不允许访问 {audience}。");
        }

        if (activeContext.Token.IsExpired(nowProvider()))
        {
            throw new InvalidOperationException("Local Remote Module token 已过期。");
        }

        if (activeContext.Token.Scope.ThreadRefs.Count > 0
            && !activeContext.Token.Scope.ThreadRefs.Contains(threadId.Value, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Local Remote Module token 不允许访问该 thread。");
        }

        return new ActiveRemoteModuleState(activeContext, activeBridge);
    }

    private static string BuildCommandId(
        RemoteModuleActivationContext context,
        RemoteCommandKind kind,
        RemoteCommandIdempotencyKey idempotencyKey)
        => $"{context.ModuleId}:{context.DeviceId.Value}:{kind}:{idempotencyKey.Value}";

    private static string BuildIdempotencyCacheKey(
        RemoteModuleActivationContext context,
        ThreadId threadId,
        RemoteCommandKind kind,
        RemoteCommandIdempotencyKey idempotencyKey)
        => $"{context.Pairing.PairingId}:{context.DeviceId.Value}:{threadId.Value}:{kind}:{idempotencyKey.Value}";

    private static RemoteCommandResult ToDuplicateResult(RemoteCommandResult cached)
        => new(
            cached.CommandId,
            cached.Kind,
            RemoteCommandAdmissionStatus.DuplicateIgnored,
            cached.IdempotencyKey,
            cached.AcceptedOperationRef,
            cached.FailureCode,
            cached.DiagnosticsRef ?? $"diagnostics://remote/local/{cached.Kind}/duplicate");

    private sealed record ActiveRemoteModuleState(
        RemoteModuleActivationContext Context,
        IRemoteContinuityBridge Bridge);
}
