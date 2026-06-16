using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli.Tests;

[Collection("ConsoleCapture")]
public sealed class TerminalInteractionHostTests
{
    [Fact]
    public void ShouldShowWaitingPlaceholder_SkipsSlashAndShellCommands()
    {
        Assert.True(TerminalInteractionHost.ShouldShowWaitingPlaceholder("创建 WPF 项目"));
        Assert.False(TerminalInteractionHost.ShouldShowWaitingPlaceholder("/help"));
        Assert.False(TerminalInteractionHost.ShouldShowWaitingPlaceholder("!Get-Location"));
        Assert.False(TerminalInteractionHost.ShouldShowWaitingPlaceholder("   /model-route status"));
    }

    [Fact]
    public void RenderPrompt_StoresComposerDockStateWithTypedStatus()
    {
        var plan = new PlanDockSummary(1, 3, "编写界面");
        var host = CreateHost(plan, isBusy: true, model: "deepseek-chat", workingElapsed: TimeSpan.FromSeconds(12));
        var composer = new TerminalChatComposer();
        composer.SetText("继续");
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            host.RenderPrompt(composer, new TerminalPromptRenderer(), "> ");
            var firstRender = writer.ToString();
            Assert.StartsWith("\r\u001b[2K", firstRender, StringComparison.Ordinal);
            Assert.Contains(
                "\n\u001b[2K" + TerminalAnsi.Reset + "\n\u001b[2K" + TerminalAnsi.Reset + "> 继续",
                firstRender,
                StringComparison.Ordinal);
            Assert.Contains("继续", firstRender, StringComparison.Ordinal);
            Assert.Contains("────────────────", firstRender, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            host.RefreshAndRestoreInlineTailPrompt();

            Assert.Contains("────────────────", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            host.Dispose();
        }

        var dock = host.BuildCurrentComposerDockState();
        Assert.Equal("继续", dock.InputText);
        Assert.Equal(2, dock.Cursor);
        Assert.True(dock.IsBusy);
        Assert.Equal(TimeSpan.FromSeconds(12), dock.WorkingElapsed);
        Assert.Equal("编写界面", dock.Plan?.CurrentStep);
        Assert.Equal("deepseek-chat", dock.Model?.Model);
    }

    [Fact]
    public void RenderPrompt_IncludesInputNoticeInCurrentDockFrame()
    {
        const string notice = "将永久删除全部线程及全部会话日志，包括当前会话。请输入 DELETE ALL THREADS 确认：";
        var host = CreateHost(plan: null, isBusy: false, model: "deepseek-chat", workingElapsed: null, inputNotice: notice);
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            host.RenderPrompt(new TerminalChatComposer(), new TerminalPromptRenderer(), "> ", placeholder: "请输入确认文本");
        }
        finally
        {
            Console.SetOut(originalOut);
            host.Dispose();
        }

        var output = writer.ToString();
        Assert.Contains("确认提示：", output, StringComparison.Ordinal);
        Assert.Contains("DELETE ALL THREADS", output, StringComparison.Ordinal);
        Assert.Equal(notice, host.BuildCurrentComposerDockState().InputNotice);
    }

    [Fact]
    public void WriteHumanTerminalPresentationBlock_RendersThroughTerminalFrame()
    {
        var host = CreateHost(plan: null, isBusy: false, model: "gpt-5.4", workingElapsed: null);
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            host.WriteHumanTerminalPresentationBlock(new AssistantMessageBlock("完成了。", IsComplete: true), isError: false);
        }
        finally
        {
            Console.SetOut(originalOut);
            host.Dispose();
        }

        var output = writer.ToString();
        Assert.Contains("完成了。", output, StringComparison.Ordinal);
        Assert.Contains("• ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAndRestoreInlineTailPrompt_WhenAssistantTailActive_RendersTailFrame()
    {
        var pipeline = new InteractionPipeline();
        pipeline.ProjectStreamEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            Text = "正在继续生成。",
        });
        var host = CreateHost(
            plan: new PlanDockSummary(1, 2, "整理输出"),
            isBusy: true,
            model: "deepseek-chat",
            workingElapsed: TimeSpan.FromSeconds(8),
            pipeline: pipeline,
            queuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "待发送内容")]));
        var composer = new TerminalChatComposer();
        composer.SetText("/follow-up");
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            host.RenderPrompt(
                composer,
                new TerminalPromptRenderer(),
                "> ",
                ["  /follow-up drop    删除待发送项"]);
            writer.GetStringBuilder().Clear();

            host.RefreshAndRestoreInlineTailPrompt();
        }
        finally
        {
            Console.SetOut(originalOut);
            host.Dispose();
        }

        var output = writer.ToString();
        Assert.Contains("正在继续生成。", output, StringComparison.Ordinal);
        Assert.Contains("/follow-up drop", output, StringComparison.Ordinal);
        Assert.Contains("待发送内容", output, StringComparison.Ordinal);
        Assert.Contains("整理输出", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteInputLine_WhenAssistantTailFrameVisible_CommitsAssistantAndDropsSubmittedCommand()
    {
        var pipeline = new InteractionPipeline();
        pipeline.ProjectStreamEvent(new ControlPlaneConversationStreamEvent
        {
            Kind = ControlPlaneConversationStreamEventKind.AssistantTextDelta,
            Text = "我继续执行。",
        });
        var host = CreateHost(
            plan: null,
            isBusy: true,
            model: "deepseek-chat",
            workingElapsed: TimeSpan.FromSeconds(12),
            pipeline: pipeline,
            queuedFollowUps: new QueuedFollowUpDockState(1, [new QueuedFollowUpDockEntryState(1, "待处理消息")]));
        var composer = new TerminalChatComposer();
        composer.SetText("/follow-up drop 1");
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            host.RenderPrompt(composer, new TerminalPromptRenderer(), "> ");
            writer.GetStringBuilder().Clear();

            host.CompleteInputLine(new TerminalPromptRenderer(), addVisualSpacerAfter: true, clearSubmittedInput: true);
        }
        finally
        {
            Console.SetOut(originalOut);
            host.Dispose();
        }

        var output = writer.ToString();
        Assert.Contains("我继续执行。", output, StringComparison.Ordinal);
        Assert.DoesNotContain("/follow-up drop 1", output, StringComparison.Ordinal);
        Assert.DoesNotContain("待处理消息", output, StringComparison.Ordinal);
    }

    private static TerminalInteractionHost CreateHost(
        PlanDockSummary? plan,
        bool isBusy,
        string model,
        TimeSpan? workingElapsed,
        InteractionPipeline? pipeline = null,
        QueuedFollowUpDockState? queuedFollowUps = null,
        string? inputNotice = null)
        => new(
            new object(),
            isHumanOutput: static () => true,
            isScriptMode: static () => false,
            isBusy: () => isBusy,
            getModelSummary: () => new ModelDockSummary(model),
            getPlan: () => plan,
            getQueuedFollowUps: () => queuedFollowUps,
            getInputNotice: () => inputNotice,
            getWorkingElapsed: () => workingElapsed,
            getPipeline: () => pipeline ?? new InteractionPipeline(),
            shouldSkipWorkingDockRefresh: static () => false,
            hideCursorForRefresh: static () => NoopDisposable.Instance);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
