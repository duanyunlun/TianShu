using TianShu.Cli.Interaction.Rendering;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class ModelStatusTableRendererTests
{
    [Fact]
    public void BuildHeader_PlainText_ContainsStableColumns()
    {
        var renderer = new ModelStatusTableRenderer();

        var header = renderer.BuildHeader(styled: false);

        Assert.Contains("序号", header, StringComparison.Ordinal);
        Assert.Contains("模型", header, StringComparison.Ordinal);
        Assert.Contains("协议", header, StringComparison.Ordinal);
        Assert.Contains("测试结果", header, StringComparison.Ordinal);
        Assert.Contains("思考", header, StringComparison.Ordinal);
        Assert.Contains("耗时", header, StringComparison.Ordinal);
        Assert.Contains("报错", header, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRow_PadsAndTruncatesWideCharacters()
    {
        var renderer = new ModelStatusTableRenderer(static () => 96, static () => true);

        var row = renderer.BuildRow(
            12,
            "deepseek-chat-with-a-very-long-display-name",
            "anthropic_messages",
            ModelStatusProbeOutcome.Succeeded,
            "可见",
            TimeSpan.FromMilliseconds(1250),
            "中文错误详情中文错误详情中文错误详情中文错误详情中文错误详情",
            styled: false);

        Assert.StartsWith("12", row, StringComparison.Ordinal);
        Assert.Contains("anthropic_messages", row, StringComparison.Ordinal);
        Assert.Contains("成功", row, StringComparison.Ordinal);
        Assert.Contains("可见", row, StringComparison.Ordinal);
        Assert.Contains("1250ms", row, StringComparison.Ordinal);
        Assert.True(TerminalLayoutCalculator.GetDisplayWidth(row) < 210);
    }

    [Fact]
    public void BuildRow_StyledOutput_UsesOutcomeAndReasoningColors()
    {
        var renderer = new ModelStatusTableRenderer(static () => 120, static () => false);

        var row = renderer.BuildRow(
            1,
            "claude-opus",
            "anthropic_messages",
            ModelStatusProbeOutcome.Running,
            "检测中",
            TimeSpan.FromMilliseconds(500),
            null,
            styled: true);

        Assert.Contains("\u001b[", row, StringComparison.Ordinal);
        Assert.Contains("claude-opus", row, StringComparison.Ordinal);
        Assert.Contains("测试中", row, StringComparison.Ordinal);
        Assert.Contains("检测中", row, StringComparison.Ordinal);
    }

    [Fact]
    public void FitTerminalRow_TruncatesStyledRowsBeforeTerminalAutoWrap()
    {
        var renderer = new ModelStatusTableRenderer(static () => 30, static () => false);
        var row = TerminalAnsi.BlueText("模型名称很长很长很长") + " " + TerminalAnsi.RedText("错误详情很长很长很长");

        var fitted = renderer.FitTerminalRow(row, styled: true);

        Assert.True(TerminalLayoutCalculator.GetDisplayWidth(fitted) <= 29);
        Assert.EndsWith(TerminalAnsi.Reset, fitted, StringComparison.Ordinal);
    }
}
