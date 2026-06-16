using System.Text.RegularExpressions;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// 默认诊断脱敏器，集中处理 secret-like key 与文本。
/// Default diagnostic redactor for secret-like keys and text.
/// </summary>
public sealed partial class DefaultDiagnosticRedactor : IDiagnosticRedactor
{
    private const string RedactedValue = "[REDACTED]";

    public bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("authorization", StringComparison.Ordinal)
               || normalized.Contains("apikey", StringComparison.Ordinal)
               || normalized.Contains("token", StringComparison.Ordinal)
               || normalized.Contains("secret", StringComparison.Ordinal)
               || normalized.Contains("cookie", StringComparison.Ordinal)
               || normalized.Contains("password", StringComparison.Ordinal)
               || normalized is "header" or "headers" or "rawheader" or "rawheaders" or "httpheader" or "httpheaders";
    }

    public string RedactText(string? key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!string.IsNullOrWhiteSpace(key) && IsSensitiveKey(key))
        {
            return RedactedValue;
        }

        var withoutBearer = BearerSecretRegex().Replace(value, "$1" + RedactedValue);
        var withoutAssignments = SecretAssignmentRegex().Replace(withoutBearer, match =>
        {
            var prefix = match.Groups["prefix"].Value;
            var quote = match.Groups["quote"].Value;
            var suffix = match.Groups["suffix"].Value;
            return string.IsNullOrEmpty(quote)
                ? prefix + RedactedValue
                : prefix + quote + RedactedValue + suffix;
        });

        var withoutWindowsPaths = WindowsAbsolutePathRegex().Replace(withoutAssignments, "[REDACTED_PATH]");
        return UnixAbsolutePathRegex().Replace(withoutWindowsPaths, "[REDACTED_PATH]");
    }

    public StructuredValue RedactStructuredValue(StructuredValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Kind switch
        {
            StructuredValueKind.Object => StructuredValue.FromObject(value.Properties.ToDictionary(
                static pair => pair.Key,
                pair => IsSensitiveKey(pair.Key)
                    ? StructuredValue.FromString(RedactedValue)
                    : RedactStructuredValue(pair.Value),
                StringComparer.Ordinal)),
            StructuredValueKind.Array => StructuredValue.FromArray(value.Items.Select(RedactStructuredValue).ToArray()),
            StructuredValueKind.String => StructuredValue.FromString(RedactText(null, value.StringValue ?? string.Empty)),
            _ => value,
        };
    }

    [GeneratedRegex("""(?i)(authorization\s*[:=]\s*Bearer\s+)[^\s,\r\n"}]+""", RegexOptions.CultureInvariant)]
    private static partial Regex BearerSecretRegex();

    [GeneratedRegex("""(?i)(?<prefix>["']?(authorization|api[_-]?key|apikey|token|secret|cookie|password|raw[_-]?headers?|http[_-]?headers?|headers?|set-cookie)["']?\s*[:=]\s*)(?<quote>["']?)[^,\r\n"'}]+(?<suffix>["']?)""", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"(?i)\b[a-z]:\\[^\s,;:""'{}\[\]]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePathRegex();

    [GeneratedRegex(@"(?i)(?<![a-z0-9])/(users|home|var|tmp|opt|etc)/[^\s,;:""'{}\[\]]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixAbsolutePathRegex();
}
