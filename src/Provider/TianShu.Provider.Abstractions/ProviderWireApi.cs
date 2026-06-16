namespace TianShu.Provider.Abstractions;

/// <summary>
/// 统一规范 provider wire API 的公开取值，避免执行层和宿主层各自维护分散校验。
/// Normalizes supported provider wire API values so execution and host layers share one validation rule.
/// </summary>
public static class ProviderWireApi
{
    public const string Responses = "responses";

    public const string OpenAiResponses = "openai_responses";

    public const string OpenAiChatCompletions = "openai_chat_completions";

    public const string AnthropicMessages = "anthropic_messages";

    public const string GoogleGenerative = "google_generative";

    public static string? NormalizeOrThrow(string? wireApi, string source)
    {
        var normalized = Normalize(wireApi);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (string.Equals(normalized, Responses, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, OpenAiResponses, StringComparison.OrdinalIgnoreCase))
        {
            return Responses;
        }

        if (string.Equals(normalized, "chat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "chat_completions", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, OpenAiChatCompletions, StringComparison.OrdinalIgnoreCase))
        {
            return OpenAiChatCompletions;
        }

        if (string.Equals(normalized, "messages", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "anthropic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, AnthropicMessages, StringComparison.OrdinalIgnoreCase))
        {
            return AnthropicMessages;
        }

        if (string.Equals(normalized, "gemini", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "google", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, GoogleGenerative, StringComparison.OrdinalIgnoreCase))
        {
            return GoogleGenerative;
        }

        throw new InvalidOperationException(
            $"`{source}` 仅支持 `responses`、`openai_responses`、`chat_completions`、`openai_chat_completions`、`anthropic_messages` 或 `google_generative`，当前值为 `{normalized}`。");
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
