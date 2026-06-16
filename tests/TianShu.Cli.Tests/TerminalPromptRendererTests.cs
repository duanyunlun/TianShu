using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalPromptRendererTests
{
    [Fact]
    public void Render_WhenCursorIsInMiddle_ClearsLineAndMovesCursorBack()
    {
        var renderer = new TerminalPromptRenderer();
        var frame = new TerminalRenderFrame("> ", "hello", 2);

        var output = renderer.Render(frame);

        Assert.StartsWith("\r\u001b[2K" + TerminalAnsi.Reset + "> hello", output, StringComparison.Ordinal);
        Assert.EndsWith("\r\u001b[4C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenTextHasNewLines_RendersSeparatePromptLines()
    {
        var renderer = new TerminalPromptRenderer();
        var frame = new TerminalRenderFrame("> ", "line 1" + Environment.NewLine + "line 2", 14);

        var output = renderer.Render(frame);

        Assert.Contains("> line 1", output, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "  line 2", output, StringComparison.Ordinal);
        Assert.DoesNotContain("> line 1 line 2", output, StringComparison.Ordinal);
        Assert.EndsWith("\r\u001b[8C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenPopupShrinks_ClearsPreviouslyRenderedPopupLines()
    {
        var renderer = new TerminalPromptRenderer();

        var first = renderer.Render(new TerminalRenderFrame("> ", "/h", 2, ["  /help", "  /history"]));
        var second = renderer.Render(new TerminalRenderFrame("> ", "/he", 3, ["  /help"]));

        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "  /help", first, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K", second, StringComparison.Ordinal);
        Assert.Contains("\u001b[2A", second, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteLine_WhenPopupWasRendered_ClearsPopupAndAdvancesAfterPromptFrame()
    {
        var renderer = new TerminalPromptRenderer();
        renderer.Render(new TerminalRenderFrame("> ", "/h", 2, ["  /help"]));

        var output = renderer.CompleteLine();

        Assert.Contains("\u001b[0J", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\u001b[2K", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\u001b[1A", output, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteLine_WhenFooterWasRendered_DoesNotCommitFooterRowsAsBlankScrollback()
    {
        var renderer = new TerminalPromptRenderer();
        renderer.Render(new TerminalRenderFrame(
            "> ",
            "为什么生成的是.net8.0框架的？",
            "为什么生成的是.net8.0框架的？".Length,
            FooterLines:
            [
                "\u001b[2m────────────────────────\u001b[0m",
                "\u001b[2mWorking · Model: openai-compatible-default\u001b[0m",
                "\u001b[32m  ✓ 1. 创建项目\u001b[0m",
            ]));

        var output = renderer.CompleteLine();

        Assert.Contains("\u001b[0J", output, StringComparison.Ordinal);
        Assert.Equal(1, output.Count(static character => character == '\n'));
        Assert.DoesNotContain("\n\u001b[2K", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteLine_WhenCursorIsInMiddle_MovesToPromptLineEndBeforeClearingFooter()
    {
        var renderer = new TerminalPromptRenderer();
        renderer.Render(new TerminalRenderFrame(
            "> ",
            "abcdef",
            2,
            FooterLine: "\u001b[2mIdle\u001b[0m"));

        var output = renderer.CompleteLine();

        Assert.StartsWith("\u001b[4C\u001b[0J", output, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenTextIsEmpty_RendersPlaceholderAndFooterWithoutMovingCursorPastPlaceholder()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame(
            "> ",
            string.Empty,
            0,
            PlaceholderText: "\u001b[2m问天枢，或输入 /help 与 @filename\u001b[0m",
            FooterLine: "\u001b[2mgpt-test · ~\u001b[0m"));

        Assert.Contains(TerminalAnsi.Reset + "> \u001b[2m问天枢，或输入 /help 与 @filename\u001b[0m", output, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "\u001b[2mgpt-test · ~\u001b[0m", output, StringComparison.Ordinal);
        Assert.Contains("\u001b[1A\r\u001b[2C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenFooterHasMultipleLines_KeepsCursorOnInputLine()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame(
            "> ",
            string.Empty,
            0,
            FooterLines:
            [
                "\u001b[2mPlan 1/3\u001b[0m",
                "\u001b[32m  ✓ 1. 创建项目\u001b[0m",
                "\u001b[33m  ▶ 2. 实现界面\u001b[0m",
            ]));

        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "\u001b[2mPlan 1/3\u001b[0m", output, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "\u001b[32m  ✓ 1. 创建项目\u001b[0m", output, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "\u001b[33m  ▶ 2. 实现界面\u001b[0m", output, StringComparison.Ordinal);
        Assert.Contains("\u001b[3A\r\u001b[2C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenPopupAndFooterLinesExist_PlacesPopupBelowInputSeparator()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame(
            "> ",
            "/",
            1,
            PopupLines:
            [
                "  /approve          提交 accept 审批响应",
                "  /archive          归档线程",
            ],
            FooterLines:
            [
                "\u001b[2m────────────────────────\u001b[0m",
                "\u001b[2mWorking · Model: openai-compatible-default\u001b[0m",
                "\u001b[33m  ▶ 2. 实现界面\u001b[0m",
            ]));

        var separatorIndex = output.IndexOf("────────────────────────", StringComparison.Ordinal);
        var popupIndex = output.IndexOf("  /approve", StringComparison.Ordinal);
        var statusIndex = output.IndexOf("Working · Model", StringComparison.Ordinal);
        var planIndex = output.IndexOf("▶ 2. 实现界面", StringComparison.Ordinal);

        Assert.True(separatorIndex >= 0);
        Assert.True(separatorIndex < popupIndex);
        Assert.True(popupIndex < statusIndex);
        Assert.True(statusIndex < planIndex);
    }

    [Fact]
    public void Render_WhenMultiLinePromptShrinks_ClearsPreviouslyRenderedInputRows()
    {
        var renderer = new TerminalPromptRenderer();

        renderer.Render(new TerminalRenderFrame("> ", "one" + Environment.NewLine + "two" + Environment.NewLine + "three", 15));
        var output = renderer.Render(new TerminalRenderFrame("> ", "one", 3));

        Assert.Equal(3, CountOccurrences(output, "\u001b[2K"));
        Assert.Contains("\u001b[2A\r\u001b[5C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenLongInputExceedsWritableWidth_WrapsIntoTrackedPhysicalRows()
    {
        var renderer = new TerminalPromptRenderer(() => 12);

        var output = renderer.Render(new TerminalRenderFrame("> ", "abcdefghijklmno", 15));

        Assert.Contains("> abcdefghij", output, StringComparison.Ordinal);
        Assert.Contains("\n\u001b[2K" + TerminalAnsi.Reset + "  klmno", output, StringComparison.Ordinal);
        Assert.EndsWith("\r\u001b[7C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenLongInputShrinks_ClearsPreviouslyWrappedPhysicalRows()
    {
        var renderer = new TerminalPromptRenderer(() => 12);

        renderer.Render(new TerminalRenderFrame("> ", "abcdefghijklmno", 15));
        var output = renderer.Render(new TerminalRenderFrame("> ", "abc", 3));

        Assert.Equal(2, CountOccurrences(output, "\u001b[2K"));
        Assert.Contains("\u001b[1A\r\u001b[5C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearFrame_WhenLongInputWasWrapped_ClearsAllPhysicalRows()
    {
        var renderer = new TerminalPromptRenderer(() => 12);
        renderer.Render(new TerminalRenderFrame("> ", "abcdefghijklmno", 15));

        var output = renderer.ClearFrame();

        Assert.StartsWith("\u001b[1A\r", output, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(output, "\u001b[2K"));
        Assert.EndsWith("\u001b[1A\r", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteLine_WhenCursorIsBeforeMultiLineInputEnd_MovesToFinalInputLineBeforeClearingFooter()
    {
        var renderer = new TerminalPromptRenderer();
        renderer.Render(new TerminalRenderFrame(
            "> ",
            "line 1" + Environment.NewLine + "line 2",
            2,
            FooterLine: "\u001b[2mIdle\u001b[0m"));

        var output = renderer.CompleteLine();

        Assert.StartsWith("\u001b[1B\r\u001b[8C\u001b[0J", output, StringComparison.Ordinal);
        Assert.EndsWith(Environment.NewLine, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenLeadingBlankLinesAreRequested_DrawsPromptAfterSpacer()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame(
            "> ",
            string.Empty,
            0,
            FooterLine: "\u001b[2mIdle\u001b[0m",
            LeadingBlankLineCount: 2));

        Assert.StartsWith(
            "\r\u001b[2K" + TerminalAnsi.Reset + "\n\u001b[2K" + TerminalAnsi.Reset + "\n\u001b[2K" + TerminalAnsi.Reset + "> ",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\u001b[1A\r\u001b[2C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearFrame_WhenLeadingBlankLinesWereRendered_ClearsSpacerPromptAndFooter()
    {
        var renderer = new TerminalPromptRenderer();
        renderer.Render(new TerminalRenderFrame(
            "> ",
            string.Empty,
            0,
            FooterLine: "\u001b[2mIdle\u001b[0m",
            LeadingBlankLineCount: 2));

        var output = renderer.ClearFrame();

        Assert.StartsWith("\u001b[2A\r", output, StringComparison.Ordinal);
        Assert.Equal(4, CountOccurrences(output, "\u001b[2K"));
        Assert.EndsWith("\u001b[3A\r", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenTextContainsWideCharacters_MovesCursorByTerminalCellWidth()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame(
            "> ",
            "你叫什么名字",
            "你叫什么名字".Length,
            FooterLine: "\u001b[2mgpt-test · ~\u001b[0m"));

        Assert.Contains("\u001b[1A\r\u001b[14C", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WhenCursorIsBeforeWideCharacters_MovesBackByTerminalCellWidth()
    {
        var renderer = new TerminalPromptRenderer();

        var output = renderer.Render(new TerminalRenderFrame("> ", "你好ab", 1));

        Assert.EndsWith("\r\u001b[4C", output, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
