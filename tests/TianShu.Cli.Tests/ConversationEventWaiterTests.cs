using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class ConversationEventWaiterTests
{
    [Fact]
    public async Task WaitForEventAsync_ReturnsPreviouslyObservedMatchingEvent()
    {
        var waiter = new ConversationEventWaiter();
        var started = Event(ControlPlaneConversationStreamEventKind.TurnStarted, "turn-1");
        var completed = Event(ControlPlaneConversationStreamEventKind.TurnCompleted, "turn-1");
        waiter.RecordObservedEventAndNotifyWaiters(started);
        waiter.RecordObservedEventAndNotifyWaiters(completed);

        var matched = await waiter.WaitForEventAsync(
            streamEvent => streamEvent.Kind == ControlPlaneConversationStreamEventKind.TurnCompleted,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Same(completed, matched);
        Assert.Equal(2, waiter.ObservedEventCount);
        Assert.Equal(0, waiter.WaitingRegistrationCount);
    }

    [Fact]
    public async Task WaitForEventAsync_CompletesWhenFutureEventArrives()
    {
        var waiter = new ConversationEventWaiter();
        var waitTask = waiter.WaitForEventAsync(
            streamEvent => streamEvent.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(1, waiter.WaitingRegistrationCount);
        var toolStarted = Event(ControlPlaneConversationStreamEventKind.ToolCallStarted, "turn-2");
        waiter.RecordObservedEventAndNotifyWaiters(toolStarted);

        var matched = await waitTask;

        Assert.Same(toolStarted, matched);
        Assert.Equal(0, waiter.WaitingRegistrationCount);
    }

    [Fact]
    public async Task WaitForEventAsync_WhenTimeout_ReturnsNullAndRemovesRegistration()
    {
        var waiter = new ConversationEventWaiter();

        var matched = await waiter.WaitForEventAsync(
            streamEvent => streamEvent.Kind == ControlPlaneConversationStreamEventKind.Error,
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.Null(matched);
        Assert.Equal(0, waiter.WaitingRegistrationCount);
    }

    [Fact]
    public async Task WaitForIdleAsync_WhenRefreshMakesIdle_ReturnsTrue()
    {
        var waiter = new ConversationEventWaiter();
        var refreshCount = 0;
        var idle = false;

        var completed = await waiter.WaitForIdleAsync(
            _ =>
            {
                refreshCount++;
                idle = true;
                return Task.CompletedTask;
            },
            () => idle,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(completed);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task WaitForIdleAsync_WhenTimeout_ReturnsLatestIdleState()
    {
        var waiter = new ConversationEventWaiter();

        var completed = await waiter.WaitForIdleAsync(
            static _ => Task.CompletedTask,
            static () => false,
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(completed);
    }

    [Fact]
    public async Task Reset_ClearsObservedEventsAndRegistrationsWithoutCompletingWaiters()
    {
        var waiter = new ConversationEventWaiter();
        waiter.RecordObservedEventAndNotifyWaiters(Event(ControlPlaneConversationStreamEventKind.TurnStarted, "turn-3"));

        var waitTask = waiter.WaitForEventAsync(
            streamEvent => streamEvent.Kind == ControlPlaneConversationStreamEventKind.ToolCallStarted,
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None);

        Assert.Equal(1, waiter.ObservedEventCount);
        Assert.Equal(1, waiter.WaitingRegistrationCount);

        waiter.Reset();

        Assert.Equal(0, waiter.ObservedEventCount);
        Assert.Equal(0, waiter.WaitingRegistrationCount);
        Assert.Null(await waitTask);
    }

    private static ControlPlaneConversationStreamEvent Event(
        ControlPlaneConversationStreamEventKind kind,
        string turnId)
        => new()
        {
            Kind = kind,
            ThreadId = new ThreadId("thread-1"),
            TurnId = new TurnId(turnId),
        };
}
