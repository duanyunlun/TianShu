namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 侧 responses 组件装配入口。
/// Provider-side bootstrap entry for responses components.
/// </summary>
public interface IProviderResponsesComponentBootstrap
{
    /// <summary>
    /// 该 bootstrap 对应的 wire API 标识。
    /// Wire API identifier owned by this bootstrap.
    /// </summary>
    string WireApi { get; }

    /// <summary>
    /// 创建 provider-specific 的 responses request composer。
    /// Creates the provider-specific responses request composer.
    /// </summary>
    IProviderResponsesRequestComposer CreateRequestComposer();

    /// <summary>
    /// 创建 provider-specific 的 responses transport protocol binding。
    /// Creates the provider-specific responses transport protocol binding.
    /// </summary>
    IProviderResponsesTransportProtocolBinding CreateTransportProtocolBinding();

    /// <summary>
    /// 创建 provider-specific 的 responses transport retry strategy。
    /// Creates the provider-specific responses transport retry strategy.
    /// </summary>
    IProviderResponsesTransportRetryStrategy CreateTransportRetryStrategy();

    /// <summary>
    /// 创建 provider-specific 的 responses stream chunk parser。
    /// Creates the provider-specific responses stream chunk parser.
    /// </summary>
    IProviderResponsesStreamChunkParser CreateStreamChunkParser();

    /// <summary>
    /// 创建 provider-specific 的 responses tool surface builder。
    /// Creates the provider-specific responses tool surface builder.
    /// </summary>
    IProviderResponsesToolSurfaceBuilder CreateToolSurfaceBuilder();
}
