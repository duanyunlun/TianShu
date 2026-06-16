using System.Globalization;
using System.Text;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

internal enum ModelStatusProbeOutcome
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Error = 3,
    Unavailable = 4,
}

internal sealed class ModelStatusTableRenderer
{
    private const int IndexColumnWidth = 5;
    private const int ModelColumnWidth = 42;
    private const int ProtocolColumnWidth = 24;
    private const int ResultColumnWidth = 10;
    private const int ReasoningColumnWidth = 8;
    private const int ElapsedColumnWidth = 8;

    private readonly Func<int> windowWidthProvider;
    private readonly Func<bool> outputRedirectedProvider;

    public ModelStatusTableRenderer()
        : this(static () => Console.WindowWidth, static () => Console.IsOutputRedirected)
    {
    }

    internal ModelStatusTableRenderer(Func<int> windowWidthProvider, Func<bool> outputRedirectedProvider)
    {
        this.windowWidthProvider = windowWidthProvider ?? throw new ArgumentNullException(nameof(windowWidthProvider));
        this.outputRedirectedProvider = outputRedirectedProvider ?? throw new ArgumentNullException(nameof(outputRedirectedProvider));
    }

    public string BuildHeader(bool styled)
    {
        var header = string.Concat(
            PadCell("序号", IndexColumnWidth),
            "  ",
            PadCell("模型", ModelColumnWidth),
            "  ",
            PadCell("协议", ProtocolColumnWidth),
            "  ",
            PadCell("测试结果", ResultColumnWidth),
            "  ",
            PadCell("思考", ReasoningColumnWidth),
            "  ",
            PadCell("耗时", ElapsedColumnWidth),
            "  ",
            "报错");
        return styled ? TerminalAnsi.DimText(header) : header;
    }

    public string BuildRow(
        int index,
        string model,
        string protocol,
        ModelStatusProbeOutcome outcome,
        string reasoning,
        TimeSpan elapsed,
        string? error,
        bool styled)
    {
        var indexText = index <= 0 ? "-" : index.ToString(CultureInfo.InvariantCulture);
        var resultText = FormatOutcome(outcome);
        var prefix = string.Concat(
            PadCell(indexText, IndexColumnWidth),
            "  ",
            PadCell(model, ModelColumnWidth),
            "  ",
            PadCell(protocol, ProtocolColumnWidth),
            "  ",
            PadCell(resultText, ResultColumnWidth),
            "  ",
            PadCell(reasoning, ReasoningColumnWidth),
            "  ",
            PadCell(FormatElapsed(elapsed), ElapsedColumnWidth),
            "  ");
        var errorText = NormalizeError(error, ResolveErrorColumnWidth(prefix, styled));
        var row = prefix + errorText;

        if (!styled)
        {
            return row;
        }

        var styledPrefix = string.Concat(
            TerminalAnsi.DimText(PadCell(indexText, IndexColumnWidth)),
            "  ",
            TerminalAnsi.BlueText(PadCell(model, ModelColumnWidth)),
            "  ",
            TerminalAnsi.MagentaText(PadCell(protocol, ProtocolColumnWidth)),
            "  ",
            StyleOutcome(PadCell(resultText, ResultColumnWidth), outcome),
            "  ",
            StyleReasoningSignal(PadCell(reasoning, ReasoningColumnWidth), reasoning),
            "  ",
            TerminalAnsi.DimText(PadCell(FormatElapsed(elapsed), ElapsedColumnWidth)),
            "  ");
        return styledPrefix + (outcome is ModelStatusProbeOutcome.Succeeded ? TerminalAnsi.DimText(errorText) : StyleOutcome(errorText, outcome));
    }

    public string StyleTitle(string text, bool styled)
        => styled ? TerminalAnsi.BoldText(text) : text;

    public string StyleMeta(string text, bool styled)
        => styled ? TerminalAnsi.DimText(text) : text;

    public string FitTerminalRow(string row, bool styled)
    {
        if (!styled || outputRedirectedProvider())
        {
            return row;
        }

        try
        {
            var width = windowWidthProvider();
            return width <= 1 ? row : TruncateAnsiToDisplayWidth(row, width - 1);
        }
        catch (IOException)
        {
            return row;
        }
        catch (InvalidOperationException)
        {
            return row;
        }
    }

    private static string FormatOutcome(ModelStatusProbeOutcome outcome)
        => outcome switch
        {
            ModelStatusProbeOutcome.Running => "测试中",
            ModelStatusProbeOutcome.Succeeded => "成功",
            ModelStatusProbeOutcome.Failed => "失败",
            ModelStatusProbeOutcome.Error => "报错",
            ModelStatusProbeOutcome.Unavailable => "未实现",
            _ => "未知",
        };

    private static string StyleOutcome(string text, ModelStatusProbeOutcome outcome)
        => outcome switch
        {
            ModelStatusProbeOutcome.Running => TerminalAnsi.YellowText(text),
            ModelStatusProbeOutcome.Succeeded => TerminalAnsi.GreenText(text),
            ModelStatusProbeOutcome.Failed => TerminalAnsi.YellowText(text),
            ModelStatusProbeOutcome.Error => TerminalAnsi.RedText(text),
            ModelStatusProbeOutcome.Unavailable => TerminalAnsi.DimText(text),
            _ => text,
        };

    private static string StyleReasoningSignal(string text, string reasoning)
        => reasoning switch
        {
            "可见" => TerminalAnsi.BlueText(text),
            "未观测" => TerminalAnsi.DimText(text),
            "检测中" => TerminalAnsi.YellowText(text),
            _ => TerminalAnsi.DimText(text),
        };

    private static string FormatElapsed(TimeSpan elapsed)
        => elapsed.TotalSeconds >= 10
            ? $"{elapsed.TotalSeconds:0.0}s"
            : $"{elapsed.TotalMilliseconds:0}ms";

    private int ResolveErrorColumnWidth(string prefix, bool styled)
    {
        var fallback = 96;
        if (!styled || outputRedirectedProvider())
        {
            return fallback;
        }

        try
        {
            var remaining = windowWidthProvider() - GetDisplayWidth(prefix) - 1;
            return Math.Clamp(remaining, 16, fallback);
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }

    private static string NormalizeError(string? error, int maxDisplayWidth)
    {
        var normalized = Normalize(error)?.Replace('\r', ' ').Replace('\n', ' ');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "-";
        }

        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return TruncateToDisplayWidth(normalized, Math.Max(1, maxDisplayWidth));
    }

    private static string PadCell(string value, int width)
    {
        var normalized = NormalizeCell(value);
        normalized = TruncateToDisplayWidth(normalized, width);
        return normalized + new string(' ', Math.Max(0, width - GetDisplayWidth(normalized)));
    }

    private static string NormalizeCell(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().Replace('\r', ' ').Replace('\n', ' ');

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string TruncateToDisplayWidth(string value, int maxDisplayWidth)
    {
        if (GetDisplayWidth(value) <= maxDisplayWidth)
        {
            return value;
        }

        if (maxDisplayWidth <= 1)
        {
            return "…";
        }

        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var width = GetRuneDisplayWidth(rune);
            if (used + width > maxDisplayWidth - 1)
            {
                break;
            }

            builder.Append(rune);
            used += width;
        }

        builder.Append('…');
        return builder.ToString();
    }

    private static string TruncateAnsiToDisplayWidth(string text, int maxWidth)
    {
        if (maxWidth <= 0 || GetDisplayWidthWithoutAnsi(text) <= maxWidth)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var width = 0;
        var truncated = false;
        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '\u001b' && TryCopyAnsiSequence(text, ref i, builder))
            {
                continue;
            }

            if (!Rune.TryGetRuneAt(text, i, out var rune))
            {
                break;
            }

            var runeWidth = GetRuneDisplayWidth(rune);
            if (width + runeWidth > maxWidth)
            {
                truncated = true;
                break;
            }

            builder.Append(rune.ToString());
            width += runeWidth;
            i += rune.Utf16SequenceLength;
        }

        if (truncated && text.Contains('\u001b', StringComparison.Ordinal))
        {
            builder.Append(TerminalAnsi.Reset);
        }

        return builder.ToString();
    }

    private static int GetDisplayWidth(string value)
    {
        var width = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            width += GetRuneDisplayWidth(rune);
        }

        return width;
    }

    private static int GetDisplayWidthWithoutAnsi(string text)
    {
        var width = 0;
        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '\u001b')
            {
                SkipAnsiSequence(text, ref i);
                continue;
            }

            if (!Rune.TryGetRuneAt(text, i, out var rune))
            {
                break;
            }

            width += GetRuneDisplayWidth(rune);
            i += rune.Utf16SequenceLength;
        }

        return width;
    }

    private static int GetRuneDisplayWidth(Rune rune)
    {
        var value = rune.Value;
        if (value == 0 || value < 32 || value is >= 0x7F and < 0xA0)
        {
            return 0;
        }

        return IsWideRune(value) ? 2 : 1;
    }

    private static bool IsWideRune(int value)
        => value is >= 0x1100 and <= 0x115F
            or >= 0x2329 and <= 0x232A
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF;

    private static bool TryCopyAnsiSequence(string text, ref int index, StringBuilder builder)
    {
        var start = index;
        if (!SkipAnsiSequence(text, ref index))
        {
            return false;
        }

        builder.Append(text, start, index - start);
        return true;
    }

    private static bool SkipAnsiSequence(string text, ref int index)
    {
        if (index >= text.Length || text[index] != '\u001b')
        {
            return false;
        }

        var start = index++;
        if (index >= text.Length || text[index] != '[')
        {
            index = start;
            return false;
        }

        index++;
        while (index < text.Length)
        {
            var ch = text[index++];
            if (ch is >= '@' and <= '~')
            {
                return true;
            }
        }

        index = start;
        return false;
    }
}
