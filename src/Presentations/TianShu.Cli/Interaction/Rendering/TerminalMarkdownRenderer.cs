using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TianShu.Cli.Terminal;

namespace TianShu.Cli.Interaction.Rendering;

internal static partial class TerminalMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    public static IReadOnlyList<string> RenderLines(string text, bool styled)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [string.Empty];
        }

        var document = Markdown.Parse(text, Pipeline);
        var lines = new List<string>();
        RenderContainerBlocks(document, lines, styled, separateBlocks: true);
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static void RenderContainerBlocks(ContainerBlock container, ICollection<string> lines, bool styled, bool separateBlocks)
    {
        foreach (var block in container)
        {
            var before = lines.Count;
            RenderBlock(block, lines, styled);
            if (separateBlocks && lines.Count > before && ShouldSeparateBlock(block))
            {
                lines.Add(string.Empty);
            }
        }

        TrimTrailingBlankLines(lines);
    }

    private static void RenderBlock(Block block, ICollection<string> lines, bool styled)
    {
        switch (block)
        {
            case HeadingBlock heading:
                RenderHeading(heading, lines, styled);
                break;
            case ParagraphBlock paragraph:
                RenderParagraph(paragraph, lines, styled);
                break;
            case ListBlock list:
                RenderList(list, lines, styled);
                break;
            case FencedCodeBlock fencedCode:
                RenderCodeBlock(fencedCode, lines);
                break;
            case CodeBlock code:
                RenderCodeBlock(code, lines);
                break;
            case QuoteBlock quote:
                RenderQuote(quote, lines, styled);
                break;
            case Table table:
                RenderTable(table, lines, styled);
                break;
            case ThematicBreakBlock:
                lines.Add("────────────────");
                break;
            case ContainerBlock container:
                RenderContainerBlocks(container, lines, styled, separateBlocks: true);
                break;
            case LeafBlock leaf:
                RenderLeafBlock(leaf, lines, styled);
                break;
        }
    }

    private static void RenderHeading(HeadingBlock heading, ICollection<string> lines, bool styled)
    {
        var headingLines = RenderInlineLabelAwareLines(heading.Inline, styled)
            .SelectMany(TerminalMarkdownDenseLineExpander.Expand)
            .Where(static line => line.Trim().Length > 0)
            .ToList();
        if (headingLines.Count == 0)
        {
            return;
        }

        lines.Add(styled ? TerminalAnsi.BoldText(TerminalMarkdownInlineSpacingNormalizer.Normalize(headingLines[0])) : TerminalMarkdownInlineSpacingNormalizer.Normalize(headingLines[0]));
        foreach (var line in headingLines.Skip(1))
        {
            AddTextLine(lines, line);
        }
    }

    private static void RenderParagraph(ParagraphBlock paragraph, ICollection<string> lines, bool styled)
    {
        foreach (var text in RenderInlineLabelAwareLines(paragraph.Inline, styled))
        {
            foreach (var expanded in TerminalMarkdownDenseLineExpander.Expand(text))
            {
                AddTextLine(lines, expanded);
            }
        }
    }

    private static void RenderList(ListBlock list, ICollection<string> lines, bool styled)
    {
        var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var orderedStart)
            ? orderedStart
            : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var itemLines = new List<string>();
            RenderContainerBlocks(item, itemLines, styled, separateBlocks: false);
            if (itemLines.Count == 0)
            {
                continue;
            }

            var prefix = list.IsOrdered ? $"{number}. " : "• ";
            lines.Add(prefix + itemLines[0].TrimStart());
            foreach (var continuation in itemLines.Skip(1))
            {
                lines.Add("  " + continuation);
            }

            number++;
        }
    }

    private static void RenderCodeBlock(LeafBlock codeBlock, ICollection<string> lines)
    {
        var codeLines = ReadLeafBlockLines(codeBlock);
        if (codeLines.Count == 0)
        {
            lines.Add(string.Empty);
            return;
        }

        foreach (var line in codeLines)
        {
            lines.Add(line);
        }
    }

    private static void RenderQuote(QuoteBlock quote, ICollection<string> lines, bool styled)
    {
        var quoteLines = new List<string>();
        RenderContainerBlocks(quote, quoteLines, styled, separateBlocks: true);
        foreach (var line in quoteLines)
        {
            lines.Add("> " + line);
        }
    }

    private static void RenderTable(Table table, ICollection<string> lines, bool styled)
    {
        var rows = table.OfType<TableRow>()
            .Select(row => row.OfType<TableCell>().Select(cell => RenderTableCell(cell, styled)).ToList())
            .Where(row => row.Count > 0)
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var headers = rows[0];
        foreach (var row in rows.Skip(1))
        {
            if (row.Count == 1)
            {
                lines.Add("  • " + row[0]);
                continue;
            }

            if (row.Count == 2)
            {
                var key = string.IsNullOrWhiteSpace(row[0])
                    ? headers.FirstOrDefault() ?? "项目"
                    : row[0];
                lines.Add($"  {key}：{row[1]}");
                continue;
            }

            var cells = row.Select((cell, index) =>
            {
                var header = index < headers.Count && !string.IsNullOrWhiteSpace(headers[index])
                    ? headers[index]
                    : $"列 {index + 1}";
                return $"{header}: {cell}";
            });
            lines.Add("  • " + string.Join("；", cells));
        }
    }

    private static string RenderTableCell(TableCell cell, bool styled)
    {
        var cellLines = new List<string>();
        RenderContainerBlocks(cell, cellLines, styled, separateBlocks: false);
        var text = string.Join(" ", cellLines.Select(static line => line.Trim()).Where(static line => line.Length > 0));
        return TerminalMarkdownInlineSpacingNormalizer.Normalize(text);
    }

    private static void RenderLeafBlock(LeafBlock leaf, ICollection<string> lines, bool styled)
    {
        foreach (var line in ReadLeafBlockLines(leaf))
        {
            AddTextLine(lines, styled ? RenderInlineText(line, styled) : line);
        }
    }

    private static IReadOnlyList<string> ReadLeafBlockLines(LeafBlock leaf)
    {
        var result = new List<string>();
        for (var index = 0; index < leaf.Lines.Count; index++)
        {
            result.Add(leaf.Lines.Lines[index].Slice.ToString());
        }

        return result;
    }

    private static void AddTextLine(ICollection<string> lines, string value)
    {
        var normalized = TerminalMarkdownInlineSpacingNormalizer.Normalize(value);
        var trimmed = normalized.TrimStart();
        var indent = normalized.Length - trimmed.Length;
        if (trimmed.StartsWith("- ", StringComparison.Ordinal)
            || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            lines.Add(new string(' ', indent) + "• " + trimmed[2..]);
            return;
        }

        lines.Add(new string(' ', indent) + trimmed);
    }

    private static string RenderInline(ContainerInline? inline, bool styled)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var child in inline)
        {
            builder.Append(RenderInlineObject(child, styled));
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> RenderInlineLabelAwareLines(ContainerInline? inline, bool styled)
    {
        if (inline is null)
        {
            return [string.Empty];
        }

        var lines = new List<string>();
        var head = new StringBuilder();
        StringBuilder? currentLabelLine = null;
        var hasStrongLabel = false;

        foreach (var child in inline)
        {
            if (TryRenderStrongLabel(child, styled, out var label))
            {
                hasStrongLabel = true;
                if (currentLabelLine is not null)
                {
                    AddCompletedLabelLine(lines, currentLabelLine);
                }
                else
                {
                    AddCompletedHeadLine(lines, head);
                }

                currentLabelLine = new StringBuilder("  ");
                currentLabelLine.Append(label);
                if (!label.EndsWith(' '))
                {
                    currentLabelLine.Append(' ');
                }

                continue;
            }

            var rendered = RenderInlineObject(child, styled);
            if (currentLabelLine is not null)
            {
                AppendContinuation(currentLabelLine, rendered);
            }
            else
            {
                head.Append(rendered);
            }
        }

        if (currentLabelLine is not null)
        {
            AddCompletedLabelLine(lines, currentLabelLine);
        }
        else
        {
            AddCompletedHeadLine(lines, head);
        }

        if (hasStrongLabel)
        {
            return lines;
        }

        var renderedLine = RenderInline(inline, styled);
        var plainLine = styled ? RenderInline(inline, styled: false) : renderedLine;
        var expandedPlainLines = TerminalMarkdownDenseLineExpander.ExpandLabels(plainLine).ToList();
        return expandedPlainLines.Count == 1 && string.Equals(expandedPlainLines[0], plainLine, StringComparison.Ordinal)
            ? [renderedLine]
            : expandedPlainLines;
    }

    private static bool TryRenderStrongLabel(Inline inline, bool styled, out string label)
    {
        label = string.Empty;
        if (inline is not EmphasisInline { DelimiterCount: >= 2 } emphasis)
        {
            return false;
        }

        var plain = RenderInline(emphasis, styled: false).Trim();
        if (!TerminalMarkdownDenseLineExpander.IsStrongLabel(plain))
        {
            return false;
        }

        label = RenderInlineObject(emphasis, styled).Trim();
        return true;
    }

    private static void AddCompletedHeadLine(ICollection<string> lines, StringBuilder builder)
    {
        var value = builder.ToString().Trim();
        if (value.Length > 0)
        {
            lines.Add(value);
        }

        builder.Clear();
    }

    private static void AddCompletedLabelLine(ICollection<string> lines, StringBuilder builder)
    {
        var value = builder.ToString().TrimEnd();
        if (value.Length > 0)
        {
            lines.Add(value);
        }
    }

    private static void AppendContinuation(StringBuilder builder, string value)
    {
        if (builder.Length > 0
            && char.IsWhiteSpace(builder[^1])
            && value.Length > 0
            && char.IsWhiteSpace(value[0]))
        {
            builder.Append(value.TrimStart());
            return;
        }

        builder.Append(value);
    }

    private static string RenderInlineObject(Inline inline, bool styled)
        => inline switch
        {
            LiteralInline literal => literal.Content.ToString(),
            CodeInline code => styled ? TerminalAnsi.BlueText(code.Content) : code.Content,
            EmphasisInline emphasis => RenderEmphasis(emphasis, styled),
            LinkInline link => RenderLink(link, styled),
            LineBreakInline => " ",
            HtmlInline html => html.Tag,
            AutolinkInline autolink => autolink.Url,
            ContainerInline container => RenderInline(container, styled),
            _ => string.Empty,
        };

    private static string RenderEmphasis(EmphasisInline emphasis, bool styled)
    {
        var content = RenderInline(emphasis, styled);
        return styled && emphasis.DelimiterCount >= 2
            ? TerminalAnsi.BoldText(content)
            : content;
    }

    private static string RenderLink(LinkInline link, bool styled)
    {
        var label = RenderInline(link, styled);
        if (string.IsNullOrWhiteSpace(link.Url) || string.Equals(label, link.Url, StringComparison.Ordinal))
        {
            return label;
        }

        return $"{label} ({link.Url})";
    }

    private static string RenderInlineText(string value, bool styled)
    {
        var document = Markdown.Parse(value, Pipeline);
        var lines = new List<string>();
        RenderContainerBlocks(document, lines, styled, separateBlocks: false);
        return string.Join(" ", lines);
    }

    private static bool ShouldSeparateBlock(Block block)
        => block is ParagraphBlock or HeadingBlock or ListBlock or Table or QuoteBlock or CodeBlock;

    private static void TrimTrailingBlankLines(ICollection<string> lines)
    {
        while (lines is List<string> list && list.Count > 0 && list[^1].Length == 0)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

}
