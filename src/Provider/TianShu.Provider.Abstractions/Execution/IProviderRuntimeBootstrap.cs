namespace TianShu.Provider.Abstractions;

/// <summary>
/// 执行层 southbound provider bootstrap 抽象。
/// Southbound provider bootstrap abstraction for the execution plane.
/// </summary>
public interface IProviderRuntimeBootstrap
{
    /// <summary>
    /// 该 bootstrap 负责的协议适配器标识。
    /// Protocol adapter identifier owned by this bootstrap.
    /// </summary>
    string ProtocolAdapterId { get; }

    /// <summary>
    /// 创建 southbound 协议适配器实例。
    /// Creates the southbound protocol adapter instance.
    /// </summary>
    IProtocolAdapter CreateProtocolAdapter();

    /// <summary>
    /// 创建 provider 原始通知解释器。
    /// Creates the provider raw-notification interpreter.
    /// </summary>
    IProviderNotificationInterpreter CreateNotificationInterpreter();

    /// <summary>
    /// 创建 provider 工具生命周期事件工厂。
    /// Creates the provider tool-lifecycle event factory.
    /// </summary>
    IProviderToolEventFactory CreateToolEventFactory();

    /// <summary>
    /// 创建 provider 服务端请求路由器。
    /// Creates the provider server-request router.
    /// </summary>
    IProviderServerRequestRouter CreateServerRequestRouter();

    /// <summary>
    /// 创建 provider 服务端请求载荷解释器。
    /// Creates the provider server-request payload interpreter.
    /// </summary>
    IProviderServerRequestInterpreter CreateServerRequestInterpreter();

    /// <summary>
    /// 创建 provider 服务端请求响应序列化器。
    /// Creates the provider server-request response serializer.
    /// </summary>
    IProviderServerRequestResponseSerializer CreateServerRequestResponseSerializer();

    /// <summary>
    /// 创建 provider 模型能力目录。
    /// Creates the provider model capability catalog.
    /// </summary>
    IProviderModelCatalog CreateModelCatalog();

    /// <summary>
    /// 生成 provider 专属的 southbound CLI 参数片段。
    /// Builds provider-specific southbound CLI argument segments.
    /// </summary>
    IReadOnlyList<string> BuildCliArguments(ProviderRuntimeCliArguments arguments);
}
