using System.Text.Json;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Remote;

namespace TianShu.Contracts.Remote.Tests;

public sealed class RemoteEventStreamContractTests
{
    [Fact]
    public void RemoteEventSubscriptionRequest_RequiresExplicitTransport()
    {
        Assert.Throws<ArgumentException>(() => new RemoteEventSubscriptionRequest(
            "subscription-no-transport",
            new ThreadId("thread-event-001"),
            RemoteEventTransportKind.Unspecified,
            RemoteEventReplayMode.SnapshotThenEvents));
    }

    [Fact]
    public void RemoteEventSubscriptionRequest_RequiresCursorForCursorReplay()
    {
        Assert.Throws<ArgumentException>(() => new RemoteEventSubscriptionRequest(
            "subscription-no-cursor",
            new ThreadId("thread-event-002"),
            RemoteEventTransportKind.ServerSentEvents,
            RemoteEventReplayMode.FromCursor));
    }

    [Fact]
    public void RemoteEventReplayPlan_RequiresSnapshotWhenCursorExpired()
    {
        var plan = new RemoteEventReplayPlan(
            RemoteEventReplayMode.FromCursor,
            RemoteEventRetentionState.CursorExpired,
            new RemoteEventCursor("cursor-expired-001"),
            reason: "cursor expired");

        Assert.True(plan.SnapshotRequired);
        Assert.Equal(RemoteEventRetentionState.CursorExpired, plan.RetentionState);
        Assert.Equal("cursor-expired-001", plan.FromCursor?.Value);
    }

    [Fact]
    public void RemoteContinuityEvent_RejectsUnknownKind()
    {
        Assert.Throws<ArgumentException>(() => new RemoteContinuityEvent(
            "event-unknown",
            new ThreadId("thread-event-003"),
            new RemoteEventCursor("cursor-event-003"),
            RemoteContinuityEventKind.Unknown,
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RemoteContinuityEvent_PreservesCursorAndRedactionVisibility()
    {
        var @event = new RemoteContinuityEvent(
            "event-redacted-001",
            new ThreadId("thread-event-004"),
            new RemoteEventCursor("cursor-event-004"),
            RemoteContinuityEventKind.ToolStateChanged,
            new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero),
            payload: StructuredValue.FromObject(new Dictionary<string, StructuredValue>(StringComparer.Ordinal)
            {
                ["toolId"] = StructuredValue.FromString("write"),
                ["status"] = StructuredValue.FromString("approval_required"),
            }),
            visibility: new RemoteEventVisibility(
                redacted: true,
                visibleScopes: ["remote.thread.read"],
                redactedKinds: ["absolute_path"],
                policyRef: "remote-redaction-policy://default"),
            correlationId: "correlation-event-004");

        Assert.Equal("cursor-event-004", @event.Cursor.Value);
        Assert.True(@event.Visibility.Redacted);
        Assert.Equal("absolute_path", Assert.Single(@event.Visibility.RedactedKinds));
        Assert.Equal("correlation-event-004", @event.CorrelationId);
    }

    [Fact]
    public void RemoteEventCursor_SerializesAsIdentifierValue()
    {
        var json = JsonSerializer.Serialize(new RemoteEventCursor("cursor-json-001"));

        Assert.Contains("cursor-json-001", json, StringComparison.Ordinal);
    }
}
