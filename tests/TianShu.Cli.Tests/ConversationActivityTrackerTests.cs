using TianShu.Cli.Interaction.Orchestration;

namespace TianShu.Cli.Tests;

public sealed class ConversationActivityTrackerTests
{
    [Fact]
    public void StartAndCompleteOperation_TracksBusyAndWorkingElapsed()
    {
        var tracker = new ConversationActivityTracker();

        Assert.True(tracker.StartOperation());

        Assert.True(tracker.IsBusy);
        Assert.NotNull(tracker.WorkingElapsed);

        Assert.True(tracker.CompleteOperation());
        Assert.False(tracker.IsBusy);
        Assert.Null(tracker.WorkingElapsed);
    }

    [Fact]
    public void HasSteerableConversation_RequiresActiveNonTerminalTurn()
    {
        var tracker = new ConversationActivityTracker();

        tracker.ApplySessionActiveTurn(true);

        Assert.False(tracker.IsBusy);
        Assert.True(tracker.HasSteerableConversation("turn-1", "in_progress"));
        Assert.False(tracker.HasSteerableConversation("turn-1", "completed"));
    }
}
