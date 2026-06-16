using System.Text.Json;
using TianShu.Cli.Interaction;
using TianShu.Cli.Interaction.Host;
using TianShu.Cli.Interaction.Recording;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Tests;

[Collection("ConsoleCapture")]
public sealed class ChatOutputWriterTests
{
    [Fact]
    public void Write_JsonlAssistantDelta_WritesPartialStdoutAndTranscript()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Jsonl, scriptMode: false);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            fixture.Writer.Write("hello");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("stdout", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("hello", document.RootElement.GetProperty("text").GetString());
        Assert.True(document.RootElement.GetProperty("partial").GetBoolean());
        var transcript = Assert.Single(fixture.Transcript);
        Assert.Equal("hello", transcript.Text);
        Assert.False(transcript.AppendNewLine);
        Assert.Equal("assistant_text", transcript.Kind);
    }

    [Fact]
    public void WriteLine_JsonlError_WritesStderrFrameAndMarksFailure()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Jsonl, scriptMode: false);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            fixture.Writer.WriteLine("bad", isError: true);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("stderr", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("bad", document.RootElement.GetProperty("text").GetString());
        Assert.False(document.RootElement.GetProperty("partial").GetBoolean());
        Assert.Equal(1, fixture.FailureCount);
        Assert.Equal("bad", fixture.LastFailureMessage);
    }

    [Fact]
    public void WriteLine_HumanScriptMode_WritesPlainConsoleLineWithoutTerminalFrame()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: true);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            fixture.Writer.WriteLine("script output");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("script output", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, fixture.TerminalHost.RetainedTextWrites);
        Assert.Equal(0, fixture.TerminalHost.PresentationBlockWrites);
    }

    [Fact]
    public void WriteLine_HumanInteractiveControlOutputScope_RendersAsciiBox()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope())
        {
            fixture.Writer.WriteLine("已删除待发送 #1。");
        }

        var retainedText = Assert.Single(fixture.TerminalHost.RetainedTexts);
        Assert.Contains("控制输出", retainedText, StringComparison.Ordinal);
        Assert.Contains("已删除待发送 #1。", retainedText, StringComparison.Ordinal);
        Assert.Contains("\u001b[", retainedText, StringComparison.Ordinal);
        Assert.Contains("+", retainedText, StringComparison.Ordinal);
        Assert.Contains("| ", retainedText, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteControlPlaneLine_WritesVisibleOutputWithoutPersistingTranscript()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        fixture.Writer.WriteControlPlaneLine("控制面状态：已刷新配置。");

        var retainedText = Assert.Single(fixture.TerminalHost.RetainedTexts);
        Assert.Contains("控制面状态：已刷新配置。", retainedText, StringComparison.Ordinal);
        Assert.Empty(fixture.Transcript);
    }

    [Fact]
    public void WriteErrorLineOnce_HumanInteractiveControlOutputScope_RendersAsciiBox()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope())
        {
            fixture.Writer.WriteErrorLineOnce("follow-up 执行失败：跟进发送已删除。");
        }

        var retainedText = Assert.Single(fixture.TerminalHost.RetainedTexts);
        Assert.Contains("控制输出", retainedText, StringComparison.Ordinal);
        Assert.Contains("follow-up 执行失败：跟进发送已删除。", retainedText, StringComparison.Ordinal);
        Assert.Contains("+", retainedText, StringComparison.Ordinal);
        Assert.Contains("| ", retainedText, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteLine_HumanInteractiveBufferedControlOutputScope_GroupsLinesInOneBox()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            fixture.Writer.WriteLine("follow-up 执行失败：跟进发送已删除。", isError: true);
            fixture.Writer.WriteLine("follow-up 提交失败：运行时未接收这条内容。", isError: true);
            fixture.Writer.WriteLine("已删除待发送 #1。");
        }

        var retainedText = Assert.Single(fixture.TerminalHost.RetainedTexts);
        Assert.StartsWith(Environment.NewLine, retainedText, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine + Environment.NewLine, retainedText, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(retainedText, "控制输出"));
        Assert.Contains("follow-up 执行失败：跟进发送已删除。", retainedText, StringComparison.Ordinal);
        Assert.Contains("follow-up 提交失败：运行时未接收这条内容。", retainedText, StringComparison.Ordinal);
        Assert.Contains("已删除待发送 #1。", retainedText, StringComparison.Ordinal);
        Assert.Contains("\u001b[31m", retainedText, StringComparison.Ordinal);
        Assert.Contains("\u001b[38;2;135;206;250m", retainedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeginControlOutputScope_WhenBufferedScopesOverlapAcrossTasks_GroupsLinesInOneBox()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            Task backgroundScopeTask;
            using (ExecutionContext.SuppressFlow())
            {
                backgroundScopeTask = Task.Run(() =>
                {
                    using (fixture.Writer.BeginControlOutputScope(buffered: true))
                    {
                        fixture.Writer.WriteLine("follow-up 执行失败：跟进发送已删除。", isError: true);
                        fixture.Writer.WriteLine("follow-up 提交失败：运行时未接收这条内容。", isError: true);
                    }
                });
            }

            await backgroundScopeTask;
            fixture.Writer.WriteLine("已删除待发送 #1。");
        }

        var retainedText = Assert.Single(fixture.TerminalHost.RetainedTexts);
        Assert.Equal(1, CountOccurrences(retainedText, "控制输出"));
        Assert.Contains("follow-up 执行失败：跟进发送已删除。", retainedText, StringComparison.Ordinal);
        Assert.Contains("follow-up 提交失败：运行时未接收这条内容。", retainedText, StringComparison.Ordinal);
        Assert.Contains("已删除待发送 #1。", retainedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteDisplayBlock_HumanInteractiveBufferedControlOutputScope_QueuesExternalOutputUntilControlBoxFlush()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            fixture.Writer.WriteLine("follow-up 执行失败：跟进发送已删除。", isError: true);
            Task displayBlockTask;
            using (ExecutionContext.SuppressFlow())
            {
                displayBlockTask = Task.Run(
                    () => fixture.Writer.WriteDisplayBlock(
                        new ToolInvocationBlock(
                            "执行命令",
                            "Get-Location",
                            "已完成",
                            ToolPresentationStatus.Completed)));
            }

            await displayBlockTask;
            fixture.Writer.WriteLine("follow-up 提交失败：运行时未接收这条内容。", isError: true);
        }

        Assert.Equal(3, fixture.TerminalHost.OutputEvents.Count);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Contains("follow-up 执行失败：跟进发送已删除。", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Contains("follow-up 提交失败：运行时未接收这条内容。", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Equal("presentation:ToolInvocationBlock:False", fixture.TerminalHost.OutputEvents[1]);
        Assert.Equal("refresh", fixture.TerminalHost.OutputEvents[2]);
    }

    [Fact]
    public async Task WriteDisplayBlock_HumanInteractiveNonBufferedControlOutputScope_DoesNotQueueExternalOutput()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope())
        {
            Task displayBlockTask;
            using (ExecutionContext.SuppressFlow())
            {
                displayBlockTask = Task.Run(
                    () => fixture.Writer.WriteDisplayBlock(
                        new ToolInvocationBlock(
                            "执行命令",
                            "Get-Location",
                            "已完成",
                            ToolPresentationStatus.Completed)));
            }

            await displayBlockTask;
            fixture.Writer.WriteLine("已执行 slash 命令。");
        }

        Assert.Equal(2, fixture.TerminalHost.OutputEvents.Count);
        Assert.Equal("presentation:ToolInvocationBlock:False", fixture.TerminalHost.OutputEvents[0]);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[1], StringComparison.Ordinal);
        Assert.Contains("已执行 slash 命令。", fixture.TerminalHost.OutputEvents[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteDisplayBlock_HumanInteractiveBufferedControlOutputScopeWithoutExternalQueue_DoesNotQueueExternalOutput()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true, queueExternalOutput: false))
        {
            fixture.Writer.WriteLine("第一行控制输出。");
            Task displayBlockTask;
            using (ExecutionContext.SuppressFlow())
            {
                displayBlockTask = Task.Run(
                    () => fixture.Writer.WriteDisplayBlock(
                        new ToolInvocationBlock(
                            "执行命令",
                            "Get-Location",
                            "已完成",
                            ToolPresentationStatus.Completed)));
            }

            await displayBlockTask;
            fixture.Writer.WriteLine("第二行控制输出。");
        }

        Assert.Equal(3, fixture.TerminalHost.OutputEvents.Count);
        Assert.Equal("presentation:ToolInvocationBlock:False", fixture.TerminalHost.OutputEvents[0]);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[1], StringComparison.Ordinal);
        Assert.Contains("第一行控制输出。", fixture.TerminalHost.OutputEvents[1], StringComparison.Ordinal);
        Assert.Contains("第二行控制输出。", fixture.TerminalHost.OutputEvents[1], StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(fixture.TerminalHost.OutputEvents[1], " 控制输出 "));
        Assert.Equal("refresh", fixture.TerminalHost.OutputEvents[2]);
    }

    [Fact]
    public void WriteDisplayBlock_HumanInteractiveBufferedControlOutputScope_KeepsToolBlockOutOfControlBox()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            fixture.Writer.WriteLine("follow-up 执行失败：跟进发送已删除。", isError: true);
            fixture.Writer.WriteDisplayBlock(
                new ToolInvocationBlock(
                    "执行命令",
                    "Get-Location",
                    "已完成",
                    ToolPresentationStatus.Completed));
            fixture.Writer.WriteLine("follow-up 提交失败：运行时未接收这条内容。", isError: true);
        }

        Assert.Equal(3, fixture.TerminalHost.OutputEvents.Count);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Contains("follow-up 执行失败：跟进发送已删除。", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Location", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Equal("presentation:ToolInvocationBlock:False", fixture.TerminalHost.OutputEvents[1]);
        Assert.Equal("refresh", fixture.TerminalHost.OutputEvents[2]);
    }

    [Fact]
    public void WriteCommittedAssistantBlock_HumanInteractiveBufferedControlOutputScope_QueuesAssistantBlockUntilControlBoxFlush()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            fixture.Writer.WriteLine("已提交中途引导。");
            fixture.Writer.WriteCommittedAssistantBlock(new AssistantMessageBlock("assistant 正文", IsComplete: true));
        }

        Assert.Equal(3, fixture.TerminalHost.OutputEvents.Count);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Contains("已提交中途引导。", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Equal("presentation:AssistantMessageBlock:False", fixture.TerminalHost.OutputEvents[1]);
        Assert.Equal("refresh", fixture.TerminalHost.OutputEvents[2]);
    }

    [Fact]
    public void BufferedControlOutputScope_FlushesQueuedBlocksBeforeRefreshingPrompt()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        using (fixture.Writer.BeginControlOutputScope(buffered: true))
        {
            fixture.Writer.WriteLine("已删除待发送 #1。");
            fixture.Writer.WriteCommittedAssistantBlock(new AssistantMessageBlock("继续推进。", IsComplete: true));
            fixture.Writer.WriteDisplayBlock(
                new ToolInvocationBlock(
                    "执行命令",
                    "Get-Location",
                    "已完成",
                    ToolPresentationStatus.Completed));
        }

        Assert.Equal(4, fixture.TerminalHost.OutputEvents.Count);
        Assert.StartsWith("retained:", fixture.TerminalHost.OutputEvents[0], StringComparison.Ordinal);
        Assert.Equal("presentation:AssistantMessageBlock:False", fixture.TerminalHost.OutputEvents[1]);
        Assert.Equal("presentation:ToolInvocationBlock:False", fixture.TerminalHost.OutputEvents[2]);
        Assert.Equal("refresh", fixture.TerminalHost.OutputEvents[3]);
    }

    [Fact]
    public void WriteLine_HumanScriptModeControlOutputScope_KeepsPlainConsoleOutput()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: true);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            using (fixture.Writer.BeginControlOutputScope())
            {
                fixture.Writer.WriteLine("script output");
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal("script output" + Environment.NewLine, writer.ToString());
        Assert.Empty(fixture.TerminalHost.RetainedTexts);
    }

    [Fact]
    public void Write_HumanInteractive_RendersAssistantRetainedFrame()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            fixture.Writer.Write("streaming text");
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(1, fixture.TerminalHost.RenderAssistantRetainedTailFrameCalls);
        Assert.Empty(writer.ToString());
    }

    [Fact]
    public void WriteProjectionCommittedBlocks_HumanToolBlock_SetsAssistantSpacer()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);

        fixture.Writer.WriteProjectionCommittedBlocks([
            new ToolInvocationBlock(
                "执行命令",
                "Get-Location",
                "已完成",
                ToolPresentationStatus.Completed),
        ]);

        Assert.True(fixture.AssistantLeadingSpacerPending);
        Assert.Equal(1, fixture.TerminalHost.PresentationBlockWrites);
    }

    [Fact]
    public void WriteCommittedAssistantBlock_SetsLastTextAndClosesAssistantLine()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);
        fixture.AssistantLineOpen = true;

        fixture.Writer.WriteCommittedAssistantBlock(new AssistantMessageBlock("完成总结。", IsComplete: true));

        Assert.Equal("完成总结。", fixture.LastCompletedAssistantText);
        Assert.False(fixture.AssistantLineOpen);
        Assert.Equal(1, fixture.TerminalHost.PresentationBlockWrites);
        Assert.Equal(1, fixture.TerminalHost.RefreshCalls);
    }

    [Fact]
    public void WriteDisplayBlock_HumanInteractive_UsesFrameCoordinatorTransaction()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);
        fixture.TerminalHost.PrepareInlineTailPromptWriteReturns = true;
        fixture.TerminalHost.RecordFrameLifecycleEvents = true;

        fixture.Writer.WriteDisplayBlock(
            new ToolInvocationBlock(
                "执行命令",
                "Get-Location",
                "已完成",
                ToolPresentationStatus.Completed));

        Assert.Equal(
            [
                "prepare",
                "presentation:ToolInvocationBlock:False",
                "restore",
            ],
            fixture.TerminalHost.OutputEvents);
    }

    [Fact]
    public void BeginExclusiveTerminalFrameScope_HumanInteractive_ClearsAndRestoresFinalDock()
    {
        var fixture = ChatOutputWriterFixture.Create(ChatOutputProtocol.Human, scriptMode: false);
        fixture.TerminalHost.RecordFrameLifecycleEvents = true;

        using (fixture.Writer.BeginExclusiveTerminalFrameScope())
        {
            fixture.TerminalHost.OutputEvents.Add("overlay");
        }

        Assert.Equal(
            [
                "prepare",
                "overlay",
                "refresh",
            ],
            fixture.TerminalHost.OutputEvents);
    }

    private sealed class ChatOutputWriterFixture
    {
        private ChatOutputWriterFixture(ChatOutputProtocol outputProtocol, bool scriptMode)
        {
            OutputProtocol = outputProtocol;
            ScriptMode = scriptMode;
            TerminalHost = new FakeChatOutputTerminalHost();
            Writer = new ChatOutputWriter(new ChatOutputWriterContext
            {
                ConsoleGate = new object(),
                TerminalHost = TerminalHost,
                GetPresentationPipeline = () => Pipeline,
                GetOutputProtocol = () => OutputProtocol,
                IsScriptMode = () => ScriptMode,
                GetCurrentPlanDockSummary = () => null,
                AppendTranscript = (text, appendNewLine, kind, isError) =>
                    Transcript.Add(CliTranscriptRecord.Create(kind, text, appendNewLine, isError)),
                MarkFailure = () => FailureCount++,
                SetLastFailureMessage = value => LastFailureMessage = value,
                GetAssistantLineOpen = () => AssistantLineOpen,
                SetAssistantLineOpen = value => AssistantLineOpen = value,
                SetLastCompletedAssistantText = value => LastCompletedAssistantText = value,
                SetAssistantLeadingSpacerPending = value => AssistantLeadingSpacerPending = value,
            });
        }

        public ChatOutputWriter Writer { get; }

        public FakeChatOutputTerminalHost TerminalHost { get; }

        public List<CliTranscriptRecord> Transcript { get; } = [];

        public int FailureCount { get; private set; }

        public string? LastFailureMessage { get; private set; }

        public string? LastCompletedAssistantText { get; private set; }

        public bool AssistantLineOpen { get; set; }

        public bool AssistantLeadingSpacerPending { get; private set; }

        private ChatOutputProtocol OutputProtocol { get; }

        private bool ScriptMode { get; }

        private InteractionPipeline Pipeline { get; } = new();

        public static ChatOutputWriterFixture Create(ChatOutputProtocol outputProtocol, bool scriptMode)
            => new(outputProtocol, scriptMode);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private sealed class FakeChatOutputTerminalHost : IChatOutputTerminalHost
    {
        public int RenderAssistantRetainedTailFrameCalls { get; private set; }

        public int RetainedTextWrites { get; private set; }

        public List<string> RetainedTexts { get; } = [];

        public List<string> OutputEvents { get; } = [];

        public int PresentationBlockWrites { get; private set; }

        public int RefreshCalls { get; private set; }

        public bool PrepareInlineTailPromptWriteReturns { get; set; }

        public bool RecordFrameLifecycleEvents { get; set; }

        public TerminalFrameBuildContext BuildTerminalFrameContextFromDock(
            ComposerDockState dockState,
            bool includePopupLines,
            bool styled)
            => new(
                dockState.InputText,
                dockState.Cursor,
                dockState.Prompt,
                dockState.Agents,
                dockState.Model,
                dockState.IsBusy,
                dockState.WorkingElapsed,
                dockState.QueuedFollowUps,
                dockState.InputNotice,
                dockState.CommandOverlay,
                includePopupLines ? [] : null,
                Width: 100,
                Styled: styled);

        public ComposerDockState BuildCurrentComposerDockState()
            => new(
                string.Empty,
                0,
                "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: new ModelDockSummary("test-model"),
                IsBusy: false,
                CommandOverlay: null);

        public bool PrepareInlineTailPromptWrite(bool assistantLineOpen)
        {
            if (RecordFrameLifecycleEvents)
            {
                OutputEvents.Add("prepare");
            }

            return PrepareInlineTailPromptWriteReturns;
        }

        public void RestoreInlineTailPrompt()
        {
            if (RecordFrameLifecycleEvents)
            {
                OutputEvents.Add("restore");
            }
        }

        public void RenderAssistantRetainedTailFrameUnsafe()
        {
            RenderAssistantRetainedTailFrameCalls++;
            OutputEvents.Add("assistant-frame");
        }

        public void RefreshAndRestoreInlineTailPrompt()
        {
            RefreshCalls++;
            OutputEvents.Add("refresh");
        }

        public IDisposable BeginCommandOverlay(Action? onEscape = null)
            => new DisposeAction(() => { });

        public void SetCommandOverlayLines(IReadOnlyList<string> lines)
        {
        }

        public void WriteHumanTerminalRetainedText(string text, bool isError)
        {
            RetainedTextWrites++;
            RetainedTexts.Add(text);
            OutputEvents.Add("retained:" + text);
        }

        public void WriteHumanTerminalPresentationBlock(ChatPresentationBlock block, bool isError)
        {
            PresentationBlockWrites++;
            OutputEvents.Add($"presentation:{block.GetType().Name}:{isError}");
        }
    }

    private sealed class DisposeAction(Action dispose) : IDisposable
    {
        public void Dispose()
            => dispose();
    }
}
