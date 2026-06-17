using System.Runtime.CompilerServices;
using TianShu.Contracts.Kernel;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;
using TianShu.Remote.Local;

namespace TianShu.Remote.Local.Tests;

public sealed class LocalRemoteContinuityModuleTests
{
    [Fact]
    public async Task ActivateAsync_AcceptsSupportedLocalTransports()
    {
        var module = new LocalRemoteContinuityModule(() => TestClock.Now);

        var result = await module.ActivateAsync(
            CreateContext(RemoteModuleTransportKind.ServerSentEvents),
            new FakeRemoteContinuityBridge(),
            CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.True(module.IsActivated);
        Assert.Contains(LocalRemoteContinuityModule.SupportedTransports, static transport => transport.Kind == RemoteModuleTransportKind.LocalHttpPolling);
        Assert.Contains(LocalRemoteContinuityModule.SupportedTransports, static transport => transport.Kind == RemoteModuleTransportKind.ServerSentEvents);
        Assert.All(LocalRemoteContinuityModule.SupportedTransports, static transport => Assert.False(transport.AllowsPublicNetwork));
    }

    [Fact]
    public async Task Operations_RejectBeforeActivationAndAfterDeactivation()
    {
        var module = new LocalRemoteContinuityModule(() => TestClock.Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() => module.GetSnapshotAsync(
            new ThreadId("thread-001"),
            CancellationToken.None).AsTask());

        var result = await module.ActivateAsync(
            CreateContext(RemoteModuleTransportKind.ServerSentEvents),
            new FakeRemoteContinuityBridge(),
            CancellationToken.None);
        Assert.True(result.Accepted);

        await module.DeactivateAsync(
            new RemoteModuleDeactivationRequest(
                "module.remote.local",
                "pairing-001",
                new DeviceId("device-001"),
                "test"),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "不应提交",
            new RemoteCommandIdempotencyKey("idem-after-deactivate"),
            CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetSnapshotAsync_DelegatesReadOnlySnapshotToBridge()
    {
        var bridge = new FakeRemoteContinuityBridge();
        bridge.Snapshot = new RemoteThreadSnapshot(
            "snapshot-local-001",
            new ThreadId("thread-001"),
            new RemoteRunState(RemoteRunLifecycle.Running));
        var module = await ActivateAsync(bridge);

        var snapshot = await module.GetSnapshotAsync(new ThreadId("thread-001"), CancellationToken.None);

        Assert.Equal("snapshot-local-001", snapshot.SnapshotId);
        Assert.Equal("thread-001", bridge.LastSnapshotQuery?.ThreadId.Value);
        Assert.Equal("device-001", bridge.LastSnapshotQuery?.DeviceId.Value);
        Assert.Equal("token://local-001", bridge.LastSnapshotQuery?.TokenRef);
    }

    [Fact]
    public async Task GetSnapshotAsync_RedactsOutboundSnapshotBeforeReturningToRemoteConsumer()
    {
        var bridge = new FakeRemoteContinuityBridge();
        bridge.Snapshot = new RemoteThreadSnapshot(
            "snapshot-local-redaction-001",
            new ThreadId("thread-001"),
            new RemoteRunState(
                RemoteRunLifecycle.Running,
                activeRunRef: @"D:\GitRepos\Personal\TianShu\runtime\run.json"),
            artifacts:
            [
                new RemoteArtifactRef(
                    new ArtifactId("artifact-local-redaction-001"),
                    "summary.md",
                    "document",
                    "available",
                    uriRef: "file:///D:/GitRepos/Personal/TianShu/summary.md",
                    summary: "secret=hidden"),
            ]);
        var module = await ActivateAsync(bridge);

        var snapshot = await module.GetSnapshotAsync(new ThreadId("thread-001"), CancellationToken.None);

        Assert.Equal("[redacted:absolute_path]", snapshot.RunState.ActiveRunRef);
        Assert.Equal("[redacted:absolute_path]", Assert.Single(snapshot.Artifacts).UriRef);
        Assert.Equal("[redacted:secret]", Assert.Single(snapshot.Artifacts).Summary);
        Assert.Contains("absolute_path", snapshot.Redaction.RedactedKinds);
        Assert.Contains("secret", snapshot.Redaction.RedactedKinds);
    }

    [Fact]
    public async Task SubscribeServerSentEventsAsync_DelegatesEventStreamToBridge()
    {
        var bridge = new FakeRemoteContinuityBridge
        {
            Events =
            [
                new RemoteContinuityEvent(
                    "event-local-001",
                    new ThreadId("thread-001"),
                    new RemoteEventCursor("cursor-local-001"),
                    RemoteContinuityEventKind.StageChanged,
                    TestClock.Now),
            ],
        };
        var module = await ActivateAsync(bridge);

        var events = new List<RemoteContinuityEvent>();
        await foreach (var item in module.SubscribeServerSentEventsAsync(
                           new ThreadId("thread-001"),
                           new RemoteEventCursor("cursor-before"),
                           CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.Equal("event-local-001", Assert.Single(events).EventId);
        Assert.Equal(RemoteEventTransportKind.ServerSentEvents, bridge.LastSubscriptionRequest?.TransportKind);
        Assert.Equal(RemoteEventReplayMode.FromCursor, bridge.LastSubscriptionRequest?.ReplayMode);
        Assert.Equal("cursor-before", bridge.LastSubscriptionRequest?.LastCursor?.Value);
    }

    [Fact]
    public async Task SubscribeServerSentEventsAsync_RedactsOutboundEventPayload()
    {
        var bridge = new FakeRemoteContinuityBridge
        {
            Events =
            [
                new RemoteContinuityEvent(
                    "event-local-redaction-001",
                    new ThreadId("thread-001"),
                    new RemoteEventCursor("cursor-local-redaction-001"),
                    RemoteContinuityEventKind.ArtifactChanged,
                    TestClock.Now,
                    StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
                    {
                        ["file_content"] = StructuredValue.FromString("workspace file body"),
                        ["path"] = StructuredValue.FromString(@"C:\Users\SEMI\.tianshu\workspace.txt"),
                    })),
            ],
        };
        var module = await ActivateAsync(bridge);

        var events = new List<RemoteContinuityEvent>();
        await foreach (var item in module.SubscribeServerSentEventsAsync(
                           new ThreadId("thread-001"),
                           null,
                           CancellationToken.None))
        {
            events.Add(item);
        }

        var payload = Assert.Single(events).Payload;
        Assert.NotNull(payload);
        Assert.Equal("[redacted:workspace_file_content]", payload!.GetProperty("file_content").GetString());
        Assert.Equal("[redacted:absolute_path]", payload.GetProperty("path").GetString());
        Assert.True(Assert.Single(events).Visibility.Redacted);
        Assert.Contains("workspace_file_content", Assert.Single(events).Visibility.RedactedKinds);
        Assert.Contains("absolute_path", Assert.Single(events).Visibility.RedactedKinds);
    }

    [Fact]
    public async Task BuildReconnectReplayPlan_UsesCursorWhenAvailableAndRequiresSnapshotWhenExpired()
    {
        var module = await ActivateAsync(new FakeRemoteContinuityBridge());

        var available = module.BuildReconnectReplayPlan(
            new RemoteEventCursor("cursor-resume-001"),
            RemoteEventRetentionState.Available);
        var expired = module.BuildReconnectReplayPlan(
            new RemoteEventCursor("cursor-expired-001"),
            RemoteEventRetentionState.CursorExpired,
            "cursor expired");

        Assert.Equal(RemoteEventReplayMode.FromCursor, available.ReplayMode);
        Assert.False(available.SnapshotRequired);
        Assert.Equal("cursor-resume-001", available.FromCursor?.Value);
        Assert.Equal(RemoteEventReplayMode.SnapshotThenEvents, expired.ReplayMode);
        Assert.True(expired.SnapshotRequired);
        Assert.Equal("cursor expired", expired.Reason);
    }

    [Fact]
    public async Task ReconnectFlow_UsesCursorResumeThenSnapshotRefreshWhenCursorExpires()
    {
        var bridge = new FakeRemoteContinuityBridge
        {
            Snapshot = new RemoteThreadSnapshot(
                "snapshot-reconnect-refresh",
                new ThreadId("thread-001"),
                new RemoteRunState(RemoteRunLifecycle.Running)),
            Events =
            [
                new RemoteContinuityEvent(
                    "event-reconnect-001",
                    new ThreadId("thread-001"),
                    new RemoteEventCursor("cursor-reconnect-next"),
                    RemoteContinuityEventKind.RunStateChanged,
                    TestClock.Now),
            ],
        };
        var module = await ActivateAsync(bridge);

        var replay = module.BuildReconnectReplayPlan(
            new RemoteEventCursor("cursor-reconnect-last"),
            RemoteEventRetentionState.Available);
        var events = new List<RemoteContinuityEvent>();
        await foreach (var item in module.SubscribeServerSentEventsAsync(
                           new ThreadId("thread-001"),
                           replay.FromCursor,
                           CancellationToken.None))
        {
            events.Add(item);
        }

        var expired = module.BuildReconnectReplayPlan(
            new RemoteEventCursor("cursor-reconnect-last"),
            RemoteEventRetentionState.CursorExpired);
        var snapshot = await module.RefreshSnapshotAsync(
            new ThreadId("thread-001"),
            replay.FromCursor,
            CancellationToken.None);

        Assert.Equal(RemoteEventReplayMode.FromCursor, replay.ReplayMode);
        Assert.Equal("cursor-reconnect-last", bridge.LastSubscriptionRequest?.LastCursor?.Value);
        Assert.Equal("event-reconnect-001", Assert.Single(events).EventId);
        Assert.True(expired.SnapshotRequired);
        Assert.Equal("snapshot-reconnect-refresh", snapshot.SnapshotId);
    }

    [Fact]
    public async Task RefreshSnapshotAsync_UsesSnapshotProjectionForReconnectRecovery()
    {
        var bridge = new FakeRemoteContinuityBridge
        {
            Snapshot = new RemoteThreadSnapshot(
                "snapshot-refresh-001",
                new ThreadId("thread-001"),
                new RemoteRunState(
                    RemoteRunLifecycle.Running,
                    activeRunRef: @"C:\Users\SEMI\.tianshu\run.json")),
        };
        var module = await ActivateAsync(bridge);

        var snapshot = await module.RefreshSnapshotAsync(
            new ThreadId("thread-001"),
            new RemoteEventCursor("cursor-expired-001"),
            CancellationToken.None);

        Assert.Equal("snapshot-refresh-001", snapshot.SnapshotId);
        Assert.Equal("[redacted:absolute_path]", snapshot.RunState.ActiveRunRef);
        Assert.Equal("thread-001", bridge.LastSnapshotQuery?.ThreadId.Value);
        Assert.Equal(1, bridge.SnapshotReadCount);
    }

    [Fact]
    public async Task CommandMethods_DelegateSubmitApprovalInterruptAndResumeToBridge()
    {
        var bridge = new FakeRemoteContinuityBridge();
        var module = await ActivateAsync(bridge);

        await module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "继续执行",
            new RemoteCommandIdempotencyKey("idem-submit"),
            CancellationToken.None);
        bridge.Snapshot = SnapshotWithPendingApproval(
            new ApprovalId("approval-001"),
            TestClock.Now.AddMinutes(5));
        await module.SubmitApprovalDecisionAsync(
            new ThreadId("thread-001"),
            new ApprovalId("approval-001"),
            RemoteApprovalDecisionKind.Approve,
            new RemoteCommandIdempotencyKey("idem-approval"),
            CancellationToken.None);
        bridge.Snapshot = SnapshotWithPendingApproval(
            new ApprovalId("approval-deny-001"),
            TestClock.Now.AddMinutes(5));
        await module.SubmitApprovalDecisionAsync(
            new ThreadId("thread-001"),
            new ApprovalId("approval-deny-001"),
            RemoteApprovalDecisionKind.Deny,
            new RemoteCommandIdempotencyKey("idem-approval-deny"),
            CancellationToken.None);
        await module.InterruptAsync(
            new ThreadId("thread-001"),
            "stop",
            new RemoteCommandIdempotencyKey("idem-interrupt"),
            CancellationToken.None);
        await module.ResumeAsync(
            new ThreadId("thread-001"),
            "checkpoint://thread-001/1",
            new RemoteCommandIdempotencyKey("idem-resume"),
            CancellationToken.None);

        Assert.Equal(
            [
                RemoteCommandKind.SubmitMessage,
                RemoteCommandKind.ApprovalDecision,
                RemoteCommandKind.ApprovalDecision,
                RemoteCommandKind.Interrupt,
                RemoteCommandKind.Resume,
            ],
            bridge.SubmittedCommandKinds);
        Assert.Equal(
            [RemoteApprovalDecisionKind.Approve, RemoteApprovalDecisionKind.Deny],
            bridge.SubmittedApprovalDecisions);
        Assert.All(bridge.SubmittedThreadIds, static threadId => Assert.Equal("thread-001", threadId));
        Assert.All(bridge.SubmittedDeviceIds, static deviceId => Assert.Equal("device-001", deviceId));
    }

    [Fact]
    public async Task SubmitMessageAsync_DeduplicatesRepeatedCommandByIdempotencyKey()
    {
        var bridge = new FakeRemoteContinuityBridge();
        var module = await ActivateAsync(bridge);

        var first = await module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "继续执行",
            new RemoteCommandIdempotencyKey("idem-duplicate"),
            CancellationToken.None);
        var duplicate = await module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "继续执行",
            new RemoteCommandIdempotencyKey("idem-duplicate"),
            CancellationToken.None);

        Assert.Equal(RemoteCommandAdmissionStatus.Accepted, first.Status);
        Assert.Equal(RemoteCommandAdmissionStatus.DuplicateIgnored, duplicate.Status);
        Assert.Equal(first.AcceptedOperationRef, duplicate.AcceptedOperationRef);
        Assert.Single(bridge.SubmittedCommandKinds);
    }

    [Fact]
    public async Task SubmitMessageAsync_ReturnsScopeDeniedWhenTokenDoesNotAllowCommand()
    {
        var readOnlyScope = new RemoteCommandScope([RemoteCommandKind.ApprovalDecision], SideEffectLevel.ReadOnly, threadRefs: ["thread-001"]);
        var bridge = new FakeRemoteContinuityBridge();
        var module = await ActivateAsync(bridge, tokenScope: readOnlyScope, audiences: [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream, RemoteTokenAudience.Command]);

        var result = await module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "不应提交",
            new RemoteCommandIdempotencyKey("idem-denied"),
            CancellationToken.None);

        Assert.Equal(RemoteCommandAdmissionStatus.ScopeDenied, result.Status);
        Assert.Empty(bridge.SubmittedCommandKinds);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_ReturnsScopeDeniedWhenTokenIsReadOnly()
    {
        var readOnlyApprovalScope = new RemoteCommandScope(
            [RemoteCommandKind.ApprovalDecision],
            SideEffectLevel.ReadOnly,
            threadRefs: ["thread-001"]);
        var bridge = new FakeRemoteContinuityBridge();
        var module = await ActivateAsync(
            bridge,
            tokenScope: readOnlyApprovalScope,
            audiences: [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream, RemoteTokenAudience.Command]);

        var result = await module.SubmitApprovalDecisionAsync(
            new ThreadId("thread-001"),
            new ApprovalId("approval-readonly-denied"),
            RemoteApprovalDecisionKind.Approve,
            new RemoteCommandIdempotencyKey("idem-readonly-approval"),
            CancellationToken.None);

        Assert.Equal(RemoteCommandAdmissionStatus.ScopeDenied, result.Status);
        Assert.Equal("remote.local.scope_denied", result.FailureCode);
        Assert.Empty(bridge.SubmittedCommandKinds);
    }

    [Fact]
    public async Task SubmitApprovalDecisionAsync_ReturnsExpiredWhenPendingApprovalExpired()
    {
        var bridge = new FakeRemoteContinuityBridge
        {
            Snapshot = SnapshotWithPendingApproval(
                new ApprovalId("approval-expired"),
                TestClock.Now.AddMinutes(-1)),
        };
        var module = await ActivateAsync(bridge);

        var result = await module.SubmitApprovalDecisionAsync(
            new ThreadId("thread-001"),
            new ApprovalId("approval-expired"),
            RemoteApprovalDecisionKind.Approve,
            new RemoteCommandIdempotencyKey("idem-expired-approval"),
            CancellationToken.None);

        Assert.Equal(RemoteCommandAdmissionStatus.Expired, result.Status);
        Assert.Equal("remote.local.approval_expired", result.FailureCode);
        Assert.Equal(1, bridge.SnapshotReadCount);
        Assert.Empty(bridge.SubmittedCommandKinds);
    }

    [Fact]
    public async Task ReadOnlySubscriptionToken_AllowsSnapshotAndEventsButRejectsCommands()
    {
        var readOnlySubscriptionScope = new RemoteCommandScope(
            [],
            SideEffectLevel.ReadOnly,
            threadRefs: ["thread-001"]);
        var bridge = new FakeRemoteContinuityBridge
        {
            Snapshot = new RemoteThreadSnapshot(
                "snapshot-readonly-subscription",
                new ThreadId("thread-001"),
                new RemoteRunState(RemoteRunLifecycle.Running)),
            Events =
            [
                new RemoteContinuityEvent(
                    "event-readonly-subscription",
                    new ThreadId("thread-001"),
                    new RemoteEventCursor("cursor-readonly-subscription"),
                    RemoteContinuityEventKind.Heartbeat,
                    TestClock.Now),
            ],
        };
        var module = await ActivateAsync(
            bridge,
            tokenScope: readOnlySubscriptionScope,
            audiences: [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream]);

        var snapshot = await module.GetSnapshotAsync(new ThreadId("thread-001"), CancellationToken.None);
        var events = new List<RemoteContinuityEvent>();
        await foreach (var item in module.SubscribeServerSentEventsAsync(
                           new ThreadId("thread-001"),
                           null,
                           CancellationToken.None))
        {
            events.Add(item);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => module.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "只读 token 不应提交命令",
            new RemoteCommandIdempotencyKey("idem-readonly-subscription"),
            CancellationToken.None).AsTask());
        Assert.Equal("snapshot-readonly-subscription", snapshot.SnapshotId);
        Assert.Equal("event-readonly-subscription", Assert.Single(events).EventId);
        Assert.Empty(bridge.SubmittedCommandKinds);
    }

    [Fact]
    public async Task Operations_RejectExpiredTokenWrongThreadAndMissingAudience()
    {
        var expiredModule = await ActivateAsync(
            new FakeRemoteContinuityBridge(),
            now: TestClock.Now.AddHours(2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => expiredModule.GetSnapshotAsync(new ThreadId("thread-001"), CancellationToken.None).AsTask());

        var module = await ActivateAsync(new FakeRemoteContinuityBridge());
        await Assert.ThrowsAsync<InvalidOperationException>(() => module.GetSnapshotAsync(new ThreadId("thread-other"), CancellationToken.None).AsTask());

        var noCommandModule = await ActivateAsync(
            new FakeRemoteContinuityBridge(),
            audiences: [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => noCommandModule.SubmitMessageAsync(
            new ThreadId("thread-001"),
            "不应提交",
            new RemoteCommandIdempotencyKey("idem-no-command"),
            CancellationToken.None).AsTask());
    }

    [Fact]
    public void LocalRemoteContinuityModule_ShouldNotReferenceRuntimeWorkspaceOrStores()
    {
        var repoRoot = FindRepoRoot();
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "Modules", "TianShu.Remote.Local", "LocalRemoteContinuityModule.cs"));
        var forbiddenTokens = new[]
        {
            "TianShu.Execution.Runtime",
            "TianShu.RuntimeComposition",
            "StableKernelCore",
            "RuntimeStep",
            "StageGraph",
            "WorkspaceResolver",
            "KernelThreadStore",
            "IProjectionRuntimeStores",
            "File.Write",
            "Directory.CreateDirectory",
        };

        var violations = forbiddenTokens
            .Where(token => source.Contains(token, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "本地 Remote Module 示例不得直接引用 Runtime、workspace 或 store。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static async Task<LocalRemoteContinuityModule> ActivateAsync(
        FakeRemoteContinuityBridge bridge,
        RemoteCommandScope? tokenScope = null,
        IReadOnlyList<RemoteTokenAudience>? audiences = null,
        DateTimeOffset? now = null)
    {
        var module = new LocalRemoteContinuityModule(() => now ?? TestClock.Now);
        var result = await module.ActivateAsync(
            CreateContext(
                RemoteModuleTransportKind.ServerSentEvents,
                tokenScope,
                audiences),
            bridge,
            CancellationToken.None);
        Assert.True(result.Accepted);
        return module;
    }

    private static RemoteModuleActivationContext CreateContext(
        RemoteModuleTransportKind transportKind,
        RemoteCommandScope? tokenScope = null,
        IReadOnlyList<RemoteTokenAudience>? audiences = null)
    {
        var pairingScope = new RemoteCommandScope(
            [
                RemoteCommandKind.SubmitMessage,
                RemoteCommandKind.ApprovalDecision,
                RemoteCommandKind.Interrupt,
                RemoteCommandKind.Resume,
            ],
            SideEffectLevel.HostMutation,
            threadRefs: ["thread-001"]);
        return new RemoteModuleActivationContext(
            "module.remote.local",
            new DeviceId("device-001"),
            new RemoteTransportDescriptor(
                transportKind,
                "remote.local.sse://loopback",
                RemoteTransportSecurityMode.LocalOnly,
                bindAddress: "127.0.0.1"),
            new RemotePairingGrant(
                "pairing-001",
                new DeviceId("device-001"),
                "Phone",
                RemoteDeviceTrustLevel.InteractiveOperator,
                pairingScope,
                TestClock.Now,
                TestClock.Now.AddDays(7),
                "revocation://pairing-001",
                [RemoteModuleTransportKind.ServerSentEvents, RemoteModuleTransportKind.LocalHttpPolling]),
            new RemoteSessionTokenDescriptor(
                "token://local-001",
                "pairing-001",
                new DeviceId("device-001"),
                audiences ?? [RemoteTokenAudience.Snapshot, RemoteTokenAudience.EventStream, RemoteTokenAudience.Command],
                tokenScope ?? new RemoteCommandScope(
                    [
                        RemoteCommandKind.SubmitMessage,
                        RemoteCommandKind.ApprovalDecision,
                        RemoteCommandKind.Interrupt,
                        RemoteCommandKind.Resume,
                    ],
                    SideEffectLevel.HostMutation,
                    threadRefs: ["thread-001"]),
                TestClock.Now,
                TestClock.Now.AddHours(1),
                "revocation://token-001"),
            TestClock.Now);
    }

    private static RemoteThreadSnapshot SnapshotWithPendingApproval(
        ApprovalId approvalId,
        DateTimeOffset expiresAt)
        => new(
            "snapshot-approval",
            new ThreadId("thread-001"),
            new RemoteRunState(RemoteRunLifecycle.WaitingForApproval),
            pendingApprovals:
            [
                new RemotePendingApproval(
                    approvalId,
                    "Approve workspace write",
                    RemoteApprovalState.Pending,
                    SideEffectLevel.WorkspaceWrite,
                    requiresHumanGate: true,
                    decisionOptions: ["approve", "deny"],
                    expiresAt: expiresAt),
            ]);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TianShu.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法从测试运行目录定位 TianShu 仓库根目录。");
    }

    private static class TestClock
    {
        public static DateTimeOffset Now { get; } = new(2026, 6, 17, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeRemoteContinuityBridge : IRemoteContinuityBridge
    {
        public RemoteThreadSnapshot? Snapshot { get; set; }

        public IReadOnlyList<RemoteContinuityEvent> Events { get; set; } = [];

        public RemoteThreadSnapshotQuery? LastSnapshotQuery { get; private set; }

        public RemoteEventSubscriptionRequest? LastSubscriptionRequest { get; private set; }

        public int SnapshotReadCount { get; private set; }

        public List<RemoteCommandKind> SubmittedCommandKinds { get; } = [];

        public List<RemoteApprovalDecisionKind> SubmittedApprovalDecisions { get; } = [];

        public List<string> SubmittedThreadIds { get; } = [];

        public List<string> SubmittedDeviceIds { get; } = [];

        public ValueTask<RemoteThreadSnapshot> GetSnapshotAsync(RemoteThreadSnapshotQuery query, CancellationToken cancellationToken)
        {
            LastSnapshotQuery = query;
            SnapshotReadCount++;
            return ValueTask.FromResult(Snapshot ?? new RemoteThreadSnapshot(
                "snapshot-default",
                query.ThreadId,
                new RemoteRunState(RemoteRunLifecycle.Idle)));
        }

        public async IAsyncEnumerable<RemoteContinuityEvent> SubscribeAsync(
            RemoteEventSubscriptionRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastSubscriptionRequest = request;
            foreach (var item in Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public ValueTask<RemoteCommandResult> SubmitCommandAsync<TPayload>(
            RemoteCommandEnvelope<TPayload> command,
            CancellationToken cancellationToken)
            where TPayload : IRemoteCommandPayload
        {
            SubmittedCommandKinds.Add(command.Kind);
            SubmittedThreadIds.Add(command.ThreadId.Value);
            SubmittedDeviceIds.Add(command.DeviceId.Value);
            if (command.Payload is RemoteApprovalDecisionPayload approval)
            {
                SubmittedApprovalDecisions.Add(approval.Decision);
            }

            return ValueTask.FromResult(new RemoteCommandResult(
                command.CommandId,
                command.Kind,
                RemoteCommandAdmissionStatus.Accepted,
                command.IdempotencyKey,
                $"host-operation:{command.CommandId}"));
        }
    }
}
