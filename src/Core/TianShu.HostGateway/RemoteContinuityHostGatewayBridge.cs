using System.Runtime.CompilerServices;
using TianShu.Contracts.Host;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Projections;
using TianShu.Contracts.Remote;

namespace TianShu.HostGateway;

/// <summary>
/// 多宿主远程连续性桥，只通过 Host Gateway typed projection 消费线程状态。
/// Multi-host remote-continuity bridge that consumes thread state only through Host Gateway typed projections.
/// </summary>
public sealed class RemoteContinuityHostGatewayBridge : IRemoteContinuityBridge
{
    private readonly IHostGateway hostGateway;
    private readonly IRemoteCommandIngress commandIngress;
    private readonly string hostId;

    /// <summary>
    /// 初始化远程连续性 Host Gateway 桥。
    /// Initializes a remote-continuity Host Gateway bridge.
    /// </summary>
    public RemoteContinuityHostGatewayBridge(IHostGateway hostGateway, string hostId)
        : this(hostGateway, new RemoteCommandHostGatewayBridge(hostGateway), hostId)
    {
    }

    /// <summary>
    /// 初始化远程连续性 Host Gateway 桥，并允许测试或组合根注入命令入口。
    /// Initializes a remote-continuity Host Gateway bridge and allows tests or composition roots to inject command ingress.
    /// </summary>
    public RemoteContinuityHostGatewayBridge(
        IHostGateway hostGateway,
        IRemoteCommandIngress commandIngress,
        string hostId)
    {
        this.hostGateway = hostGateway ?? throw new ArgumentNullException(nameof(hostGateway));
        this.commandIngress = commandIngress ?? throw new ArgumentNullException(nameof(commandIngress));
        this.hostId = string.IsNullOrWhiteSpace(hostId)
            ? throw new ArgumentException("Host id 不能为空。", nameof(hostId))
            : hostId.Trim();
    }

    /// <inheritdoc />
    public async ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(
        RemoteThreadSnapshotQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var hostSnapshot = await hostGateway.SnapshotAsync(
            new HostSnapshotRequest(hostId, ProjectionScopeKind.Thread, query.ThreadId.Value),
            cancellationToken).ConfigureAwait(false);

        var threadProjection = FindThreadProjection(hostSnapshot.Projections, query.ThreadId);
        var snapshot = threadProjection is null
            ? CreateFallbackSnapshot(hostSnapshot.SnapshotId, query.ThreadId, hostSnapshot.GeneratedAt)
            : ToRemoteSnapshot(hostSnapshot, threadProjection);

        return RemoteProjectionSecurityProjector.ProjectSnapshot(snapshot);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
        RemoteEventSubscriptionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var projectionCursor = request.LastCursor is { } lastCursor
            ? new ProjectionCursor(lastCursor.Value)
            : (ProjectionCursor?)null;
        var subscription = new ProjectionSubscription(
            new SubscriptionToken(request.SubscriptionId),
            ProjectionScopeKind.Thread,
            request.ThreadId.Value,
            projectionCursor,
            includeHistory: request.ReplayMode == RemoteEventReplayMode.FromCursor);

        await foreach (var update in hostGateway.SubscribeAsync(
                           new HostSubscriptionRequest(subscription),
                           cancellationToken).ConfigureAwait(false))
        {
            var @event = ToRemoteEvent(request, update);
            if (@event is not null)
            {
                yield return RemoteProjectionSecurityProjector.ProjectEvent(@event);
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload
        => commandIngress.SubmitCommandAsync(command, cancellationToken);

    private static ThreadProjection? FindThreadProjection(
        IReadOnlyList<ProjectionPayload> projections,
        ThreadId threadId)
        => projections
            .OfType<ThreadProjectionPayload>()
            .Select(static payload => payload.Projection)
            .FirstOrDefault(projection => projection.ThreadId == threadId);

    private static RemoteThreadSnapshot ToRemoteSnapshot(HostSnapshot hostSnapshot, ThreadProjection projection)
        => new(
            hostSnapshot.SnapshotId,
            projection.ThreadId,
            ToRemoteRunState(projection.RuntimeStatus, projection.ActiveTurnId, hostSnapshot.GeneratedAt),
            diagnostics: ToRemoteDiagnostics(projection.Diagnostics),
            evidence: ToRemoteEvidence(projection.Evidence),
            capturedAt: hostSnapshot.GeneratedAt);

    private static RemoteThreadSnapshot CreateFallbackSnapshot(
        string snapshotId,
        ThreadId threadId,
        DateTimeOffset capturedAt)
        => new(
            snapshotId,
            threadId,
            new RemoteRunState(RemoteRunLifecycle.Unknown, updatedAt: capturedAt),
            diagnostics: new RemoteDiagnosticsSummary(missingReasons: ["thread_projection_missing"]),
            capturedAt: capturedAt);

    private static RemoteRunState ToRemoteRunState(
        ThreadRuntimeStatusProjection? status,
        TurnId? activeTurnId,
        DateTimeOffset updatedAt)
    {
        if (status is null)
        {
            return new RemoteRunState(RemoteRunLifecycle.Unknown, activeTurnId: activeTurnId, updatedAt: updatedAt);
        }

        return new RemoteRunState(
            ToRemoteLifecycle(status),
            status.ActiveRunRef,
            activeTurnId,
            notificationCode: status.NotificationCode,
            updatedAt: updatedAt);
    }

    private static RemoteRunLifecycle ToRemoteLifecycle(ThreadRuntimeStatusProjection status)
    {
        var normalized = Normalize(status.Lifecycle);
        return normalized switch
        {
            "idle" => RemoteRunLifecycle.Idle,
            "queued" => RemoteRunLifecycle.Queued,
            "running" => RemoteRunLifecycle.Running,
            "waitingforapproval" or "approvalrequired" => RemoteRunLifecycle.WaitingForApproval,
            "waitingforinput" or "inputrequired" => RemoteRunLifecycle.WaitingForInput,
            "interrupted" => RemoteRunLifecycle.Interrupted,
            "completed" or "succeeded" => RemoteRunLifecycle.Completed,
            "failed" or "error" => RemoteRunLifecycle.Failed,
            "cancelled" or "canceled" => RemoteRunLifecycle.Cancelled,
            _ when status.HasActiveRun => RemoteRunLifecycle.Running,
            _ => RemoteRunLifecycle.Unknown,
        };
    }

    private static RemoteDiagnosticsSummary ToRemoteDiagnostics(ThreadDiagnosticsProjection? diagnostics)
        => diagnostics is null
            ? new RemoteDiagnosticsSummary()
            : new RemoteDiagnosticsSummary(
                diagnostics.RuntimeTraceRefs,
                diagnostics.DiagnosticsRefs,
                diagnostics.MetricsEventIds,
                diagnostics.FailureCodes,
                diagnostics.MissingReasons);

    private static RemoteEvidenceSummary ToRemoteEvidence(ThreadEvidenceProjection? evidence)
        => evidence is null
            ? new RemoteEvidenceSummary()
            : new RemoteEvidenceSummary(
                evidence.TurnLogRef,
                evidence.RolloutRef,
                evidence.AuditRefs,
                evidence.DowngradeReasons);

    private static RemoteContinuityEvent? ToRemoteEvent(
        RemoteEventSubscriptionRequest request,
        HostViewUpdate update)
    {
        if (update.Delta?.Payload is ThreadProjectionPayload threadPayload)
        {
            var projection = threadPayload.Projection;
            if (projection.ThreadId != request.ThreadId)
            {
                return null;
            }

            var cursor = update.Delta.Cursor is { } projectionCursor
                ? new RemoteEventCursor(projectionCursor.Value)
                : new RemoteEventCursor($"host:{request.SubscriptionId}:thread:{projection.ThreadId.Value}");
            return new RemoteContinuityEvent(
                $"remote:{request.SubscriptionId}:{cursor.Value}",
                projection.ThreadId,
                cursor,
                RemoteContinuityEventKind.RunStateChanged,
                DateTimeOffset.UtcNow,
                BuildThreadEventPayload(projection),
                correlationId: request.SubscriptionId);
        }

        if (update.Reset is { } reset
            && reset.ScopeKind == ProjectionScopeKind.Thread
            && string.Equals(reset.ScopeKey, request.ThreadId.Value, StringComparison.Ordinal))
        {
            var cursor = reset.Cursor is { } projectionCursor
                ? new RemoteEventCursor(projectionCursor.Value)
                : new RemoteEventCursor($"host:{request.SubscriptionId}:reset:{reset.ScopeKey}");
            return new RemoteContinuityEvent(
                $"remote:{request.SubscriptionId}:{cursor.Value}",
                request.ThreadId,
                cursor,
                RemoteContinuityEventKind.SnapshotRequired,
                DateTimeOffset.UtcNow,
                StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                {
                    ["reason"] = StructuredValue.FromString(reset.Reason),
                }),
                correlationId: request.SubscriptionId);
        }

        return null;
    }

    private static StructuredValue BuildThreadEventPayload(ThreadProjection projection)
    {
        var values = new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
        {
            ["threadId"] = StructuredValue.FromString(projection.ThreadId.Value),
            ["title"] = StructuredValue.FromString(projection.Title),
            ["lifecycle"] = StructuredValue.FromString(projection.RuntimeStatus?.Lifecycle ?? "unknown"),
            ["hasActiveTurn"] = StructuredValue.FromBoolean(projection.HasActiveTurn),
        };

        if (projection.ActiveTurnId is { } activeTurnId)
        {
            values["activeTurnId"] = StructuredValue.FromString(activeTurnId.Value);
        }

        if (!string.IsNullOrWhiteSpace(projection.RuntimeStatus?.ActiveRunRef))
        {
            values["activeRunRef"] = StructuredValue.FromString(projection.RuntimeStatus.ActiveRunRef);
        }

        if (!string.IsNullOrWhiteSpace(projection.RuntimeStatus?.NotificationCode))
        {
            values["notificationCode"] = StructuredValue.FromString(projection.RuntimeStatus.NotificationCode);
        }

        return StructuredValue.FromObject(values);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
