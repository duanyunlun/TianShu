using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using FlowDocumentList = System.Windows.Documents.List;

namespace TianShu.VSSDK.VSExtension;

internal static class ConversationMarkdownRenderer
{
    private static readonly char[] DisallowedPathChars =
    [
        .. Path.GetInvalidPathChars(),
        '"',
        '<',
        '>',
        '|',
        '*',
        '?',
    ];

    private static readonly Regex MarkdownLinkRegex = new(@"\[(?<label>[^\]\r\n]+)\]\((?<target>[^)\r\n]+)\)", RegexOptions.Compiled);
    private static readonly Regex CodeSpanRegex = new(@"`(?<code>[^`\r\n]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(?<text>.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex FileReferenceRegex = new(
        @"(?<file>(?:[A-Za-z]:[\\/]|(?:\.{0,2}[\\/])?(?:[^\\/\s\[\]()]+[\\/])+)[^\\/\s\[\]()]+?\.[A-Za-z0-9]+(?:#L\d+(?:C\d+)?)?(?::\d+(?::\d+)?)?)",
        RegexOptions.Compiled);
    private static readonly Regex OrderedListRegex = new(@"^\s*(?<index>\d+)\.\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletListRegex = new(@"^\s*[-*+]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^(?<marks>#{1,6})\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex LineAnchorRegex = new(@"#L(?<line>\d+)(?:C(?<column>\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingLineRegex = new(@"^(?<path>.+?)(?::(?<line>\d+)(?::(?<column>\d+))?)$", RegexOptions.Compiled);

    public static FlowDocument BuildDocument(
        string markdown,
        string workingDirectory,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = theme.ForegroundBrush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            LineHeight = 20,
        };

        var lines = NormalizeLineEndings(markdown)
            .Split(new[] { '\n' }, StringSplitOptions.None);

        var index = 0;
        while (index < lines.Length)
        {
            if (TryReadCodeBlock(lines, ref index, out var language, out var code))
            {
                document.Blocks.Add(CreateCodeBlock(language, code, theme));
                continue;
            }

            if (TryReadList(lines, ref index, workingDirectory, theme, onLinkActivated, out var listBlock))
            {
                document.Blocks.Add(listBlock);
                continue;
            }

            if (TryReadHeading(lines, ref index, workingDirectory, theme, onLinkActivated, out var headingBlock))
            {
                document.Blocks.Add(headingBlock);
                continue;
            }

            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            document.Blocks.Add(ReadParagraph(lines, ref index, workingDirectory, theme, onLinkActivated));
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }

        return document;
    }

    internal static bool TryParseLinkTarget(string rawTarget, string workingDirectory, out ConversationLinkTarget target)
    {
        target = ConversationLinkTarget.Empty;
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            return false;
        }

        var trimmed = rawTarget.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri)
            && (absoluteUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || absoluteUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            target = ConversationLinkTarget.CreateExternal(absoluteUri.AbsoluteUri);
            return true;
        }

        var candidate = trimmed;
        int? line = null;
        int? column = null;

        var anchorMatch = LineAnchorRegex.Match(candidate);
        if (anchorMatch.Success)
        {
            candidate = candidate.Substring(0, anchorMatch.Index);
            line = TryParseInt(anchorMatch.Groups["line"].Value);
            column = TryParseInt(anchorMatch.Groups["column"].Value);
        }
        else
        {
            var trailingLineMatch = TrailingLineRegex.Match(candidate);
            if (trailingLineMatch.Success)
            {
                var lineValue = TryParseInt(trailingLineMatch.Groups["line"].Value);
                if (lineValue is not null)
                {
                    candidate = trailingLineMatch.Groups["path"].Value;
                    line = lineValue;
                    column = TryParseInt(trailingLineMatch.Groups["column"].Value);
                }
            }
        }

        var decodedCandidate = Uri.UnescapeDataString(candidate.Trim());
        var resolvedPath = ResolveCandidatePath(decodedCandidate, workingDirectory);
        if (resolvedPath is null)
        {
            return false;
        }

        var displayPath = BuildDisplayPath(resolvedPath, workingDirectory);
        var displayText = displayPath + BuildAnchorSuffix(line, column);
        target = ConversationLinkTarget.CreateFile(resolvedPath, displayText, line, column);
        return true;
    }

    private static Paragraph ReadParagraph(
        IReadOnlyList<string> lines,
        ref int index,
        string workingDirectory,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated)
    {
        var collectedLines = new List<string>();
        while (index < lines.Count)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                break;
            }

            if (line.StartsWith("```", StringComparison.Ordinal)
                || OrderedListRegex.IsMatch(line)
                || BulletListRegex.IsMatch(line)
                || HeadingRegex.IsMatch(line))
            {
                break;
            }

            collectedLines.Add(line.TrimEnd());
            index++;
        }

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = theme.ForegroundBrush,
        };

        AppendInlineElements(
            paragraph.Inlines,
            string.Join(Environment.NewLine, collectedLines).Trim(),
            workingDirectory,
            theme,
            onLinkActivated);
        return paragraph;
    }

    private static bool TryReadHeading(
        IReadOnlyList<string> lines,
        ref int index,
        string workingDirectory,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated,
        out Paragraph paragraph)
    {
        paragraph = null!;
        if (index >= lines.Count)
        {
            return false;
        }

        var match = HeadingRegex.Match(lines[index]);
        if (!match.Success)
        {
            return false;
        }

        var level = Math.Max(1, Math.Min(6, match.Groups["marks"].Value.Length));
        paragraph = new Paragraph
        {
            Margin = new Thickness(0, level <= 2 ? 8 : 4, 0, 6),
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.ForegroundBrush,
            FontSize = level switch
            {
                1 => 16,
                2 => 15,
                3 => 14,
                _ => 13,
            },
        };

        AppendInlineElements(
            paragraph.Inlines,
            match.Groups["text"].Value.Trim(),
            workingDirectory,
            theme,
            onLinkActivated);
        index++;
        return true;
    }

    private static bool TryReadList(
        IReadOnlyList<string> lines,
        ref int index,
        string workingDirectory,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated,
        out FlowDocumentList list)
    {
        list = null!;
        if (index >= lines.Count)
        {
            return false;
        }

        var orderedMatch = OrderedListRegex.Match(lines[index]);
        var bulletMatch = BulletListRegex.Match(lines[index]);
        if (!orderedMatch.Success && !bulletMatch.Success)
        {
            return false;
        }

        var isOrdered = orderedMatch.Success;
        list = new FlowDocumentList
        {
            MarkerStyle = isOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = theme.ForegroundBrush,
        };

        while (index < lines.Count)
        {
            var currentLine = lines[index];
            var currentMatch = isOrdered ? OrderedListRegex.Match(currentLine) : BulletListRegex.Match(currentLine);
            if (!currentMatch.Success)
            {
                break;
            }

            var itemLines = new List<string> { currentMatch.Groups["text"].Value.TrimEnd() };
            index++;

            while (index < lines.Count)
            {
                var continuation = lines[index];
                if (string.IsNullOrWhiteSpace(continuation))
                {
                    index++;
                    break;
                }

                if ((isOrdered && OrderedListRegex.IsMatch(continuation)) || (!isOrdered && BulletListRegex.IsMatch(continuation)))
                {
                    break;
                }

                if (HeadingRegex.IsMatch(continuation) || continuation.StartsWith("```", StringComparison.Ordinal))
                {
                    break;
                }

                itemLines.Add(continuation.Trim());
                index++;
            }

            var itemParagraph = new Paragraph
            {
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = theme.ForegroundBrush,
            };
            AppendInlineElements(
                itemParagraph.Inlines,
                string.Join(Environment.NewLine, itemLines).Trim(),
                workingDirectory,
                theme,
                onLinkActivated);
            list.ListItems.Add(new ListItem(itemParagraph));
        }

        return list.ListItems.Count > 0;
    }

    private static bool TryReadCodeBlock(IReadOnlyList<string> lines, ref int index, out string language, out string code)
    {
        language = string.Empty;
        code = string.Empty;
        if (index >= lines.Count)
        {
            return false;
        }

        var line = lines[index];
        if (!line.StartsWith("```", StringComparison.Ordinal))
        {
            return false;
        }

        language = line.Substring(3).Trim();
        index++;

        var blockLines = new List<string>();
        while (index < lines.Count && !lines[index].StartsWith("```", StringComparison.Ordinal))
        {
            blockLines.Add(lines[index]);
            index++;
        }

        if (index < lines.Count && lines[index].StartsWith("```", StringComparison.Ordinal))
        {
            index++;
        }

        code = string.Join(Environment.NewLine, blockLines);
        return true;
    }

    private static Block CreateCodeBlock(string language, string code, ConversationMarkdownTheme theme)
    {
        var textBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language)
                ? code
                : $"{language}{Environment.NewLine}{code}",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.5,
            LineHeight = 18,
            Foreground = theme.ForegroundBrush,
        };

        var border = new Border
        {
            Margin = new Thickness(0, 2, 0, 8),
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(4),
            Background = theme.CodeBlockBackgroundBrush,
            BorderBrush = theme.BorderBrush,
            BorderThickness = new Thickness(1),
            Child = textBlock,
        };

        return new BlockUIContainer(border);
    }

    private static void AppendInlineElements(
        InlineCollection inlines,
        string text,
        string workingDirectory,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var index = 0;
        while (index < text.Length)
        {
            var markdownLinkMatch = MarkdownLinkRegex.Match(text, index);
            var codeMatch = CodeSpanRegex.Match(text, index);
            var boldMatch = BoldRegex.Match(text, index);
            var fileMatch = FileReferenceRegex.Match(text, index);

            var match = GetEarliestMatch(markdownLinkMatch, codeMatch, boldMatch, fileMatch);
            if (match is null)
            {
                AppendPlainText(inlines, text.Substring(index), theme);
                break;
            }

            if (match.Index > index)
            {
                AppendPlainText(inlines, text.Substring(index, match.Index - index), theme);
            }

            if (ReferenceEquals(match, markdownLinkMatch))
            {
                var label = markdownLinkMatch.Groups["label"].Value;
                var target = markdownLinkMatch.Groups["target"].Value;
                if (TryParseLinkTarget(target, workingDirectory, out var linkTarget))
                {
                    inlines.Add(CreateHyperlink(
                        linkTarget,
                        linkTarget.IsExternal ? label : linkTarget.DisplayText,
                        theme,
                        onLinkActivated));
                }
                else
                {
                    AppendPlainText(inlines, label, theme);
                }
            }
            else if (ReferenceEquals(match, codeMatch))
            {
                inlines.Add(CreateInlineCode(codeMatch.Groups["code"].Value, theme));
            }
            else if (ReferenceEquals(match, boldMatch))
            {
                inlines.Add(new Bold(new Run(boldMatch.Groups["text"].Value)
                {
                    Foreground = theme.ForegroundBrush,
                }));
            }
            else
            {
                var rawFileReference = fileMatch.Groups["file"].Value;
                if (TryParseLinkTarget(rawFileReference, workingDirectory, out var fileTarget))
                {
                    inlines.Add(CreateHyperlink(fileTarget, fileTarget.DisplayText, theme, onLinkActivated));
                }
                else
                {
                    AppendPlainText(inlines, rawFileReference, theme);
                }
            }

            index = match.Index + match.Length;
        }
    }

    private static Match? GetEarliestMatch(params Match[] matches)
    {
        Match? result = null;
        foreach (var match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            if (result is null || match.Index < result.Index)
            {
                result = match;
            }
        }

        return result;
    }

    private static void AppendPlainText(InlineCollection inlines, string value, ConversationMarkdownTheme theme)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var segments = value.Split(new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
        for (var i = 0; i < segments.Length; i++)
        {
            if (segments[i].Length > 0)
            {
                inlines.Add(new Run(segments[i])
                {
                    Foreground = theme.ForegroundBrush,
                });
            }

            if (i < segments.Length - 1)
            {
                inlines.Add(new LineBreak());
            }
        }
    }

    private static Inline CreateInlineCode(string code, ConversationMarkdownTheme theme)
    {
        return new Run(code)
        {
            FontFamily = new FontFamily("Consolas"),
            Background = theme.InlineCodeBackgroundBrush,
            Foreground = theme.ForegroundBrush,
        };
    }

    private static Hyperlink CreateHyperlink(
        ConversationLinkTarget target,
        string label,
        ConversationMarkdownTheme theme,
        Action<ConversationLinkTarget> onLinkActivated)
    {
        var hyperlink = new Hyperlink(new Run(label))
        {
            Cursor = Cursors.Hand,
            Foreground = theme.LinkBrush,
            ToolTip = target.ToolTipText,
        };
        hyperlink.Click += (_, _) => onLinkActivated(target);
        return hyperlink;
    }

    private static string NormalizeLineEndings(string markdown)
        => (markdown ?? string.Empty).Replace("\r\n", "\n");

    private static string? ResolveCandidatePath(string candidatePath, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        try
        {
            var sanitizedCandidate = SanitizePathCandidate(candidatePath);
            if (string.IsNullOrWhiteSpace(sanitizedCandidate))
            {
                return null;
            }

            var normalizedCandidate = sanitizedCandidate
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (normalizedCandidate.IndexOfAny(DisallowedPathChars) >= 0)
            {
                return null;
            }

            string fullPath;
            if (Path.IsPathRooted(normalizedCandidate))
            {
                fullPath = Path.GetFullPath(normalizedCandidate);
            }
            else
            {
                var relativeCandidate = normalizedCandidate.TrimStart(Path.DirectorySeparatorChar);
                fullPath = Path.GetFullPath(Path.Combine(workingDirectory, relativeCandidate));
            }

            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string SanitizePathCandidate(string candidatePath)
    {
        var result = candidatePath.Trim();
        while (result.Length > 0 && IsTrimmedLeadingPathPunctuation(result[0]))
        {
            result = result.Substring(1).TrimStart();
        }

        while (result.Length > 0 && IsTrimmedTrailingPathPunctuation(result[result.Length - 1]))
        {
            result = result.Substring(0, result.Length - 1).TrimEnd();
        }

        return result;
    }

    private static bool IsTrimmedLeadingPathPunctuation(char value)
        => value is '"' or '\'' or ':' or ';' or ',' or '(' or '[' or '{' or '<';

    private static bool IsTrimmedTrailingPathPunctuation(char value)
        => value is '"' or '\'' or ',' or ';' or ')' or ']' or '}' or '>';

    private static string BuildDisplayPath(string absolutePath, string workingDirectory)
    {
        var normalizedAbsolutePath = Path.GetFullPath(absolutePath);
        var normalizedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
            ? string.Empty
            : Path.GetFullPath(workingDirectory);

        var displayPath = normalizedAbsolutePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!string.IsNullOrWhiteSpace(normalizedWorkingDirectory))
        {
            try
            {
                var workingDirectoryUri = new Uri(AppendDirectorySeparator(normalizedWorkingDirectory));
                var fileUri = new Uri(normalizedAbsolutePath);
                if (string.Equals(
                    workingDirectoryUri.Scheme,
                    fileUri.Scheme,
                    StringComparison.OrdinalIgnoreCase))
                {
                    displayPath = Uri.UnescapeDataString(workingDirectoryUri.MakeRelativeUri(fileUri).ToString());
                }
            }
            catch
            {
                displayPath = normalizedAbsolutePath.Replace(Path.DirectorySeparatorChar, '/');
            }
        }

        displayPath = displayPath.Replace('\\', '/');
        return string.Join(
            "/",
            displayPath
                .Split(new[] { '/' }, StringSplitOptions.None)
                .Select(Uri.EscapeDataString));
    }

    private static string AppendDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string BuildAnchorSuffix(int? line, int? column)
    {
        if (line is null)
        {
            return string.Empty;
        }

        return column is null ? $"#L{line.Value}" : $"#L{line.Value}C{column.Value}";
    }

    private static int? TryParseInt(string value)
        => int.TryParse(value, out var number) && number > 0 ? number : null;
}

internal sealed class ConversationMarkdownTheme
{
    public ConversationMarkdownTheme(Brush foregroundBrush, Brush linkBrush, Brush inlineCodeBackgroundBrush, Brush codeBlockBackgroundBrush, Brush borderBrush)
    {
        ForegroundBrush = foregroundBrush;
        LinkBrush = linkBrush;
        InlineCodeBackgroundBrush = inlineCodeBackgroundBrush;
        CodeBlockBackgroundBrush = codeBlockBackgroundBrush;
        BorderBrush = borderBrush;
    }

    public Brush ForegroundBrush { get; }

    public Brush LinkBrush { get; }

    public Brush InlineCodeBackgroundBrush { get; }

    public Brush CodeBlockBackgroundBrush { get; }

    public Brush BorderBrush { get; }
}

internal readonly struct ConversationLinkTarget
{
    private ConversationLinkTarget(string? absolutePath, string displayText, int? line, int? column, string? externalUri)
    {
        AbsolutePath = absolutePath;
        DisplayText = displayText;
        Line = line;
        Column = column;
        ExternalUri = externalUri;
    }

    public static ConversationLinkTarget Empty => new(null, string.Empty, null, null, null);

    public string? AbsolutePath { get; }

    public string DisplayText { get; }

    public int? Line { get; }

    public int? Column { get; }

    public string? ExternalUri { get; }

    public bool IsExternal => !string.IsNullOrWhiteSpace(ExternalUri);

    public string ToolTipText => IsExternal
        ? ExternalUri ?? string.Empty
        : AbsolutePath ?? string.Empty;

    public static ConversationLinkTarget CreateFile(string absolutePath, string displayText, int? line, int? column)
        => new(absolutePath, displayText, line, column, null);

    public static ConversationLinkTarget CreateExternal(string externalUri)
        => new(null, externalUri, null, null, externalUri);
}
