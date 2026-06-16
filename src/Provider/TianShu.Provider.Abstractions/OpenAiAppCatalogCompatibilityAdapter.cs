using System.Net.Http.Headers;
using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// OpenAI / ChatGPT 应用目录互操作兼容适配层。
/// Compatibility adapter for OpenAI / ChatGPT app-catalog interop.
/// </summary>
public static class OpenAiAppCatalogCompatibilityAdapter
{
    public const string DefaultBaseUrl = OpenAiAppCatalogCompatibilityKeys.DefaultBaseUrl;
    public const string CodexAppsMcpServerName = OpenAiAppCatalogCompatibilityKeys.CodexAppsMcpServerName;
    public const string CodexAppsMcpPath = OpenAiAppCatalogCompatibilityKeys.CodexAppsMcpPath;
    public const string ChatGptBaseUrlConfigKey = OpenAiAppCatalogCompatibilityKeys.ChatGptBaseUrlConfigKey;
    public const string ChatGptAccountIdHeaderName = OpenAiAppCatalogCompatibilityKeys.ChatGptAccountIdHeaderName;

    private const string ChatGptAuthFileName = "auth.json";
    private const string ChatGptAuthClaimName = "https://api.openai.com/auth";
    private const string ChatGptAccountIdClaimName = "chatgpt_account_id";
    private const string DisallowedConnectorPrefix = "connector_openai_";

    private static readonly string[] DisallowedConnectorIds =
    [
        "asdk_app_6938a94a61d881918ef32cb999ff937c",
        "connector_2b0a9009c9c64bf9933a3dae3f2b1254",
        "connector_68de829bf7648191acd70a907364c67c",
        "connector_68e004f14af881919eb50893d3d9f523",
        "connector_69272cb413a081919685ec3c88d1744e",
    ];

    private static readonly string[] ToolSuggestDiscoverableConnectorIds =
    [
        "connector_2128aebfecb84f64a069897515042a44",
        "connector_68df038e0ba48191908c8434991bbac2",
        "asdk_app_69a1d78e929881919bba0dbda1f6436d",
        "connector_4964e3b22e3e427e9b4ae1acf2c1fa34",
        "connector_9d7cfa34e6654a5f98d3387af34b2e1c",
        "connector_6f1ec045b8fa4ced8738e32c7f74514b",
        "connector_947e0d954944416db111db556030eea6",
        "connector_5f3c8c41a1e54ad7a76272c89e2554fa",
        "connector_686fad9b54914a35b75be6d06a0f6f31",
        "connector_76869538009648d5b282a4bb21c3d157",
        "connector_37316be7febe4224b3d31465bae4dbd7",
    ];

    public static string? TryReadConfiguredBaseUrl(string? configText)
    {
        if (string.IsNullOrWhiteSpace(configText))
        {
            return null;
        }

        using var reader = new StringReader(configText);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var delimiterIndex = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (delimiterIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..delimiterIndex].Trim();
            if (!string.Equals(key, ChatGptBaseUrlConfigKey, StringComparison.Ordinal))
            {
                continue;
            }

            return NormalizeTomlScalar(trimmed[(delimiterIndex + 1)..]);
        }

        return null;
    }

    public static async Task<(string AccessToken, string AccountId)?> TryReadAuthContextAsync(
        string tianShuHomePath,
        CancellationToken cancellationToken)
    {
        var authPath = Path.Combine(tianShuHomePath, ChatGptAuthFileName);
        if (!File.Exists(authPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(authPath, cancellationToken).ConfigureAwait(false));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tokens", out var tokens)
                || tokens.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var accessToken = ReadString(tokens, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var accountId = ReadString(tokens, "account_id")
                ?? ReadString(tokens, ChatGptAccountIdClaimName)
                ?? ExtractAccountIdFromIdToken(ReadString(tokens, "id_token"));
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return null;
            }

            return (accessToken!, accountId!);
        }
        catch
        {
            return null;
        }
    }

    public static Uri BuildCatalogUri(string? baseUrl, string relativePath)
    {
        var normalizedBaseUrl = Normalize(baseUrl) ?? DefaultBaseUrl;
        if (!normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBaseUrl += "/";
        }

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), relativePath);
    }

    public static void ApplyAuthHeaders(HttpRequestHeaders headers, string accessToken, string accountId)
    {
        headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        headers.TryAddWithoutValidation(ChatGptAccountIdHeaderName, accountId);
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static bool IsDisallowedConnector(string? connectorId)
        => !string.IsNullOrWhiteSpace(connectorId)
           && (DisallowedConnectorIds.Contains(connectorId, StringComparer.OrdinalIgnoreCase)
               || connectorId.StartsWith(DisallowedConnectorPrefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsToolSuggestDiscoverableConnector(string? connectorId)
        => !string.IsNullOrWhiteSpace(connectorId)
           && ToolSuggestDiscoverableConnectorIds.Contains(connectorId, StringComparer.OrdinalIgnoreCase);

    public static string? BuildConnectorApprovalSessionKey(string? connectorId, string? shortName)
    {
        var normalizedConnectorId = Normalize(connectorId);
        var normalizedShortName = Normalize(shortName);
        if (string.IsNullOrWhiteSpace(normalizedConnectorId) || string.IsNullOrWhiteSpace(normalizedShortName))
        {
            return null;
        }

        return $"{CodexAppsMcpServerName}::{normalizedConnectorId}::{normalizedShortName}";
    }

    private static string? NormalizeTomlScalar(string rawValue)
    {
        var text = rawValue.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var commentIndex = text.IndexOf('#');
        if (commentIndex >= 0 && !IsQuoted(text))
        {
            text = text[..commentIndex].TrimEnd();
        }

        if (IsQuoted(text))
        {
            text = text[1..^1];
        }

        return Normalize(text);
    }

    private static bool IsQuoted(string value)
        => value.Length >= 2
           && ((value[0] == '"' && value[^1] == '"')
               || (value[0] == '\'' && value[^1] == '\''));

    private static string? ExtractAccountIdFromIdToken(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payloadBytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty(ChatGptAuthClaimName, out var auth)
                || auth.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadString(auth, ChatGptAccountIdClaimName);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var remainder = normalized.Length % 4;
        if (remainder != 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - remainder), '=');
        }

        return Convert.FromBase64String(normalized);
    }

    private static string? ReadString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? Normalize(property.GetString())
            : null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
