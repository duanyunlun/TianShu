using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;
using TianShu.Contracts.Sessions;

namespace TianShu.Cli.Tests;

public sealed class ChatSessionStateTests
{
    [Fact]
    public void ApplySessionSnapshot_TracksActiveThreadAndCurrentThread()
    {
        var state = new ChatSessionState();

        state.ApplySessionSnapshot(new ControlPlaneSessionSnapshot
        {
            ActiveThreadId = new ThreadId("thread_snapshot"),
            HasActiveTurn = true,
        });

        Assert.Equal("thread_snapshot", state.SessionActiveThreadId);
        Assert.Equal("thread_snapshot", state.LastObservedThreadId);
        Assert.Equal("thread_snapshot", state.CurrentThreadId);
    }

    [Fact]
    public void ObserveStreamEvent_UsesSessionThreadFallbackAndUpdatesTurn()
    {
        var state = new ChatSessionState();
        state.SetSessionActiveThreadId("thread_active");

        var observed = state.ObserveStreamEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            TurnId = new TurnId("turn_001"),
        });

        Assert.Equal("thread_active", observed);
        Assert.Equal("thread_active", state.LastObservedThreadId);
        Assert.Equal("turn_001", state.LastObservedTurnId);
    }

    [Fact]
    public void RememberUserRequestedInterrupt_ConsumesMatchingInterruptedTurn()
    {
        var state = new ChatSessionState();
        state.RememberUserRequestedInterrupt("turn_interrupt");

        Assert.True(state.TryConsumeUserRequestedInterrupt("turn_interrupt", "interrupted"));
        Assert.False(state.TryConsumeUserRequestedInterrupt("turn_interrupt", "interrupted"));
    }

    [Fact]
    public void RememberUserRequestedInterrupt_ConsumesPendingInterruptWithoutTurnId()
    {
        var state = new ChatSessionState();
        state.RememberUserRequestedInterrupt(null);

        Assert.True(state.TryConsumeUserRequestedInterrupt("turn_late", "interrupted"));
        Assert.False(state.TryConsumeUserRequestedInterrupt("turn_late", "interrupted"));
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("failed", true)]
    [InlineData("errored", true)]
    [InlineData("cancelled", true)]
    [InlineData("canceled", true)]
    [InlineData("interrupted", true)]
    [InlineData("running", false)]
    public void IsTerminalTurnStatus_ClassifiesTerminalStatuses(string status, bool expected)
    {
        Assert.Equal(expected, ChatSessionState.IsTerminalTurnStatus(status));
    }

    [Fact]
    public void ClearCurrentThread_RemovesThreadTurnAndInterruptState()
    {
        var state = new ChatSessionState();
        state.SetSessionActiveThreadId("thread_a");
        state.ApplyTurnResult("turn_a", "running");
        state.RememberUserRequestedInterrupt("turn_a");

        state.ClearCurrentThread();

        Assert.Null(state.CurrentThreadId);
        Assert.Null(state.LastObservedTurnId);
        Assert.Null(state.LastObservedTurnStatus);
        Assert.False(state.TryConsumeUserRequestedInterrupt("turn_a", "interrupted"));
    }
}
