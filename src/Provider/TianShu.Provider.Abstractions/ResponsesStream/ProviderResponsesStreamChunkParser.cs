using System.Text.Json;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider responses stream chunk parser SPI.
/// Provider responses 流式 chunk 解析扩展点。
/// </summary>
public interface IProviderResponsesStreamChunkParser
{
    bool TryReadChunk(JsonElement root, out ProviderResponsesStreamChunk chunk);
}

/// <summary>
/// Canonical projection of one provider-specific stream chunk.
/// 单个 provider 私有流式 chunk 的规范化投影。
/// </summary>
public sealed record ProviderResponsesStreamChunk(
    string? TextDelta = null,
    IReadOnlyList<JsonElement>? FunctionCalls = null,
    bool Completed = false)
{
    public IReadOnlyList<JsonElement> FunctionCalls { get; } = FunctionCalls ?? Array.Empty<JsonElement>();
}

/// <summary>
/// Provider stream parser exception with retry hint.
/// 携带重试提示的 provider 流式解析异常。
/// </summary>
public sealed class ProviderResponsesStreamParseException : Exception
{
    public ProviderResponsesStreamParseException(string message, bool isRetryable)
        : base(message)
    {
        IsRetryable = isRetryable;
    }

    public bool IsRetryable { get; }
}

/// <summary>
/// No-op stream chunk parser used by providers that are handled by generic runtime paths.
/// 空实现 parser，用于仍走通用 runtime 路径的 provider。
/// </summary>
public sealed class NullProviderResponsesStreamChunkParser : IProviderResponsesStreamChunkParser
{
    public static NullProviderResponsesStreamChunkParser Instance { get; } = new();

    private NullProviderResponsesStreamChunkParser()
    {
    }

    public bool TryReadChunk(JsonElement root, out ProviderResponsesStreamChunk chunk)
    {
        chunk = new ProviderResponsesStreamChunk();
        return false;
    }
}
