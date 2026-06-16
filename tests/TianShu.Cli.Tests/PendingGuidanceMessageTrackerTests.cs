using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class PendingGuidanceMessageTrackerTests
{
    [Fact]
    public void Track_WhenCorrelationAlreadyExists_ReturnsFalse()
    {
        var tracker = new PendingGuidanceMessageTracker();

        Assert.True(tracker.Track("corr-1", "等待中的引导"));
        Assert.False(tracker.Track("corr-1", "重复引导"));
    }

    [Fact]
    public void TryConsumeCommittedMessage_WhenCorrelationMatches_ReturnsCommittedText()
    {
        var tracker = new PendingGuidanceMessageTracker();
        Assert.True(tracker.Track("corr-1", "等待中的引导"));

        var consumed = tracker.TryConsumeCommittedMessage(
            CommittedUserMessage("corr-1", "已提交的引导"),
            out var message);

        Assert.True(consumed);
        Assert.Equal("已提交的引导", message);
    }

    [Fact]
    public void TryConsumeCommittedMessage_WhenPayloadUsesPascalCase_ReturnsCommittedText()
    {
        var tracker = new PendingGuidanceMessageTracker();
        Assert.True(tracker.Track("corr-1", "等待中的引导"));

        var consumed = tracker.TryConsumeCommittedMessage(
            CommittedUserMessage("CorrelationId", "Text", "corr-1", "已提交的引导"),
            out var message);

        Assert.True(consumed);
        Assert.Equal("已提交的引导", message);
    }

    [Fact]
    public void TryConsumeCommittedMessage_WhenCorrelationDoesNotMatch_DoesNotConsume()
    {
        var tracker = new PendingGuidanceMessageTracker();
        Assert.True(tracker.Track("corr-1", "等待中的引导"));

        var consumed = tracker.TryConsumeCommittedMessage(
            CommittedUserMessage("corr-2", "其它引导"),
            out var message);

        Assert.False(consumed);
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void TrackPendingState_WhenAwaitingPendingSteerArrives_TracksCorrelation()
    {
        var tracker = new PendingGuidanceMessageTracker();

        tracker.TrackPendingState(PendingInputState("corr-state-1", "等待状态里的引导"));
        var consumed = tracker.TryConsumeCommittedMessage(
            CommittedUserMessage("corr-state-1", "已提交"),
            out var message);

        Assert.True(consumed);
        Assert.Equal("已提交", message);
    }

    [Fact]
    public void TryConsumeCommittedMessage_WhenCommittedLifecycleArrives_ConsumesTrackedGuidance()
    {
        var tracker = new PendingGuidanceMessageTracker();
        Assert.True(tracker.Track("corr-lifecycle-1", "等待中的引导"));

        var consumed = tracker.TryConsumeCommittedMessage(
            PendingFollowUpCommitted("corr-lifecycle-1", "生命周期提交的引导"),
            out var message);

        Assert.True(consumed);
        Assert.Equal("生命周期提交的引导", message);
    }

    private static ControlPlaneConversationStreamEvent CommittedUserMessage(string correlationId, string text)
        => CommittedUserMessage("correlationId", "text", correlationId, text);

    private static ControlPlaneConversationStreamEvent CommittedUserMessage(
        string correlationIdPropertyName,
        string textPropertyName,
        string correlationId,
        string text)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.UserMessageCommitted,
            Text = text,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.CommittedUserMessage,
            Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [correlationIdPropertyName] = correlationId,
                [textPropertyName] = text,
            }),
        };

    private static ControlPlaneConversationStreamEvent PendingInputState(string correlationId, string text)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            Status = "awaiting_commit",
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PendingInputState,
            Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pendingSteers"] = new object?[]
                {
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["correlationId"] = correlationId,
                        ["pendingBucket"] = "PendingSteer",
                        ["lifecycleState"] = "awaiting_commit",
                        ["inputs"] = new object?[]
                        {
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["type"] = "text",
                                ["text"] = text,
                            },
                        },
                    },
                },
            }),
        };

    private static ControlPlaneConversationStreamEvent PendingFollowUpCommitted(string correlationId, string text)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.PendingFollowUpUpdated,
            ItemId = correlationId,
            Status = "committed",
            Text = text,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.PendingInputState,
            Payload = StructuredValue.FromPlainObject(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pendingSteers"] = Array.Empty<object>(),
            }),
        };
}
