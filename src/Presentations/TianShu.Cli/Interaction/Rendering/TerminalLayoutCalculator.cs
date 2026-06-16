using System.Text;

namespace TianShu.Cli.Interaction.Rendering;

internal static class TerminalLayoutCalculator
{
    public static int SafeWindowWidth(int fallback = 80)
    {
        try
        {
            return Console.IsOutputRedirected ? fallback : Math.Max(20, Console.WindowWidth);
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (ArgumentOutOfRangeException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Returns a line width that can be written safely with Console.WriteLine without triggering terminal auto-wrap.
    /// 返回适合 Console.WriteLine 的安全行宽，避免正好写满终端宽度时触发自动换行。
    /// </summary>
    public static int SafeWritableWidth(int fallback = 80)
        => Math.Max(1, SafeWindowWidth(fallback) - 1);

    public static string FitToWidth(string value, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        var currentWidth = 0;
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length;)
        {
            if (TryCopyAnsiSequence(value, ref index, builder))
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
            if (currentWidth + runeWidth > width)
            {
                break;
            }

            builder.Append(rune);
            currentWidth += runeWidth;
        }

        if (currentWidth < width)
        {
            builder.Append(' ', width - currentWidth);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<string> WrapToWidth(string value, int width)
    {
        if (width <= 0)
        {
            return [string.Empty];
        }

        if (string.IsNullOrEmpty(value))
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;
        for (var index = 0; index < value.Length;)
        {
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
            if (currentWidth > 0 && currentWidth + runeWidth > width)
            {
                lines.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            current.Append(rune);
            currentWidth += runeWidth;
        }

        lines.Add(current.ToString());
        return lines;
    }

    private static bool TryCopyAnsiSequence(string value, ref int index, StringBuilder builder)
    {
        if (index + 1 >= value.Length || value[index] != '\u001b' || value[index + 1] != '[')
        {
            return false;
        }

        var start = index;
        index += 2;
        while (index < value.Length && value[index] is < '@' or > '~')
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

    public static int GetDisplayWidth(string value)
    {
        var width = 0;
        for (var index = 0; index < value.Length;)
        {
            if (TryCopyAnsiSequence(value, ref index, new StringBuilder()))
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
}
