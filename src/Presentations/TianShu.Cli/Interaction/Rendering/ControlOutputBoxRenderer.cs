using System.Text;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

/// <summary>
/// Renders out-of-band control command output as an ASCII block for human terminals.
/// 将控制命令输出渲染为 ASCII 区块，帮助用户与 assistant 正文区分。
/// </summary>
internal static class ControlOutputBoxRenderer
{
    private const int MinimumWidth = 24;
    private const string Title = " 控制输出 ";

    public static string Render(IReadOnlyList<ControlOutputLine> entries, int width, bool styled)
    {
        var outerWidth = Math.Max(MinimumWidth, width);
        var borderWidth = outerWidth - 2;
        var contentWidth = Math.Max(1, outerWidth - 4);
        var builder = new StringBuilder();

        builder.AppendLine();
        builder.Append(StyleBorder(BuildTopBorder(borderWidth), styled));
        foreach (var entry in entries)
        {
            foreach (var line in WrapLines(entry.Text, contentWidth))
            {
                builder.AppendLine();
                builder.Append(StyleBorder("| ", styled));
                builder.Append(StyleContent(TerminalLayoutCalculator.FitToWidth(line, contentWidth), entry.IsError, styled));
                builder.Append(StyleBorder(" |", styled));
            }
        }

        builder.AppendLine();
        builder.AppendLine(StyleBorder("+" + new string('=', borderWidth) + "+", styled));
        builder.AppendLine();
        return builder.ToString();
    }

    public static string Render(string text, bool isError, int width, bool styled)
        => Render([new ControlOutputLine(text, isError)], width, styled);

    private static string BuildTopBorder(int borderWidth)
    {
        var titleWidth = TerminalLayoutCalculator.GetDisplayWidth(Title);
        if (borderWidth <= titleWidth)
        {
            return "+" + new string('=', borderWidth) + "+";
        }

        var leftWidth = (borderWidth - titleWidth) / 2;
        var rightWidth = borderWidth - titleWidth - leftWidth;
        return "+" + new string('=', leftWidth) + Title + new string('=', rightWidth) + "+";
    }

    private static string StyleBorder(string text, bool styled)
        => styled ? TerminalAnsi.SkyBlueText(text) : text;

    private static string StyleContent(string text, bool isError, bool styled)
        => styled && isError ? TerminalAnsi.RedText(text) : text;

    private static IReadOnlyList<string> WrapLines(string? text, int contentWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? physicalLine;
        while ((physicalLine = reader.ReadLine()) is not null)
        {
            lines.AddRange(TerminalLayoutCalculator.WrapToWidth(physicalLine, contentWidth));
        }

        return lines.Count == 0 ? [string.Empty] : lines;
    }
}

internal readonly record struct ControlOutputLine(string Text, bool IsError);
