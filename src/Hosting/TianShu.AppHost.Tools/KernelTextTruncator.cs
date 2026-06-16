using System.Text;

namespace TianShu.AppHost.Tools;

/// <summary>
/// 提供宿主工具输出的字符级与近似 token 级裁剪能力，避免长输出反向撑爆 northbound 载荷。
/// Provides character-level and approximate token-level truncation for host-tool output so long payloads do not bloat northbound projections.
/// </summary>
internal static class KernelTextTruncator
{
    private const int ApproxBytesPerToken = 4;

    public static string FormattedTruncate(string content, int maxChars)
    {
        if (maxChars < 0)
        {
            maxChars = 0;
        }

        if (content.Length <= maxChars)
        {
            return content;
        }

        var totalLines = content.Length == 0 ? 0 : content.Count(static ch => ch == '\n') + 1;
        var truncated = Truncate(content, maxChars);
        return $"Total output lines: {totalLines}\n\n{truncated}";
    }

    public static string Truncate(string content, int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (content.Length <= maxChars)
        {
            return content;
        }

        var leftBudget = maxChars / 2;
        var rightBudget = maxChars - leftBudget;

        var removedChars = content.Length - leftBudget - rightBudget;
        var marker = $"…{Math.Max(removedChars, 0)} chars truncated…";

        var maxContentChars = Math.Max(maxChars - marker.Length, 0);
        if (maxContentChars == 0)
        {
            return marker.Length <= maxChars ? marker : marker[..Math.Min(marker.Length, maxChars)];
        }

        leftBudget = maxContentChars / 2;
        rightBudget = maxContentChars - leftBudget;

        var left = content[..leftBudget];
        var right = rightBudget == 0 ? string.Empty : content[^rightBudget..];
        removedChars = content.Length - left.Length - right.Length;
        marker = $"…{Math.Max(removedChars, 0)} chars truncated…";

        while (left.Length + marker.Length + right.Length > maxChars && right.Length > 0)
        {
            right = right[1..];
        }

        return string.Concat(left, marker, right);
    }

    public static int ApproxTokenCount(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var byteCount = Encoding.UTF8.GetByteCount(content);
        return ApproxTokensFromByteCount(byteCount);
    }

    public static int ApproxBytesForTokens(int tokens)
    {
        if (tokens <= 0)
        {
            return 0;
        }

        return checked(tokens * ApproxBytesPerToken);
    }

    public static string FormattedTruncateTokens(string content, int maxTokens, out int? originalTokenCount)
    {
        if (string.IsNullOrEmpty(content))
        {
            originalTokenCount = null;
            return string.Empty;
        }

        maxTokens = Math.Max(maxTokens, 0);
        if (Encoding.UTF8.GetByteCount(content) <= ApproxBytesForTokens(maxTokens))
        {
            originalTokenCount = null;
            return content;
        }

        var totalLines = content.Count(static ch => ch == '\n') + 1;
        var truncated = TruncateTokens(content, maxTokens, out originalTokenCount);
        return $"Total output lines: {totalLines}\n\n{truncated}";
    }

    public static string TruncateTokens(string content, int maxTokens, out int? originalTokenCount)
    {
        if (string.IsNullOrEmpty(content))
        {
            originalTokenCount = null;
            return string.Empty;
        }

        maxTokens = Math.Max(maxTokens, 0);
        var maxBytes = ApproxBytesForTokens(maxTokens);
        var totalBytes = Encoding.UTF8.GetByteCount(content);
        if (maxTokens > 0 && totalBytes <= maxBytes)
        {
            originalTokenCount = null;
            return content;
        }

        var truncated = TruncateWithTokenBudget(content, maxBytes);
        if (string.Equals(truncated, content, StringComparison.Ordinal))
        {
            originalTokenCount = null;
            return content;
        }

        originalTokenCount = ApproxTokenCount(content);
        return truncated;
    }

    public static IReadOnlyList<KernelToolOutputContentItem> MergeTextItemsAndTruncateByTokens(
        IReadOnlyList<KernelToolOutputContentItem> items,
        int maxTokens,
        out int? originalTokenCount)
    {
        var textSegments = items
            .Where(static item => string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Text ?? string.Empty)
            .ToArray();
        if (textSegments.Length == 0)
        {
            originalTokenCount = null;
            return items;
        }

        var combined = string.Join("\n", textSegments);
        if (Encoding.UTF8.GetByteCount(combined) <= ApproxBytesForTokens(Math.Max(maxTokens, 0)))
        {
            originalTokenCount = null;
            return items.ToArray();
        }

        var output = new List<KernelToolOutputContentItem>
        {
            new("input_text", Text: FormattedTruncateTokens(combined, maxTokens, out originalTokenCount))
        };
        output.AddRange(items.Where(static item => string.Equals(item.Type, "input_image", StringComparison.OrdinalIgnoreCase)));
        return output;
    }

    public static IReadOnlyList<KernelToolOutputContentItem> TruncateFunctionOutputItemsByTokens(
        IReadOnlyList<KernelToolOutputContentItem> items,
        int maxTokens)
    {
        var remainingBudget = Math.Max(maxTokens, 0);
        var output = new List<KernelToolOutputContentItem>(items.Count);
        var omittedTextItems = 0;

        foreach (var item in items)
        {
            if (!string.Equals(item.Type, "input_text", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(item);
                continue;
            }

            var text = item.Text ?? string.Empty;
            if (remainingBudget == 0)
            {
                omittedTextItems++;
                continue;
            }

            var cost = ApproxTokenCount(text);
            if (cost <= remainingBudget)
            {
                output.Add(item);
                remainingBudget = Math.Max(remainingBudget - cost, 0);
                continue;
            }

            var snippet = TruncateTokens(text, remainingBudget, out _);
            if (string.IsNullOrEmpty(snippet))
            {
                omittedTextItems++;
            }
            else
            {
                output.Add(item with { Text = snippet });
            }

            remainingBudget = 0;
        }

        if (omittedTextItems > 0)
        {
            output.Add(new KernelToolOutputContentItem("input_text", Text: $"[omitted {omittedTextItems} text items ...]"));
        }

        return output;
    }

    private static string TruncateWithTokenBudget(string content, int maxBytes)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (maxBytes <= 0)
        {
            return $"…{ApproxTokenCount(content)} tokens truncated…";
        }

        if (Encoding.UTF8.GetByteCount(content) <= maxBytes)
        {
            return content;
        }

        var (prefixLength, suffixStart, removedBytes) = SplitUtf8ByBudget(content, maxBytes / 2, maxBytes - (maxBytes / 2));
        var prefix = prefixLength <= 0 ? string.Empty : content[..prefixLength];
        var suffix = suffixStart >= content.Length ? string.Empty : content[suffixStart..];
        var removedTokens = ApproxTokensFromByteCount(removedBytes);
        var marker = $"…{removedTokens} tokens truncated…";
        return string.Concat(prefix, marker, suffix);
    }

    private static (int PrefixLength, int SuffixStart, int RemovedBytes) SplitUtf8ByBudget(string content, int prefixBudget, int suffixBudget)
    {
        var runes = new List<(int Start, int End, int Utf8Bytes)>();
        var charIndex = 0;
        foreach (var rune in content.EnumerateRunes())
        {
            var end = charIndex + rune.Utf16SequenceLength;
            runes.Add((charIndex, end, rune.Utf8SequenceLength));
            charIndex = end;
        }

        var prefixLength = 0;
        var prefixBytes = 0;
        foreach (var rune in runes)
        {
            if (prefixBytes + rune.Utf8Bytes > prefixBudget)
            {
                break;
            }

            prefixBytes += rune.Utf8Bytes;
            prefixLength = rune.End;
        }

        var suffixStart = content.Length;
        var suffixBytes = 0;
        for (var index = runes.Count - 1; index >= 0; index--)
        {
            var rune = runes[index];
            if (rune.Start < prefixLength || suffixBytes + rune.Utf8Bytes > suffixBudget)
            {
                break;
            }

            suffixBytes += rune.Utf8Bytes;
            suffixStart = rune.Start;
        }

        if (suffixStart < prefixLength)
        {
            suffixStart = prefixLength;
        }

        var removedBytes = suffixStart <= prefixLength
            ? 0
            : Encoding.UTF8.GetByteCount(content.AsSpan(prefixLength, suffixStart - prefixLength));
        return (prefixLength, suffixStart, removedBytes);
    }

    private static int ApproxTokensFromByteCount(int byteCount)
    {
        if (byteCount <= 0)
        {
            return 0;
        }

        return checked((byteCount + ApproxBytesPerToken - 1) / ApproxBytesPerToken);
    }
}
