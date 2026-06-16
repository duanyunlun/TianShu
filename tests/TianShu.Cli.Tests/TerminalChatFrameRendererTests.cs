using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalChatFrameRendererTests
{
    [Fact]
    public void RenderLines_PlacesDockAtFrameTail()
    {
        var frame = new TerminalChatFrame(
            ["• assistant text", "● 执行命令"],
            new ComposerDockState(
                InputText: string.Empty,
                Cursor: 0,
                Prompt: "> ",
                Plan: new PlanDockSummary(1, 2, "编写界面"),
                Agents: new AgentDockSummary(0),
                Model: new ModelDockSummary("deepseek-chat"),
                IsBusy: true),
            PopupLines: null,
            Width: 80);

        var lines = new TerminalChatFrameRenderer().RenderLines(frame, styled: false);

        Assert.Equal("• assistant text", lines[0].TrimEnd());
        Assert.Equal("● 执行命令", lines[1].TrimEnd());
        Assert.Equal(string.Empty, lines[2]);
        Assert.Equal(string.Empty, lines[3]);
        Assert.StartsWith("> ", lines[^3], StringComparison.Ordinal);
        Assert.StartsWith("──", lines[^2], StringComparison.Ordinal);
        Assert.Contains("• Working...", lines[^1], StringComparison.Ordinal);
        Assert.Contains("Plan 1/2", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderLines_FitsTranscriptAndDockToWidth()
    {
        var frame = new TerminalChatFrame(
            ["using System.Windows; using System.Windows.Controls; using System.Windows.Input;"],
            new ComposerDockState(
                InputText: "/status",
                Cursor: 7,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: new ModelDockSummary("gpt-5.4"),
                IsBusy: false),
            PopupLines: ["  /status  查看当前状态"],
            Width: 24);

        var lines = new TerminalChatFrameRenderer().RenderLines(frame, styled: false);

        Assert.All(lines, line => Assert.True(TerminalLayoutCalculator.GetDisplayWidth(line) <= 24));
        Assert.StartsWith("using System.Windows;", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("> /status", lines[^4], StringComparison.Ordinal);
        Assert.StartsWith("──", lines[^3], StringComparison.Ordinal);
        Assert.StartsWith("  /status", lines[^2], StringComparison.Ordinal);
        Assert.Contains("Idle", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void RenderText_UsesSingleFrameOrder()
    {
        var frame = new TerminalChatFrame(
            ["first", "second"],
            new ComposerDockState(
                InputText: "hello",
                Cursor: 5,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(1),
                Model: new ModelDockSummary("model-a"),
                IsBusy: false),
            PopupLines: null,
            Width: 80);

        var text = new TerminalChatFrameRenderer().RenderText(frame, styled: false);

        Assert.Matches("first\\s*\\r?\\nsecond\\s*\\r?\\n> hello", text);
        Assert.Contains("Agents: 1 running", text, StringComparison.Ordinal);
        Assert.EndsWith("Model: model-a", text.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateDockInputLineIndex_CountsWrappedTranscriptAndDockSpacer()
    {
        var frame = new TerminalChatFrame(
            ["abcdefghijklmnopqrstuvwxyz"],
            new ComposerDockState(
                InputText: "draft",
                Cursor: 5,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: null,
                IsBusy: false),
            PopupLines: ["  /help"],
            Width: 10);

        var index = new TerminalChatFrameRenderer().CalculateDockInputLineIndex(frame);

        Assert.Equal(5, index);
    }

    [Fact]
    public void RenderTranscriptLines_DoesNotIncludeDockLines()
    {
        var frame = new TerminalChatFrame(
            ["tool output that should be clipped before it reaches the terminal"],
            new ComposerDockState(
                InputText: "draft",
                Cursor: 5,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: new ModelDockSummary("model-a"),
                IsBusy: true),
            PopupLines: null,
            Width: 18);

        var lines = new TerminalChatFrameRenderer().RenderTranscriptLines(frame);

        Assert.Equal(
            ["tool output that s", "hould be clipped b", "efore it reaches t", "he terminal"],
            lines);
        Assert.All(lines, line => Assert.True(TerminalLayoutCalculator.GetDisplayWidth(line) <= 18));
        Assert.All(lines, line => Assert.DoesNotContain("Working", line, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain("> draft", line, StringComparison.Ordinal));
    }

    [Fact]
    public void RenderTranscriptLines_WrapsLongTextInsteadOfClipping()
    {
        var frame = new TerminalChatFrame(
            ["abcdefghijklmnopqrstuvwxyz"],
            new ComposerDockState(
                InputText: string.Empty,
                Cursor: 0,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: null,
                IsBusy: false),
            PopupLines: null,
            Width: 10);

        var lines = new TerminalChatFrameRenderer().RenderTranscriptLines(frame);

        Assert.Equal(["abcdefghij", "klmnopqrst", "uvwxyz"], lines);
    }

    [Fact]
    public void RenderLines_WhenStyled_ResetsAnsiBeforeDockLines()
    {
        var frame = new TerminalChatFrame(
            [TerminalAnsi.Red + "assistant text without local reset"],
            new ComposerDockState(
                InputText: string.Empty,
                Cursor: 0,
                Prompt: "> ",
                Plan: null,
                Agents: new AgentDockSummary(0),
                Model: null,
                IsBusy: false),
            PopupLines: null,
            Width: 80);

        var lines = new TerminalChatFrameRenderer().RenderLines(frame, styled: true);

        Assert.All(lines, line => Assert.StartsWith(TerminalAnsi.Reset, line, StringComparison.Ordinal));
        Assert.StartsWith(TerminalAnsi.Reset + "> ", lines[^3], StringComparison.Ordinal);
        Assert.EndsWith(TerminalAnsi.Reset, lines[^3], StringComparison.Ordinal);
    }
}
