using System.Text.RegularExpressions;

namespace TianShu.Cli.Interaction.Rendering;

internal static partial class TerminalMarkdownDenseLineExpander
{
    public static IEnumerable<string> Expand(string line)
    {
        foreach (var headingSegment in SplitHeadings(line))
        {
            foreach (var labelSegment in ExpandLabels(headingSegment))
            {
                foreach (var itemSegment in ExpandSeparatorItems(labelSegment))
                {
                    yield return itemSegment;
                }
            }
        }
    }

    public static IEnumerable<string> ExpandLabels(string line)
    {
        var matches = DenseLabelRegex().Matches(line);
        if (matches.Count == 0)
        {
            yield return line;
            yield break;
        }

        var first = matches[0].Index;
        var head = line[..first].Trim();
        if (head.Length > 0)
        {
            yield return head;
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : line.Length;
            var segment = line[start..end].Trim();
            if (segment.Length > 0)
            {
                yield return "  " + NormalizeLabelSpacing(segment);
            }
        }
    }

    public static bool IsStrongLabel(string value)
        => LabelOnlyRegex().IsMatch(value);

    private static IEnumerable<string> SplitHeadings(string line)
    {
        var matches = DenseHeadingRegex().Matches(line);
        if (matches.Count == 0)
        {
            yield return line;
            yield break;
        }

        var cursor = 0;
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            if (start > cursor)
            {
                var head = line[cursor..start].Trim();
                if (head.Length > 0)
                {
                    yield return head;
                }
            }

            var end = index + 1 < matches.Count ? matches[index + 1].Index : line.Length;
            var segment = line[start..end].Trim();
            if (segment.Length > 0)
            {
                yield return segment.TrimStart('#').Trim();
            }

            cursor = end;
        }
    }

    private static string NormalizeLabelSpacing(string value)
    {
        var colon = value.IndexOfAny([':', '：']);
        if (colon < 0 || colon + 1 >= value.Length || char.IsWhiteSpace(value[colon + 1]))
        {
            return value;
        }

        return value[..(colon + 1)] + " " + value[(colon + 1)..];
    }

    private static IEnumerable<string> ExpandSeparatorItems(string line)
    {
        var matches = DenseSeparatorRegex().Matches(line);
        var trimmed = line.TrimStart();
        if (matches.Count == 0
            || (matches.Count == 1 && !trimmed.StartsWith("##", StringComparison.Ordinal)))
        {
            var compactItems = ExpandCompactHyphenItems(line).ToList();
            if (compactItems.Count > 0)
            {
                foreach (var compactItem in compactItems)
                {
                    yield return compactItem;
                }

                yield break;
            }

            yield return line;
            yield break;
        }

        var first = matches[0].Index;
        var head = line[..first].Trim();
        if (head.Length > 0)
        {
            yield return head.TrimStart('#').Trim();
        }

        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index + matches[index].Length;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : line.Length;
            var item = line[start..end].Trim();
            if (item.Length > 0)
            {
                yield return "  - " + item;
            }
        }
    }

    private static IEnumerable<string> ExpandCompactHyphenItems(string line)
    {
        var match = DenseColonHyphenListRegex().Match(line);
        if (!match.Success)
        {
            yield break;
        }

        var prefix = match.Groups["prefix"].Value.TrimEnd();
        var value = match.Groups["value"].Value.Trim();
        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            yield break;
        }

        if (TrySplitTrailingLabelItem(prefix, out var prefixItem, out var prefixLabel))
        {
            yield return "  • " + prefixItem;
            yield return "  " + prefixLabel;
        }
        else
        {
            yield return prefix;
        }

        foreach (var part in parts)
        {
            if (TrySplitTrailingLabelItem(part, out var item, out var label))
            {
                yield return "  • " + item;
                yield return "  " + label;
                continue;
            }

            yield return "  • " + part;
        }
    }

    private static bool TrySplitTrailingLabelItem(string value, out string item, out string label)
    {
        item = string.Empty;
        label = string.Empty;

        var text = value.Trim();
        var colonIndex = text.LastIndexOfAny([':', '：']);
        if (colonIndex <= 0 || colonIndex != text.Length - 1)
        {
            return false;
        }

        if (!TryResolveLabelSuffixLength(text[..colonIndex], out var labelLength))
        {
            return false;
        }

        var labelStart = colonIndex - labelLength;
        item = text[..labelStart].Trim();
        label = text[labelStart..].Trim();
        return item.Length > 0 && label.Length > 0;
    }

    private static bool TryResolveLabelSuffixLength(string value, out int labelLength)
    {
        labelLength = 0;
        if (value.Length < 4)
        {
            return false;
        }

        var suffixEnd = value.Length;
        var suffixStart = suffixEnd;
        while (suffixStart > 0 && IsCjk(value[suffixStart - 1]))
        {
            suffixStart--;
        }

        var cjkLength = suffixEnd - suffixStart;
        if (cjkLength < 6)
        {
            return false;
        }

        labelLength = Math.Clamp(cjkLength / 2, 2, 6);
        return suffixStart > 0 || cjkLength > labelLength;
    }

    private static bool IsCjk(char value)
        => value is >= '\u4e00' and <= '\u9fff';

    [GeneratedRegex(@"^[\p{IsCJKUnifiedIdeographs}A-Za-z][\p{IsCJKUnifiedIdeographs}A-Za-z0-9 _/\-]{1,47}[:：]$")]
    private static partial Regex LabelOnlyRegex();

    [GeneratedRegex(@"\p{IsCJKUnifiedIdeographs}[\p{IsCJKUnifiedIdeographs}A-Za-z0-9 _/\-]{1,47}[:：](?![\\/])")]
    private static partial Regex DenseLabelRegex();

    [GeneratedRegex(@"(?<=\S)#{2,6}(?=\s*\S)")]
    private static partial Regex DenseHeadingRegex();

    [GeneratedRegex(@"\s+[—-]\s+")]
    private static partial Regex DenseSeparatorRegex();

    [GeneratedRegex(@"^(?<prefix>\s{0,4}[^:：]{1,48}[:：])\s*(?<value>-[^\r\n]+)$")]
    private static partial Regex DenseColonHyphenListRegex();
}
