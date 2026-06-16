using TianShu.Provider.Abstractions;

namespace TianShu.Provider.OpenAICompatible;

/// <summary>
/// OpenAI-compatible Chat Completions 组件 bootstrap。
/// Component bootstrap for OpenAI-compatible Chat Completions.
/// </summary>
public sealed class OpenAiChatCompletionsProviderBootstrap : IProviderResponsesComponentBootstrap
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.OpenAiChatCompletions;

    /// <inheritdoc />
    public IProviderResponsesRequestComposer CreateRequestComposer()
        => new OpenAiChatCompletionsRequestComposer();

    /// <inheritdoc />
    public IProviderResponsesTransportProtocolBinding CreateTransportProtocolBinding()
        => new OpenAiChatCompletionsTransportProtocolBinding();

    /// <inheritdoc />
    public IProviderResponsesTransportRetryStrategy CreateTransportRetryStrategy()
        => new OpenAiChatCompletionsTransportRetryStrategy();

    /// <inheritdoc />
    public IProviderResponsesStreamChunkParser CreateStreamChunkParser()
        => NullProviderResponsesStreamChunkParser.Instance;

    /// <inheritdoc />
    public IProviderResponsesToolSurfaceBuilder CreateToolSurfaceBuilder()
        => new OpenAiChatCompletionsToolSurfaceBuilder();
}
