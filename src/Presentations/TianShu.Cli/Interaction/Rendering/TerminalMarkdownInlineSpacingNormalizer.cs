using System.Text.RegularExpressions;

namespace TianShu.Cli.Interaction.Rendering;

internal static partial class TerminalMarkdownInlineSpacingNormalizer
{
    public static string Normalize(string value)
    {
        var normalized = UrlSchemeWhitespaceRegex().Replace(value, "$1://");
        normalized = DotNetVersionSdkRegex().Replace(normalized, ".NET $1 SDK");
        normalized = DotNetSdkRegex().Replace(normalized, ".NET SDK");
        normalized = WpfHttpRegex().Replace(normalized, "WPF HTTP");
        normalized = CjkBeforeTechTokenRegex().Replace(normalized, "$1 $2");
        normalized = TechTokenBeforeCjkRegex().Replace(normalized, "$1 $2");
        return CjkPunctuationSpacingRegex().Replace(normalized, "$1");
    }

    [GeneratedRegex(@"\b(https?):\s+//", RegexOptions.IgnoreCase)]
    private static partial Regex UrlSchemeWhitespaceRegex();

    [GeneratedRegex(@"([，、。；！？])\s+")]
    private static partial Regex CjkPunctuationSpacingRegex();

    [GeneratedRegex(@"\.NET\s*(\d+(?:\.\d+)*)\s*SDK", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetVersionSdkRegex();

    [GeneratedRegex(@"\.NET\s*SDK", RegexOptions.IgnoreCase)]
    private static partial Regex DotNetSdkRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])WPF\s*HTTP(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex WpfHttpRegex();

    [GeneratedRegex(@"([\p{IsCJKUnifiedIdeographs}])(\.NET(?: \d+(?:\.\d+)*)? SDK|WPF HTTP|GET/POST|POST|GET|HTTP)", RegexOptions.IgnoreCase)]
    private static partial Regex CjkBeforeTechTokenRegex();

    [GeneratedRegex(@"(\.NET(?: \d+(?:\.\d+)*)? SDK|WPF HTTP|GET/POST|POST|GET|HTTP)([\p{IsCJKUnifiedIdeographs}])", RegexOptions.IgnoreCase)]
    private static partial Regex TechTokenBeforeCjkRegex();
}
