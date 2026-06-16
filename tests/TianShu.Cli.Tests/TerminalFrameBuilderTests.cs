using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Projection;
using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalFrameBuilderTests
{
    [Fact]
    public void Build_IncludesCommittedBlocksActiveAssistantAndDock()
    {
        var state = new ChatPresentationState(
            [
                new AssistantMessageBlock("我先确认目录。", IsComplete: true),
                new ToolInvocationBlock("执行命令", "Get-Location", "完成，退出码 0", ToolPresentationStatus.Completed),
            ],
            ActiveAssistantText: "接下来创建项目。",
            Plan: new PlanDockSummary(1, 2, "创建项目"),
            Output: ConversationOutputModel.FromBlocks(
                [
                    new AssistantMessageBlock("我先确认目录。", IsComplete: true),
                    new ToolInvocationBlock("执行命令", "Get-Location", "完成，退出码 0", ToolPresentationStatus.Completed),
                ]));
        var frame = new TerminalFrameBuilder().Build(
            state,
            Context(input: "继续", cursor: 2, busy: true));

        Assert.Equal(
            ["我先确认目录。", "● 执行命令", "  Get-Location", "  ✓ 完成，退出码 0", "接下来创建项目。"],
            frame.TranscriptLines);
        Assert.Equal("继续", frame.Dock.InputText);
        Assert.Equal(2, frame.Dock.Cursor);
        Assert.True(frame.Dock.IsBusy);
        Assert.Equal("创建项目", frame.Dock.Plan?.CurrentStep);
        Assert.Equal(80, frame.Width);
    }

    [Fact]
    public void Build_ExpandsAssistantMarkdownBeforeRendererWrapsFrame()
    {
        var state = new ChatPresentationState(
            [new AssistantMessageBlock("## 结果**文件：** `MainWindow.xaml` **状态：** 已完成", IsComplete: true)],
            ActiveAssistantText: string.Empty,
            Plan: null,
            Output: ConversationOutputModel.FromBlocks([new AssistantMessageBlock("## 结果**文件：** `MainWindow.xaml` **状态：** 已完成", IsComplete: true)]));

        var frame = new TerminalFrameBuilder().Build(state, Context(styled: false));

        Assert.Equal(
            ["结果", "  文件： MainWindow.xaml", "  状态： 已完成"],
            frame.TranscriptLines);
    }

    [Fact]
    public void Build_WhenStyled_KeepsAssistantMarkerAfterCommit()
    {
        var state = new ChatPresentationState(
            [new AssistantMessageBlock("先检查一下可用的 .NET SDK 版本。", IsComplete: true)],
            ActiveAssistantText: "接下来创建项目。",
            Plan: null,
            Output: ConversationOutputModel.FromBlocks([new AssistantMessageBlock("先检查一下可用的 .NET SDK 版本。", IsComplete: true)]));

        var frame = new TerminalFrameBuilder().Build(state, Context(styled: true));

        Assert.Equal(
            TerminalAnsi.GreenText("• ") + "先检查一下可用的 .NET SDK 版本。",
            frame.TranscriptLines[0]);
        Assert.Equal(
            TerminalAnsi.GreenText("• ") + "接下来创建项目。",
            frame.TranscriptLines[1]);
    }

    [Fact]
    public void Build_RendersPlanProgressBlockThroughTranscriptRenderer()
    {
        var state = new ChatPresentationState(
            [
                new PlanProgressBlock(
                    Title: null,
                    CompletedCount: 1,
                    TotalCount: 3,
                    CurrentStep: "运行测试",
                    Steps:
                    [
                        new PlanProgressStep("1", "整理结构", PlanStepPresentationStatus.Completed, "completed"),
                        new PlanProgressStep("2", "运行测试", PlanStepPresentationStatus.Running, "in_progress"),
                        new PlanProgressStep("3", "提交代码", PlanStepPresentationStatus.Pending, "pending"),
                    ]),
            ],
            ActiveAssistantText: string.Empty,
            Plan: null,
            Output: ConversationOutputModel.FromBlocks(
                [
                    new PlanProgressBlock(
                        Title: null,
                        CompletedCount: 1,
                        TotalCount: 3,
                        CurrentStep: "运行测试",
                        Steps:
                        [
                            new PlanProgressStep("1", "整理结构", PlanStepPresentationStatus.Completed, "completed"),
                            new PlanProgressStep("2", "运行测试", PlanStepPresentationStatus.Running, "in_progress"),
                            new PlanProgressStep("3", "提交代码", PlanStepPresentationStatus.Pending, "pending"),
                        ]),
                ]));

        var frame = new TerminalFrameBuilder().Build(state, Context(styled: false));

        Assert.Equal(
            [
                "● 计划更新  1/3 完成",
                "  ✓ 1. 整理结构",
                "  ▶ 2. 运行测试",
                "  □ 3. 提交代码",
            ],
            frame.TranscriptLines);
    }

    [Fact]
    public void Build_WhenStyled_ProducesAnsiBoundariesThroughFrameRenderer()
    {
        var state = new ChatPresentationState(
            [new SystemNoticeBlock("provider returned，model overloaded")],
            ActiveAssistantText: string.Empty,
            Plan: null,
            Output: ConversationOutputModel.FromBlocks([new SystemNoticeBlock("provider returned，model overloaded")]));
        var frame = new TerminalFrameBuilder().Build(state, Context(styled: true));

        var lines = new TerminalChatFrameRenderer().RenderLines(frame, styled: true);

        Assert.Contains(lines, line => line.Contains(TerminalAnsi.Red, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith(TerminalAnsi.Reset, line, StringComparison.Ordinal));
        Assert.StartsWith(TerminalAnsi.Reset + "> ", lines[^3], StringComparison.Ordinal);
    }

    [Fact]
    public void AssistantRetainedTailFrameLimiter_LimitsLongActiveAssistantPreview()
    {
        var text = string.Join(Environment.NewLine, Enumerable.Range(1, 20).Select(index => $"- 第 {index} 行"));
        var frame = new TerminalFrameBuilder().Build(
            new ChatPresentationState(
                Blocks: [],
                ActiveAssistantText: text,
                Plan: null,
                Output: ConversationOutputModel.Empty),
            Context(styled: true));
        var renderer = new TerminalChatFrameRenderer();

        var limited = AssistantRetainedTailFrameLimiter.Limit(frame, renderer, maxTranscriptPhysicalLines: 6, styled: true);

        Assert.Equal(6, renderer.RenderTranscriptLines(limited).Count);
        Assert.StartsWith(TerminalAnsi.GreenText("• ") + TerminalAnsi.DimText("..."), limited.TranscriptLines[0], StringComparison.Ordinal);
        Assert.Contains("第 20 行", limited.TranscriptLines[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(limited.TranscriptLines, line => line.Contains("第 1 行", StringComparison.Ordinal));
    }

    [Fact]
    public void AssistantRetainedTailFrameLimiter_DoesNotTrimCommittedBlockFrame()
    {
        var text = string.Join(Environment.NewLine, Enumerable.Range(1, 20).Select(index => $"- 第 {index} 行"));
        var frame = new TerminalFrameBuilder().Build(
            new ChatPresentationState(
                Blocks: [new AssistantMessageBlock(text, IsComplete: true)],
                ActiveAssistantText: string.Empty,
                Plan: null,
                Output: ConversationOutputModel.FromBlocks([new AssistantMessageBlock(text, IsComplete: true)])),
            Context(styled: true));
        var renderer = new TerminalChatFrameRenderer();

        Assert.Equal(20, renderer.RenderTranscriptLines(frame).Count);
        Assert.Contains("第 1 行", frame.TranscriptLines[0], StringComparison.Ordinal);
        Assert.Contains("第 20 行", frame.TranscriptLines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateDockInputLineIndex_WhenQueuedFollowUpsExist_KeepsInputLineBeforeFooter()
    {
        var frame = new TerminalChatFrame(
            ["正在处理。"],
            new ComposerDockState(
                InputText: "/follow-up drop",
                Cursor: "/follow-up drop".Length,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: new ModelDockSummary("deepseek-chat"),
                IsBusy: true,
                QueuedFollowUps: new QueuedFollowUpDockState(
                    2,
                    [
                        new QueuedFollowUpDockEntryState(1, "第一条"),
                        new QueuedFollowUpDockEntryState(2, "第二条"),
                    ])),
            PopupLines: ["/follow-up drop"],
            Width: 100);

        var renderer = new TerminalChatFrameRenderer();

        Assert.Equal(3, renderer.CalculateDockInputLineIndex(frame));
    }

    private static TerminalFrameBuildContext Context(
        string input = "",
        int cursor = 0,
        bool busy = false,
        bool styled = false)
        => new(
            InputText: input,
            Cursor: cursor,
            Prompt: "> ",
            Agents: new AgentDockSummary(0),
            Model: new ModelDockSummary("deepseek-chat"),
            IsBusy: busy,
            WorkingElapsed: busy ? TimeSpan.FromSeconds(12) : null,
            QueuedFollowUps: null,
            InputNotice: null,
            CommandOverlay: null,
            PopupLines: null,
            Width: 80,
            Styled: styled);
}
