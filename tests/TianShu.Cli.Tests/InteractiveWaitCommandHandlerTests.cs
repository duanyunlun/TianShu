using TianShu.Cli.Interaction.Commands.Wait;
using TianShu.Cli.Interaction.Orchestration;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Primitives;

namespace TianShu.Cli.Tests;

public sealed class InteractiveWaitCommandHandlerTests
{
    [Fact]
    public async Task HandleWaitAsync_WhenMillisecondsInvalid_WritesUsage()
    {
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleWaitAsync("-1", (text, isError) => output.Add((text, isError)), CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "用法：/wait [milliseconds]");
    }

    [Fact]
    public async Task HandleWaitEventAsync_WhenObservedMatchingEventExists_WritesFormattedEvent()
    {
        var waiter = new ConversationEventWaiter();
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        waiter.RecordObservedEventAndNotifyWaiters(Event(ControlPlaneConversationStreamEventKind.TurnCompleted));

        await handler.HandleWaitEventAsync(
            waiter,
            "turn-completed 1",
            static streamEvent => $"formatted:{streamEvent.Kind}",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => !line.IsError && line.Text == "已等到事件：formatted:TurnCompleted");
    }

    [Fact]
    public async Task HandleWaitEventAsync_WhenEventKindUnknown_WritesUsage()
    {
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleWaitEventAsync(
            new ConversationEventWaiter(),
            "no-such-event",
            static streamEvent => streamEvent.Kind.ToString(),
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "用法：/wait-event <event-kind> [timeout-seconds]");
    }

    [Fact]
    public async Task HandleWaitNextToolCallAsync_IgnoresApprovalPhaseToolCall()
    {
        var waiter = new ConversationEventWaiter();
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        waiter.RecordObservedEventAndNotifyWaiters(ToolCallStarted("request_approval"));
        waiter.RecordObservedEventAndNotifyWaiters(ToolCallStarted("call"));

        await handler.HandleWaitNextToolCallAsync(
            waiter,
            "1",
            static streamEvent => $"tool:{streamEvent.Payload!.Properties["phase"].StringValue}",
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => !line.IsError && line.Text == "已等到事件：tool:call");
    }

    [Fact]
    public async Task HandleWaitCompleteAsync_WhenIdleAfterRefresh_WritesCompleted()
    {
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();
        var idle = false;

        await handler.HandleWaitCompleteAsync(
            new ConversationEventWaiter(),
            "1",
            _ =>
            {
                idle = true;
                return Task.CompletedTask;
            },
            () => idle,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => !line.IsError && line.Text == "当前没有运行中的回合。");
    }

    [Fact]
    public async Task HandleWaitCompleteAsync_WhenTimeout_WritesTimeout()
    {
        var handler = new InteractiveWaitCommandHandler();
        var output = new List<(string Text, bool IsError)>();

        await handler.HandleWaitCompleteAsync(
            new ConversationEventWaiter(),
            "1",
            static _ => Task.CompletedTask,
            static () => false,
            (text, isError) => output.Add((text, isError)),
            CancellationToken.None);

        Assert.Contains(output, static line => line.IsError && line.Text == "等待回合结束超时。");
    }

    private static ControlPlaneConversationStreamEvent Event(ControlPlaneConversationStreamEventKind kind)
        => new()
        {
            Kind = kind,
            ThreadId = new ThreadId("thread_1"),
            TurnId = new TurnId("turn_1"),
        };

    private static ControlPlaneConversationStreamEvent ToolCallStarted(string phase)
        => new()
        {
            Kind = ControlPlaneConversationStreamEventKind.ToolCallStarted,
            PayloadKind = ControlPlaneConversationStreamPayloadKind.ToolCall,
            Payload = StructuredValueTestHelper.FromJson($$"""{"phase":"{{phase}}"}"""),
            ThreadId = new ThreadId("thread_1"),
            TurnId = new TurnId("turn_1"),
        };
}
