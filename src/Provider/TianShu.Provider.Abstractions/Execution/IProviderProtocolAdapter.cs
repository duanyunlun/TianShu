using System.Text.Json;
using TianShu.Contracts.Provider;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider 协议族适配器。
/// Provider protocol-family adapter.
/// </summary>
public interface IProviderProtocolAdapter
{
    /// <summary>
    /// 当前适配器支持的协议族。
    /// Protocol family supported by this adapter.
    /// </summary>
    ProviderProtocolKind ProtocolKind { get; }

    /// <summary>
    /// 稳定适配器标识。
    /// Stable adapter identifier.
    /// </summary>
    string Id { get; }
}

/// <summary>
/// Provider 模型目录读取客户端。
/// Provider model catalog client.
/// </summary>
public interface IProviderModelCatalogClient
{
    Task<IReadOnlyList<ProviderModelDescriptor>> ListModelsAsync(
        ProviderEndpointDescriptor endpoint,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provider 请求组合器。
/// Provider request composer.
/// </summary>
public interface IProviderRequestComposer
{
    JsonElement Compose(ProviderConversationRequest request);
}

/// <summary>
/// Provider 流式响应解析器。
/// Provider stream parser.
/// </summary>
public interface IProviderStreamParser
{
    IAsyncEnumerable<ProviderStreamEvent> ParseAsync(
        IAsyncEnumerable<string> streamEvents,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provider 非流式响应解析器。
/// Provider non-streaming response parser.
/// </summary>
public interface IProviderResponseParser
{
    ProviderCompletion Parse(JsonElement response);
}

/// <summary>
/// Provider 工具表面映射器。
/// Provider tool surface mapper.
/// </summary>
public interface IProviderToolSurfaceMapper
{
    IReadOnlyList<JsonElement> MapTools(IReadOnlyList<ProviderToolDescriptor> tools);
}

/// <summary>
/// Provider 连通性探测器。
/// Provider connectivity probe.
/// </summary>
public interface IProviderConnectivityProbe
{
    Task<ProviderProbeResult> ProbeAsync(
        ProviderEndpointDescriptor endpoint,
        string model,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provider 错误归一化器。
/// Provider error normalizer.
/// </summary>
public interface IProviderErrorNormalizer
{
    ProviderFailure Normalize(Exception exception, ProviderEndpointDescriptor endpoint, string? model = null);
}
