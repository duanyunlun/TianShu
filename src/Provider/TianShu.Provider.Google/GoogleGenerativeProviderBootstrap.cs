using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Google;

/// <summary>
/// Google Generative provider 组件引导器。
/// Component bootstrap for the Google Generative provider adapter.
/// </summary>
public sealed class GoogleGenerativeProviderBootstrap : IProviderResponsesComponentBootstrap
{
    /// <inheritdoc />
    public string WireApi => ProviderWireApi.GoogleGenerative;

    /// <inheritdoc />
    public IProviderResponsesRequestComposer CreateRequestComposer()
        => new GoogleGenerativeRequestComposer();

    /// <inheritdoc />
    public IProviderResponsesTransportProtocolBinding CreateTransportProtocolBinding()
        => new GoogleGenerativeTransportProtocolBinding();

    /// <inheritdoc />
    public IProviderResponsesTransportRetryStrategy CreateTransportRetryStrategy()
        => new GoogleGenerativeTransportRetryStrategy();

    /// <inheritdoc />
    public IProviderResponsesStreamChunkParser CreateStreamChunkParser()
        => new GoogleGenerativeStreamChunkParser();

    /// <inheritdoc />
    public IProviderResponsesToolSurfaceBuilder CreateToolSurfaceBuilder()
        => new GoogleGenerativeToolSurfaceBuilder();
}
