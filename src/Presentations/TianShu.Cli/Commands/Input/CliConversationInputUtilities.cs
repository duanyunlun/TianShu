using System.Text;
using TianShu.Contracts.Conversations;

namespace TianShu.Cli;

/// <summary>
/// CLI 对话输入工具，统一处理文本/图片/链接输入的 contracts-first 映射。
/// CLI conversation-input helpers that normalize text, image, and linked inputs into contracts-first shapes.
/// </summary>
internal static class CliConversationInputUtilities
{
    private sealed record DecodedLinkedInputs(
        string Text,
        IReadOnlyList<ControlPlaneInputItem> LinkedInputs);

    public static IReadOnlyList<ControlPlaneInputItem> BuildTextAndImageInputs(IReadOnlyList<string> imagePaths, string? text)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);

        var structuredTextInputs = BuildStructuredInputsFromText(text);
        var inputs = new List<ControlPlaneInputItem>(imagePaths.Count + structuredTextInputs.Count);
        foreach (var imagePath in imagePaths)
        {
            inputs.Add(new ControlPlaneLocalImageInput(imagePath));
        }

        inputs.AddRange(structuredTextInputs);
        return inputs;
    }

    public static IReadOnlyList<ControlPlaneInputItem> ReplaceStructuredInputs(
        IReadOnlyList<ControlPlaneInputItem> existingInputs,
        string? text)
    {
        ArgumentNullException.ThrowIfNull(existingInputs);

        var replacementInputs = BuildStructuredInputsFromText(text);
        var inputs = new List<ControlPlaneInputItem>(existingInputs.Count + replacementInputs.Count);
        var replacedStructuredInputs = false;
        foreach (var input in existingInputs)
        {
            if (input is ControlPlaneTextInput or ControlPlaneSkillInput or ControlPlaneMentionInput)
            {
                if (replacedStructuredInputs)
                {
                    continue;
                }

                inputs.AddRange(replacementInputs);
                replacedStructuredInputs = true;
                continue;
            }

            inputs.Add(input);
        }

        if (!replacedStructuredInputs)
        {
            inputs.InsertRange(0, replacementInputs);
        }

        return inputs;
    }

    public static IReadOnlyList<ControlPlaneInputItem> BuildStructuredInputsFromText(string? text)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<ControlPlaneInputItem>();
        }

        var decoded = DecodeLinkedInputs(normalized!);
        var inputs = new List<ControlPlaneInputItem>(decoded.LinkedInputs.Count + 1);
        if (!string.IsNullOrWhiteSpace(decoded.Text))
        {
            inputs.Add(new ControlPlaneTextInput(decoded.Text));
        }

        inputs.AddRange(decoded.LinkedInputs);
        return inputs;
    }

    public static string? BuildPreview(IReadOnlyList<ControlPlaneInputItem> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var parts = new List<string>();
        foreach (var input in inputs)
        {
            switch (input)
            {
                case ControlPlaneTextInput text when !string.IsNullOrWhiteSpace(text.Text):
                    parts.Add(text.Text);
                    break;
                case ControlPlaneImageInput image when !string.IsNullOrWhiteSpace(image.Url):
                    parts.Add(image.Url);
                    break;
                case ControlPlaneLocalImageInput localImage when !string.IsNullOrWhiteSpace(localImage.Path):
                    parts.Add(localImage.Path);
                    break;
                case ControlPlaneSkillInput skill when !string.IsNullOrWhiteSpace(skill.Name):
                    parts.Add($"skill:{skill.Name}");
                    break;
                case ControlPlaneMentionInput mention when !string.IsNullOrWhiteSpace(mention.Name):
                    parts.Add($"mention:{mention.Name}");
                    break;
            }
        }

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static DecodedLinkedInputs DecodeLinkedInputs(string text)
    {
        var visibleText = new StringBuilder(text.Length);
        var linkedInputs = new List<ControlPlaneInputItem>();
        var seenInputKeys = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '['
                && TryParseLinkedInput(text, index, out var displayName, out var path, out var endIndex)
                && CreateLinkedInput(displayName, path) is { } linkedInput)
            {
                visibleText.Append('$').Append(displayName);
                var dedupeKey = linkedInput switch
                {
                    ControlPlaneSkillInput skill => $"skill::{Normalize(skill.Path) ?? string.Empty}",
                    ControlPlaneMentionInput mention => $"mention::{Normalize(mention.Path) ?? string.Empty}",
                    _ => string.Empty,
                };
                if (dedupeKey.Length == 0 || seenInputKeys.Add(dedupeKey))
                {
                    linkedInputs.Add(linkedInput);
                }

                index = endIndex;
                continue;
            }

            visibleText.Append(text[index]);
            index++;
        }

        return new DecodedLinkedInputs(visibleText.ToString(), linkedInputs);
    }

    private static bool TryParseLinkedInput(string text, int startIndex, out string displayName, out string path, out int endIndex)
    {
        displayName = string.Empty;
        path = string.Empty;
        endIndex = startIndex;

        if (startIndex < 0
            || startIndex + 4 >= text.Length
            || text[startIndex] != '[')
        {
            return false;
        }

        var sigil = text[startIndex + 1];
        if (sigil is not ('$' or '@'))
        {
            return false;
        }

        var nameStart = startIndex + 2;
        if (!IsLinkedInputNameChar(text[nameStart]))
        {
            return false;
        }

        var nameEnd = nameStart + 1;
        while (nameEnd < text.Length && IsLinkedInputNameChar(text[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd >= text.Length || text[nameEnd] != ']')
        {
            return false;
        }

        var pathStart = nameEnd + 1;
        while (pathStart < text.Length && char.IsWhiteSpace(text[pathStart]))
        {
            pathStart++;
        }

        if (pathStart >= text.Length || text[pathStart] != '(')
        {
            return false;
        }

        var pathEnd = pathStart + 1;
        while (pathEnd < text.Length && text[pathEnd] != ')')
        {
            pathEnd++;
        }

        if (pathEnd >= text.Length)
        {
            return false;
        }

        var normalizedPath = Normalize(text[(pathStart + 1)..pathEnd]);
        if (string.IsNullOrWhiteSpace(normalizedPath)
            || !IsSupportedLinkedInputPath(normalizedPath!))
        {
            return false;
        }

        if (sigil == '@' && !normalizedPath.StartsWith("plugin://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        displayName = text[nameStart..nameEnd];
        path = normalizedPath!;
        endIndex = pathEnd + 1;
        return true;
    }

    private static ControlPlaneInputItem? CreateLinkedInput(string displayName, string path)
    {
        var normalizedName = Normalize(displayName);
        var normalizedPath = Normalize(path);
        if (string.IsNullOrWhiteSpace(normalizedName)
            || string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        if (IsSkillLinkedInputPath(normalizedPath!))
        {
            return new ControlPlaneSkillInput(normalizedName!, NormalizeSkillLinkedInputPath(normalizedPath!));
        }

        return new ControlPlaneMentionInput(normalizedName!, normalizedPath!);
    }

    private static bool IsSupportedLinkedInputPath(string path)
        => path.StartsWith("app://", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("mcp://", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("plugin://", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkillLinkedInputPath(string path)
        => path.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)
           || path.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSkillLinkedInputPath(string path)
        => path.StartsWith("skill://", StringComparison.OrdinalIgnoreCase)
            ? path["skill://".Length..]
            : path;

    private static bool IsLinkedInputNameChar(char ch)
        => char.IsLetterOrDigit(ch) || ch is '_' or '-';

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
