namespace TianShu.Contracts.Environment;

/// <summary>
/// Windows Sandbox 启动模式。
/// Windows Sandbox startup mode.
/// </summary>
public enum WindowsSandboxSetupMode
{
    Elevated = 0,
    Unelevated = 1,
}

/// <summary>
/// 控制平面发起 Windows Sandbox 环境准备的 typed 命令。
/// Typed control-plane command used to start Windows Sandbox environment setup.
/// </summary>
public sealed record ControlPlaneWindowsSandboxSetupStartCommand
{
    /// <summary>
    /// 目标启动模式。
    /// Target startup mode.
    /// </summary>
    public WindowsSandboxSetupMode Mode { get; init; }

    /// <summary>
    /// 可选工作目录。
    /// Optional working directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// 控制平面 Windows Sandbox 环境准备启动结果。
/// Control-plane start result for Windows Sandbox environment setup.
/// </summary>
public sealed record ControlPlaneWindowsSandboxSetupStartResult
{
    /// <summary>
    /// 是否已成功提交启动请求。
    /// Indicates whether the setup request was accepted.
    /// </summary>
    public bool Started { get; init; }
}
