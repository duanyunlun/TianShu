namespace TianShu.Cli.Terminal;

/// <summary>
/// Typed startup logo data consumed by terminal startup renderers.
/// 启动 Logo 的强类型数据模型，由终端启动渲染器消费。
/// </summary>
internal sealed record StartupLogoModel(
    string ProductName,
    string Version,
    string Directory,
    string Protocol,
    string Approval,
    string Sandbox,
    string Tip);
