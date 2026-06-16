using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// 执行层当前激活 provider 的运行态组件快照。
/// Runtime component snapshot for the currently activated execution provider.
/// </summary>
public sealed class ProviderRuntimeState
{
    /// <summary>
    /// 使用指定 bootstrap 构造运行态组件快照。
    /// Builds a runtime component snapshot from the specified bootstrap.
    /// </summary>
    /// <param name="bootstrap">provider bootstrap。Provider bootstrap.</param>
    public ProviderRuntimeState(IProviderRuntimeBootstrap bootstrap)
    {
        Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        NotificationInterpreter = bootstrap.CreateNotificationInterpreter();
        ToolEventFactory = bootstrap.CreateToolEventFactory();
        ServerRequestRouter = bootstrap.CreateServerRequestRouter();
        ServerRequestInterpreter = bootstrap.CreateServerRequestInterpreter();
        ServerRequestResponseSerializer = bootstrap.CreateServerRequestResponseSerializer();
        ProtocolAdapter = bootstrap.CreateProtocolAdapter();
    }

    /// <summary>
    /// 当前激活的 provider bootstrap。
    /// Currently activated provider bootstrap.
    /// </summary>
    public IProviderRuntimeBootstrap Bootstrap { get; }

    /// <summary>
    /// provider 原始通知解释器。
    /// Provider raw-notification interpreter.
    /// </summary>
    public IProviderNotificationInterpreter NotificationInterpreter { get; }

    /// <summary>
    /// provider 工具生命周期事件工厂。
    /// Provider tool-lifecycle event factory.
    /// </summary>
    public IProviderToolEventFactory ToolEventFactory { get; }

    /// <summary>
    /// provider 服务端请求路由器。
    /// Provider server-request router.
    /// </summary>
    public IProviderServerRequestRouter ServerRequestRouter { get; }

    /// <summary>
    /// provider 服务端请求解释器。
    /// Provider server-request interpreter.
    /// </summary>
    public IProviderServerRequestInterpreter ServerRequestInterpreter { get; }

    /// <summary>
    /// provider 服务端请求响应序列化器。
    /// Provider server-request response serializer.
    /// </summary>
    public IProviderServerRequestResponseSerializer ServerRequestResponseSerializer { get; }

    /// <summary>
    /// 当前 provider 对应的协议适配器。
    /// Protocol adapter bound to the current provider.
    /// </summary>
    public IProtocolAdapter ProtocolAdapter { get; }
}
