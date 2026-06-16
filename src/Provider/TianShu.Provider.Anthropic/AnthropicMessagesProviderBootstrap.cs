using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Anthropic;

/// <summary>
/// Anthropic Messages provider 组件引导器。
/// Component bootstrap for the Anthropic Messages provider adapter.
/// </summary>
public sealed class AnthropicMessagesProviderBootstrap : IProviderResponsesComponentBootstrap
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.AnthropicMessages;

    /// <inheritdoc />
    public IProviderResponsesRequestComposer CreateRequestComposer()
        => new AnthropicMessagesRequestComposer();

    /// <inheritdoc />
    public IProviderResponsesTransportProtocolBinding CreateTransportProtocolBinding()
        => new AnthropicMessagesTransportProtocolBinding();

    /// <inheritdoc />
    public IProviderResponsesTransportRetryStrategy CreateTransportRetryStrategy()
        => new AnthropicMessagesTransportRetryStrategy();

    /// <inheritdoc />
    public IProviderResponsesStreamChunkParser CreateStreamChunkParser()
        => NullProviderResponsesStreamChunkParser.Instance;

    /// <inheritdoc />
    public IProviderResponsesToolSurfaceBuilder CreateToolSurfaceBuilder()
        => new AnthropicMessagesToolSurfaceBuilder();
}
