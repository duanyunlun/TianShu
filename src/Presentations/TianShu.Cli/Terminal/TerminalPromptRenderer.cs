using System.Text;
using TianShu.Cli.Interaction.Rendering;

namespace TianShu.Cli.Terminal;

/// <summary>
/// Builds ANSI / VT prompt redraw sequences for the custom TianShu terminal shell.
/// 为 TianShu 自制终端交互壳生成 ANSI / VT 输入行重绘序列。
/// </summary>
internal sealed class TerminalPromptRenderer
{
    private const string ClearCurrentLine = "\u001b[2K";
    private const string ClearFromCursorToScreenEnd = "\u001b[0J";
    private readonly Func<int> writableWidthProvider;
    private int previousExtraLineCount;
    private int previousAboveInputLineCount;
    private int previousInputLineCount = 1;
    private int previousLeadingBlankLineCount;
    private int previousCursorLineIndex;
    private int previousCursorColumn;
    private int previousInputEndColumn;

    public TerminalPromptRenderer()
        : this(static () => TerminalLayoutCalculator.SafeWritableWidth())
    {
    }

    internal TerminalPromptRenderer(Func<int> writableWidthProvider)
        => this.writableWidthProvider = writableWidthProvider ?? throw new ArgumentNullException(nameof(writableWidthProvider));

    public string Render(TerminalRenderFrame frame)
    {
        if (frame.OverrideLines is { Count: > 0 } overrideLines)
        {
            return RenderOverrideFrame(frame, overrideLines);
        }

        var text = frame.Text ?? string.Empty;
        var cursor = Math.Clamp(frame.Cursor, 0, text.Length);
        var width = SafeWritableWidth();
        var promptWidth = GetDisplayWidth(frame.Prompt);
        var popupLines = frame.PopupLines ?? Array.Empty<string>();
        var aboveInputLines = WrapExtraLines(frame.AboveInputLines ?? Array.Empty<string>(), width);
        var extraLines = WrapExtraLines(
            BuildExtraLines(frame.FooterLine, frame.FooterLines, popupLines),
            width);
        var leadingBlankLineCount = Math.Max(0, frame.LeadingBlankLineCount);
        var inputLayout = BuildInputLayout(
            frame.Prompt,
            promptWidth,
            text,
            cursor,
            frame.PlaceholderText,
            width);
        var visibleLines = BuildVisibleLines(leadingBlankLineCount, aboveInputLines, inputLayout.Lines, extraLines);
        var previousFrameLineCount = previousLeadingBlankLineCount
            + previousAboveInputLineCount
            + previousInputLineCount
            + previousExtraLineCount;
        var linesToClear = Math.Max(previousFrameLineCount, visibleLines.Count);
        var builder = new StringBuilder();
        MoveToFrameStart(builder, previousLeadingBlankLineCount + previousAboveInputLineCount + previousCursorLineIndex);
        for (var index = 0; index < linesToClear; index++)
        {
            builder.Append(ClearCurrentLine);
            builder.Append(TerminalAnsi.Reset);
            if (index < visibleLines.Count && visibleLines[index].Length > 0)
            {
                builder.Append(visibleLines[index]);
                builder.Append(TerminalAnsi.Reset);
            }

            if (index < linesToClear - 1)
            {
                builder.Append('\n');
            }
        }

        var targetFrameLineIndex = leadingBlankLineCount + aboveInputLines.Count + inputLayout.CursorLineIndex;
        var rowsBelowCursor = Math.Max(0, linesToClear - 1 - targetFrameLineIndex);
        if (rowsBelowCursor > 0)
        {
            builder.Append("\u001b[");
            builder.Append(rowsBelowCursor);
            builder.Append('A');
        }

        builder.Append('\r');
        if (inputLayout.CursorColumn > 0)
        {
            builder.Append("\u001b[");
            builder.Append(inputLayout.CursorColumn);
            builder.Append('C');
        }

        previousExtraLineCount = extraLines.Count;
        previousAboveInputLineCount = aboveInputLines.Count;
        previousInputLineCount = inputLayout.Lines.Count;
        previousLeadingBlankLineCount = leadingBlankLineCount;
        previousCursorLineIndex = inputLayout.CursorLineIndex;
        previousCursorColumn = inputLayout.CursorColumn;
        previousInputEndColumn = inputLayout.InputEndColumn;
        return builder.ToString();
    }

    private string RenderOverrideFrame(TerminalRenderFrame frame, IReadOnlyList<string> overrideLines)
    {
        var width = SafeWritableWidth();
        var leadingBlankLineCount = Math.Max(0, frame.LeadingBlankLineCount);
        var renderedOverrideLines = WrapExtraLines(overrideLines, width);
        var visibleLines = new List<string>(leadingBlankLineCount + renderedOverrideLines.Count);
        for (var index = 0; index < leadingBlankLineCount; index++)
        {
            visibleLines.Add(string.Empty);
        }

        visibleLines.AddRange(renderedOverrideLines);

        var previousFrameLineCount = previousLeadingBlankLineCount
            + previousAboveInputLineCount
            + previousInputLineCount
            + previousExtraLineCount;
        var linesToClear = Math.Max(previousFrameLineCount, visibleLines.Count);
        var builder = new StringBuilder();
        MoveToFrameStart(builder, previousLeadingBlankLineCount + previousAboveInputLineCount + previousCursorLineIndex);
        for (var index = 0; index < linesToClear; index++)
        {
            builder.Append(ClearCurrentLine);
            builder.Append(TerminalAnsi.Reset);
            if (index < visibleLines.Count && visibleLines[index].Length > 0)
            {
                builder.Append(visibleLines[index]);
                builder.Append(TerminalAnsi.Reset);
            }

            if (index < linesToClear - 1)
            {
                builder.Append('\n');
            }
        }

        var targetFrameLineIndex = Math.Max(0, visibleLines.Count - 1);
        var rowsBelowCursor = Math.Max(0, linesToClear - 1 - targetFrameLineIndex);
        if (rowsBelowCursor > 0)
        {
            builder.Append("\u001b[");
            builder.Append(rowsBelowCursor);
            builder.Append('A');
        }

        builder.Append('\r');
        previousExtraLineCount = 0;
        previousAboveInputLineCount = 0;
        previousInputLineCount = Math.Max(1, visibleLines.Count - leadingBlankLineCount);
        previousLeadingBlankLineCount = leadingBlankLineCount;
        previousCursorLineIndex = Math.Max(0, previousInputLineCount - 1);
        previousCursorColumn = 0;
        previousInputEndColumn = 0;
        return builder.ToString();
    }

    public string ClearLine()
    {
        previousExtraLineCount = 0;
        previousAboveInputLineCount = 0;
        previousInputLineCount = 1;
        previousLeadingBlankLineCount = 0;
        previousCursorLineIndex = 0;
        previousCursorColumn = 0;
        previousInputEndColumn = 0;
        return "\r" + ClearCurrentLine + TerminalAnsi.Reset;
    }

    public string ClearFrame()
    {
        var builder = new StringBuilder();
        var totalLineCount = previousLeadingBlankLineCount
            + previousAboveInputLineCount
            + previousInputLineCount
            + previousExtraLineCount;
        MoveToFrameStart(builder, previousLeadingBlankLineCount + previousAboveInputLineCount + previousCursorLineIndex);
        for (var index = 0; index < totalLineCount; index++)
        {
            builder.Append(ClearCurrentLine);
            builder.Append(TerminalAnsi.Reset);
            if (index < totalLineCount - 1)
            {
                builder.Append('\n');
            }
        }

        if (totalLineCount > 1)
        {
            builder.Append("\u001b[");
            builder.Append(totalLineCount - 1);
            builder.Append('A');
            builder.Append('\r');
        }

        previousExtraLineCount = 0;
        previousAboveInputLineCount = 0;
        previousInputLineCount = 1;
        previousLeadingBlankLineCount = 0;
        previousCursorLineIndex = 0;
        previousCursorColumn = 0;
        previousInputEndColumn = 0;
        return builder.ToString();
    }

    public string CompleteLine()
    {
        var builder = new StringBuilder();
        var rowsToInputEnd = Math.Max(0, previousInputLineCount - 1 - previousCursorLineIndex);
        if (rowsToInputEnd > 0)
        {
            builder.Append("\u001b[");
            builder.Append(rowsToInputEnd);
            builder.Append('B');
            builder.Append('\r');
            if (previousInputEndColumn > 0)
            {
                builder.Append("\u001b[");
                builder.Append(previousInputEndColumn);
                builder.Append('C');
            }
        }
        else if (previousInputEndColumn > previousCursorColumn)
        {
            builder.Append("\u001b[");
            builder.Append(previousInputEndColumn - previousCursorColumn);
            builder.Append('C');
        }
        else if (previousInputEndColumn < previousCursorColumn)
        {
            builder.Append("\u001b[");
            builder.Append(previousCursorColumn - previousInputEndColumn);
            builder.Append('D');
        }

        if (previousExtraLineCount > 0)
        {
            builder.Append(ClearFromCursorToScreenEnd);
            builder.Append(TerminalAnsi.Reset);
        }

        builder.AppendLine();
        previousExtraLineCount = 0;
        previousAboveInputLineCount = 0;
        previousInputLineCount = 1;
        previousLeadingBlankLineCount = 0;
        previousCursorLineIndex = 0;
        previousCursorColumn = 0;
        previousInputEndColumn = 0;
        return builder.ToString();
    }

    private static void MoveToFrameStart(StringBuilder builder, int leadingBlankLineCount)
    {
        if (leadingBlankLineCount > 0)
        {
            builder.Append("\u001b[");
            builder.Append(leadingBlankLineCount);
            builder.Append('A');
            builder.Append('\r');
        }
        else
        {
            builder.Append('\r');
        }
    }

    private static IReadOnlyList<string> BuildExtraLines(
        string? footerLine,
        IReadOnlyList<string>? footerLines,
        IReadOnlyList<string> popupLines)
    {
        var footers = new List<string>();
        if (!string.IsNullOrEmpty(footerLine))
        {
            footers.Add(footerLine);
        }

        if (footerLines is { Count: > 0 })
        {
            footers.AddRange(footerLines.Where(static line => !string.IsNullOrEmpty(line)));
        }

        if (footers.Count == 0)
        {
            return popupLines;
        }

        if (popupLines.Count == 0)
        {
            return footers;
        }

        if (footerLines is { Count: > 0 })
        {
            var lines = new List<string>(footers.Count + popupLines.Count);
            if (!string.IsNullOrEmpty(footerLine))
            {
                lines.Add(footerLine);
            }

            lines.Add(footerLines[0]);
            lines.AddRange(popupLines);
            lines.AddRange(footerLines.Skip(1).Where(static line => !string.IsNullOrEmpty(line)));
            return lines;
        }

        var popupFirstLines = new List<string>(footers.Count + popupLines.Count);
        popupFirstLines.AddRange(popupLines);
        popupFirstLines.AddRange(footers);
        return popupFirstLines;
    }

    private int SafeWritableWidth()
        => Math.Max(1, writableWidthProvider());

    private static IReadOnlyList<string> BuildVisibleLines(
        int leadingBlankLineCount,
        IReadOnlyList<string> aboveInputLines,
        IReadOnlyList<string> inputLines,
        IReadOnlyList<string> extraLines)
    {
        var visibleLines = new List<string>(
            leadingBlankLineCount + aboveInputLines.Count + inputLines.Count + extraLines.Count);
        for (var index = 0; index < leadingBlankLineCount; index++)
        {
            visibleLines.Add(string.Empty);
        }

        visibleLines.AddRange(aboveInputLines);
        visibleLines.AddRange(inputLines);
        visibleLines.AddRange(extraLines);
        return visibleLines;
    }

    private static InputLayout BuildInputLayout(
        string prompt,
        int promptWidth,
        string text,
        int cursor,
        string? placeholderText,
        int width)
    {
        if (text.Length == 0)
        {
            var placeholderLines = WrapLogicalInputLine(
                placeholderText ?? string.Empty,
                prompt,
                promptWidth,
                width,
                cursorOffset: 0);
            return new InputLayout(
                placeholderLines.Lines,
                CursorLineIndex: 0,
                CursorColumn: promptWidth,
                InputEndColumn: promptWidth);
        }

        var logicalLines = SplitInputLines(text);
        var cursorPosition = ResolveCursorPosition(text, cursor);
        var inputIndent = new string(' ', promptWidth);
        var visibleLines = new List<string>();
        var cursorLineIndex = 0;
        var cursorColumn = promptWidth;
        var inputEndColumn = promptWidth;
        for (var index = 0; index < logicalLines.Count; index++)
        {
            var prefix = index == 0 ? prompt : inputIndent;
            var wrapped = WrapLogicalInputLine(
                logicalLines[index],
                prefix,
                promptWidth,
                width,
                cursorPosition.LineIndex == index ? cursorPosition.OffsetInLine : null);
            if (cursorPosition.LineIndex == index)
            {
                cursorLineIndex = visibleLines.Count + wrapped.CursorLineIndex;
                cursorColumn = wrapped.CursorColumn;
            }

            visibleLines.AddRange(wrapped.Lines);
            if (index == logicalLines.Count - 1)
            {
                inputEndColumn = wrapped.InputEndColumn;
            }
        }

        return new InputLayout(visibleLines, cursorLineIndex, cursorColumn, inputEndColumn);
    }

    private static WrappedInputLine WrapLogicalInputLine(
        string value,
        string firstPrefix,
        int indentWidth,
        int width,
        int? cursorOffset)
    {
        var indent = new string(' ', indentWidth);
        var lines = new List<string>();
        var segments = SplitTextByWidth(value, Math.Max(1, width - GetDisplayWidth(firstPrefix)), Math.Max(1, width - indentWidth));
        var cursorLineIndex = 0;
        var cursorColumn = GetDisplayWidth(firstPrefix);
        var inputEndColumn = cursorColumn;
        for (var index = 0; index < segments.Count; index++)
        {
            var prefix = index == 0 ? firstPrefix : indent;
            var prefixWidth = index == 0 ? GetDisplayWidth(firstPrefix) : indentWidth;
            var segment = segments[index];
            lines.Add(prefix + segment.Text);
            inputEndColumn = prefixWidth + GetDisplayWidth(segment.Text);
            if (cursorOffset is not null
                && cursorOffset.Value >= segment.Start
                && (cursorOffset.Value < segment.End || segment.End == value.Length))
            {
                cursorLineIndex = index;
                cursorColumn = prefixWidth + GetDisplayWidth(value[segment.Start..cursorOffset.Value]);
            }
        }

        return new WrappedInputLine(lines, cursorLineIndex, cursorColumn, inputEndColumn);
    }

    private static IReadOnlyList<WrappedSegment> SplitTextByWidth(string value, int firstWidth, int continuationWidth)
    {
        if (value.Length == 0)
        {
            return [new WrappedSegment(string.Empty, 0, 0)];
        }

        var lines = new List<WrappedSegment>();
        var current = new StringBuilder();
        var currentWidth = 0;
        var segmentStart = 0;
        var currentLimit = firstWidth;
        for (var index = 0; index < value.Length;)
        {
            var runeStart = index;
            if (TryCopyAnsiSequence(value, ref index, current))
            {
                continue;
            }

            Rune rune;
            if (index + 1 < value.Length && char.IsSurrogatePair(value[index], value[index + 1]))
            {
                rune = new Rune(value[index], value[index + 1]);
                index += 2;
            }
            else
            {
                rune = new Rune(value[index]);
                index++;
            }

            var runeWidth = GetRuneDisplayWidth(rune);
            if (currentWidth > 0 && currentWidth + runeWidth > currentLimit)
            {
                lines.Add(new WrappedSegment(current.ToString(), segmentStart, runeStart));
                current.Clear();
                currentWidth = 0;
                segmentStart = runeStart;
                currentLimit = continuationWidth;
            }

            current.Append(rune);
            currentWidth += runeWidth;
        }

        lines.Add(new WrappedSegment(current.ToString(), segmentStart, value.Length));
        return lines;
    }

    private static IReadOnlyList<string> WrapExtraLines(IReadOnlyList<string> lines, int width)
    {
        if (lines.Count == 0)
        {
            return lines;
        }

        var wrapped = new List<string>();
        foreach (var line in lines)
        {
            wrapped.AddRange(TerminalLayoutCalculator.WrapToWidth(line, width));
        }

        return wrapped;
    }

    private static IReadOnlyList<string> SplitInputLines(string value)
    {
        if (value.Length == 0)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] is not ('\r' or '\n'))
            {
                continue;
            }

            lines.Add(value[start..index]);
            if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        lines.Add(value[start..]);
        return lines;
    }

    private static CursorPosition ResolveCursorPosition(string value, int cursor)
    {
        var lineIndex = 0;
        var lineStart = 0;
        for (var index = 0; index < cursor;)
        {
            if (value[index] == '\r')
            {
                lineIndex++;
                if (index + 1 < value.Length && value[index + 1] == '\n' && index + 1 < cursor)
                {
                    index += 2;
                }
                else
                {
                    index++;
                }

                lineStart = index;
                continue;
            }

            if (value[index] == '\n')
            {
                lineIndex++;
                index++;
                lineStart = index;
                continue;
            }

            index++;
        }

        var textBeforeCursorOnLine = cursor > lineStart
            ? value[lineStart..cursor]
            : string.Empty;
        return new CursorPosition(lineIndex, textBeforeCursorOnLine.Length);
    }

    private static int GetDisplayWidth(string value)
    {
        var width = 0;
        for (var index = 0; index < value.Length;)
        {
            if (TrySkipAnsiSequence(value, ref index))
            {
                continue;
            }

            Rune rune;
            if (index + 1 < value.Length && char.IsSurrogatePair(value[index], value[index + 1]))
            {
                rune = new Rune(value[index], value[index + 1]);
                index += 2;
            }
            else
            {
                rune = new Rune(value[index]);
                index++;
            }

            width += GetRuneDisplayWidth(rune);
        }

        return width;
    }

    private static bool TrySkipAnsiSequence(string value, ref int index)
    {
        if (index + 1 >= value.Length || value[index] != '\u001b' || value[index + 1] != '[')
        {
            return false;
        }

        index += 2;
        while (index < value.Length && !IsAnsiFinalByte(value[index]))
        {
            index++;
        }

        if (index < value.Length)
        {
            index++;
        }

        return true;
    }

    private static bool TryCopyAnsiSequence(string value, ref int index, StringBuilder builder)
    {
        if (index + 1 >= value.Length || value[index] != '\u001b' || value[index + 1] != '[')
        {
            return false;
        }

        var start = index;
        index += 2;
        while (index < value.Length && !IsAnsiFinalByte(value[index]))
        {
            index++;
        }

        if (index < value.Length)
        {
            index++;
        }

        builder.Append(value.AsSpan(start, index - start));
        return true;
    }

    private static bool IsAnsiFinalByte(char value)
        => value is >= '@' and <= '~';

    private static int GetRuneDisplayWidth(Rune rune)
    {
        var value = rune.Value;
        if (value is < 0x20 or >= 0x7F and <= 0x9F || IsCombiningCodePoint(value))
        {
            return 0;
        }

        return IsWideCodePoint(value) ? 2 : 1;
    }

    private static bool IsCombiningCodePoint(int value)
        => value is >= 0x0300 and <= 0x036F
            or >= 0x1AB0 and <= 0x1AFF
            or >= 0x1DC0 and <= 0x1DFF
            or >= 0x20D0 and <= 0x20FF
            or >= 0xFE20 and <= 0xFE2F;

    private static bool IsWideCodePoint(int value)
        => value is >= 0x1100 and <= 0x115F
            or >= 0x2329 and <= 0x232A
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;

    private readonly record struct CursorPosition(int LineIndex, int OffsetInLine);

    private readonly record struct InputLayout(
        IReadOnlyList<string> Lines,
        int CursorLineIndex,
        int CursorColumn,
        int InputEndColumn);

    private readonly record struct WrappedInputLine(
        IReadOnlyList<string> Lines,
        int CursorLineIndex,
        int CursorColumn,
        int InputEndColumn);

    private readonly record struct WrappedSegment(string Text, int Start, int End);
}
