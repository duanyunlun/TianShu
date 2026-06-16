using System.Text.Json;

namespace TianShu.Cli.Interaction.Presenters;

internal static class ErrorNoticePresenter
{
    public static SystemNoticeBlock BuildBlock(string? message)
        => new(NormalizeMessage(message));

    public static string NormalizeMessage(string? message)
    {
        var value = Normalize(message) ?? "收到错误事件。";
        const string failureMarker = "执行失败：";
        var markerIndex = value.IndexOf(failureMarker, StringComparison.Ordinal);
        if (markerIndex >= 0 && markerIndex <= 16)
        {
            var candidate = value[(markerIndex + failureMarker.Length)..].Trim();
            value = TryExtractProviderErrorMessage(candidate) ?? value;
        }

        value = TryExtractProviderErrorMessage(value) ?? value;
        value = string.Join(" ", value.Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        const int maxLength = 600;
        return value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
    }

    private static string? TryExtractProviderErrorMessage(string message)
    {
        var jsonStart = message.IndexOf('{');
        if (jsonStart < 0)
        {
            return null;
        }

        var prefix = message[..jsonStart].Trim().TrimEnd('，', ',', ' ');
        try
        {
            using var document = JsonDocument.Parse(message[jsonStart..]);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var errorMessage)
                && errorMessage.ValueKind == JsonValueKind.String)
            {
                var brief = errorMessage.GetString();
                if (!string.IsNullOrWhiteSpace(brief))
                {
                    return string.IsNullOrWhiteSpace(prefix) ? brief : $"{prefix}，{brief}";
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
