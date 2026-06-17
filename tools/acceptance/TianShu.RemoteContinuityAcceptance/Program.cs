using System.Runtime.CompilerServices;
using System.Text.Json;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;
using TianShu.Remote.Local;

var options = RemoteContinuityAcceptanceOptions.Parse(args);
Directory.CreateDirectory(options.WorkingDirectory);

var bridge = new AcceptanceRemoteContinuityBridge();
var module = new LocalRemoteContinuityModule(() => AcceptanceClock.Now);
var activation = await module.ActivateAsync(
    CreateContext(),
    bridge,
    CancellationToken.None).ConfigureAwait(false);

var readonlySnapshot = await module.GetSnapshotAsync(
    new ThreadId("thread-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);

var followedEvents = new List<RemoteContinuityEvent>();
var resumePlan = module.BuildReconnectReplayPlan(
    new RemoteEventCursor("cursor-remote-acceptance-000"),
    RemoteEventRetentionState.Available);
await foreach (var @event in module.SubscribeServerSentEventsAsync(
                   new ThreadId("thread-remote-acceptance"),
                   resumePlan.FromCursor,
                   CancellationToken.None).ConfigureAwait(false))
{
    followedEvents.Add(@event);
}

bridge.Snapshot = CreateSnapshot(
    "snapshot-approval",
    RemoteRunLifecycle.WaitingForApproval,
    pendingApprovals:
    [
        CreatePendingApproval(
            new ApprovalId("approval-remote-acceptance"),
            AcceptanceClock.Now.AddMinutes(5)),
    ]);
var approval = await module.SubmitApprovalDecisionAsync(
    new ThreadId("thread-remote-acceptance"),
    new ApprovalId("approval-remote-acceptance"),
    RemoteApprovalDecisionKind.Approve,
    new RemoteCommandIdempotencyKey("idem-approval-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);

var interrupt = await module.InterruptAsync(
    new ThreadId("thread-remote-acceptance"),
    "mobile operator interrupt",
    new RemoteCommandIdempotencyKey("idem-interrupt-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);

var resume = await module.ResumeAsync(
    new ThreadId("thread-remote-acceptance"),
    "checkpoint://thread-remote-acceptance/1",
    new RemoteCommandIdempotencyKey("idem-resume-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);

var followUp = await module.SubmitMessageAsync(
    new ThreadId("thread-remote-acceptance"),
    "请继续当前线程并补充最终说明。",
    new RemoteCommandIdempotencyKey("idem-follow-up-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);
var duplicateFollowUp = await module.SubmitMessageAsync(
    new ThreadId("thread-remote-acceptance"),
    "请继续当前线程并补充最终说明。",
    new RemoteCommandIdempotencyKey("idem-follow-up-remote-acceptance"),
    CancellationToken.None).ConfigureAwait(false);

var expiredPlan = module.BuildReconnectReplayPlan(
    new RemoteEventCursor("cursor-remote-acceptance-expired"),
    RemoteEventRetentionState.CursorExpired,
    "cursor expired");
bridge.Snapshot = CreateSnapshot("snapshot-refresh", RemoteRunLifecycle.Running);
var refreshedSnapshot = await module.RefreshSnapshotAsync(
    new ThreadId("thread-remote-acceptance"),
    new RemoteEventCursor("cursor-remote-acceptance-expired"),
    CancellationToken.None).ConfigureAwait(false);

var evidence = new RemoteContinuityAcceptanceEvidence(
    Success: activation.Accepted
             && readonlySnapshot.Redaction.RedactedKinds.Contains("absolute_path")
             && followedEvents.Count > 0
             && resumePlan.ReplayMode == RemoteEventReplayMode.FromCursor
             && approval.Status == RemoteCommandAdmissionStatus.Accepted
             && interrupt.Status == RemoteCommandAdmissionStatus.Accepted
             && resume.Status == RemoteCommandAdmissionStatus.Accepted
             && followUp.Status == RemoteCommandAdmissionStatus.Accepted
             && duplicateFollowUp.Status == RemoteCommandAdmissionStatus.DuplicateIgnored
             && expiredPlan.SnapshotRequired
             && refreshedSnapshot.SnapshotId == "snapshot-refresh",
    AcceptanceKind: "deterministic-remote-continuity",
    ActivationAccepted: activation.Accepted,
    ReadOnlyFollowerSnapshotObserved: readonlySnapshot.SnapshotId,
    ReadOnlyFollowerEventCount: followedEvents.Count,
    SnapshotRedactedKinds: readonlySnapshot.Redaction.RedactedKinds,
    CursorResumeReplayMode: resumePlan.ReplayMode.ToString(),
    CursorResumeLastCursor: bridge.LastSubscriptionRequest?.LastCursor?.Value,
    SnapshotRefreshRequired: expiredPlan.SnapshotRequired,
    SnapshotRefreshObserved: refreshedSnapshot.SnapshotId,
    RemoteApprovalStatus: approval.Status.ToString(),
    RemoteInterruptStatus: interrupt.Status.ToString(),
    RemoteResumeStatus: resume.Status.ToString(),
    RemoteFollowUpStatus: followUp.Status.ToString(),
    DuplicateFollowUpStatus: duplicateFollowUp.Status.ToString(),
    SubmittedCommandKinds: bridge.SubmittedCommandKinds.Select(static kind => kind.ToString()).ToArray(),
    SubmittedApprovalDecisions: bridge.SubmittedApprovalDecisions.Select(static decision => decision.ToString()).ToArray());

if (!string.IsNullOrWhiteSpace(options.OutputPath))
{
    var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
    if (!string.IsNullOrWhiteSpace(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    await File.WriteAllTextAsync(options.OutputPath, JsonSerializer.Serialize(evidence, JsonOptions) + Environment.NewLine)
        .ConfigureAwait(false);
}

Console.WriteLine(JsonSerializer.Serialize(evidence, JsonOptions));
return evidence.Success ? 0 : 1;

static RemoteModuleActivationContext CreateContext()
{
    var scope = new RemoteCommandScope(
        [
            RemoteCommandKind.SubmitMessage,
            RemoteCommandKind.ApprovalDecision,
            RemoteCommandKind.Interrupt,
            RemoteCommandKind.Resume,
        ],
        SideEffectLevel.HostMutation,
        threadRefs: ["thread-remote-acceptance"]);

    return new RemoteModuleActivationContext(
        "module.remote.local.acceptance",
        new DeviceId("device-remote-acceptance"),
        new RemoteTransportDescriptor(
            RemoteModuleTransportKind.ServerSentEvents,
            "remote.local.sse://acceptance",
            RemoteTransportSecurityMode.LocalOnly,
            bindAddress: "127.0.0.1"),
        new RemotePairingGrant(
            "pairing-remote-acceptance",
            new DeviceId("device-remote-acceptance"),
            "Mobile Acceptance Client",
            RemoteDeviceTrustLevel.InteractiveOperator,
            scope,
            AcceptanceClock.Now,
            AcceptanceClock.Now.AddDays(1),
            "revocation://remote-acceptance/pairing",
            [RemoteModuleTransportKind.ServerSentEvents]),
        new RemoteSessionTokenDescriptor(
            "token://remote-acceptance",
            "pairing-remote-acceptance",
            new DeviceId("device-remote-acceptance"),
            [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream, RemoteTokenAudience.Command],
            scope,
            AcceptanceClock.Now,
            AcceptanceClock.Now.AddHours(1),
            "revocation://remote-acceptance/token"),
        AcceptanceClock.Now);
}

static RemoteThreadSnapshot CreateSnapshot(
    string snapshotId,
    RemoteRunLifecycle lifecycle,
    IReadOnlyList<RemotePendingApproval>? pendingApprovals = null)
    => new(
        snapshotId,
        new ThreadId("thread-remote-acceptance"),
        new RemoteRunState(
            lifecycle,
            activeRunRef: @"C:\TianShu\workspace\runtime\remote-acceptance-run.json"),
        artifacts:
        [
            new RemoteArtifactRef(
                new ArtifactId("artifact-remote-acceptance"),
                "summary.md",
                "document",
                "available",
                uriRef: "artifact://remote-acceptance/summary",
                summary: "safe remote summary"),
        ],
        pendingApprovals: pendingApprovals);

static RemotePendingApproval CreatePendingApproval(ApprovalId approvalId, DateTimeOffset expiresAt)
    => new(
        approvalId,
        "Approve remote workspace operation",
        RemoteApprovalState.Pending,
        SideEffectLevel.WorkspaceWrite,
        requiresHumanGate: true,
        decisionOptions: ["approve", "deny"],
        riskSummary: "remote approval acceptance",
        diffRef: "diff://remote-acceptance/approval",
        expiresAt: expiresAt);

internal sealed class AcceptanceRemoteContinuityBridge : IRemoteContinuityBridge
{
    public RemoteThreadSnapshot Snapshot { get; set; } = CreateDefaultSnapshot();

    public RemoteEventSubscriptionRequest? LastSubscriptionRequest { get; private set; }

    public List<RemoteCommandKind> SubmittedCommandKinds { get; } = [];

    public List<RemoteApprovalDecisionKind> SubmittedApprovalDecisions { get; } = [];

    public ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(
        RemoteThreadSnapshotQuery query,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(Snapshot);

    public async IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
        RemoteEventSubscriptionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastSubscriptionRequest = request;
        cancellationToken.ThrowIfCancellationRequested();
        yield return new RemoteContinuityEvent(
            "event-remote-acceptance-stage",
            request.ThreadId,
            new RemoteEventCursor("cursor-remote-acceptance-001"),
            RemoteContinuityEventKind.StageChanged,
            AcceptanceClock.Now,
            StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["stageId"] = StructuredValue.FromString("stage.remote.follow"),
                ["path"] = StructuredValue.FromString(@"C:\Users\Example\.tianshu\remote-event.json"),
            }));
        await Task.Yield();
    }

    public ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
        RemoteCommandEnvelope<TPayload> command,
        CancellationToken cancellationToken)
        where TPayload : IRemoteCommandPayload
    {
        SubmittedCommandKinds.Add(command.Kind);
        if (command.Payload is RemoteApprovalDecisionPayload approval)
        {
            SubmittedApprovalDecisions.Add(approval.Decision);
        }

        return ValueTask.FromResult(new RemoteCommandResult(
            command.CommandId,
            command.Kind,
            RemoteCommandAdmissionStatus.Accepted,
            command.IdempotencyKey,
            acceptedOperationRef: $"host-operation:{command.CommandId}"));
    }

    private static RemoteThreadSnapshot CreateDefaultSnapshot()
        => new(
            "snapshot-readonly-follow",
            new ThreadId("thread-remote-acceptance"),
            new RemoteRunState(
                RemoteRunLifecycle.Running,
                activeRunRef: @"C:\TianShu\workspace\runtime\remote-acceptance-run.json"),
            artifacts:
            [
                new RemoteArtifactRef(
                    new ArtifactId("artifact-remote-acceptance"),
                    "summary.md",
                    "document",
                    "available",
                    uriRef: "artifact://remote-acceptance/summary",
                    summary: "safe remote summary"),
            ]);
}

internal static class AcceptanceClock
{
    public static DateTimeOffset Now { get; } = new(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
}

internal sealed record RemoteContinuityAcceptanceEvidence(
    bool Success,
    string AcceptanceKind,
    bool ActivationAccepted,
    string ReadOnlyFollowerSnapshotObserved,
    int ReadOnlyFollowerEventCount,
    IReadOnlyList<string> SnapshotRedactedKinds,
    string CursorResumeReplayMode,
    string? CursorResumeLastCursor,
    bool SnapshotRefreshRequired,
    string SnapshotRefreshObserved,
    string RemoteApprovalStatus,
    string RemoteInterruptStatus,
    string RemoteResumeStatus,
    string RemoteFollowUpStatus,
    string DuplicateFollowUpStatus,
    IReadOnlyList<string> SubmittedCommandKinds,
    IReadOnlyList<string> SubmittedApprovalDecisions);

internal sealed record RemoteContinuityAcceptanceOptions(string WorkingDirectory, string? OutputPath)
{
    public static RemoteContinuityAcceptanceOptions Parse(IReadOnlyList<string> args)
    {
        var workingDirectory = Directory.GetCurrentDirectory();
        string? outputPath = null;
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], "--workdir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                workingDirectory = Path.GetFullPath(args[++i]);
                continue;
            }

            if (string.Equals(args[i], "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                outputPath = Path.GetFullPath(args[++i]);
            }
        }

        return new RemoteContinuityAcceptanceOptions(workingDirectory, outputPath);
    }
}

internal static partial class Program
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
}
