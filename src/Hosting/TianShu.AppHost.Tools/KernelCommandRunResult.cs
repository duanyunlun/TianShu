namespace TianShu.AppHost.Tools;

/// <summary>
/// 命令执行结果的宿主侧共享载荷。
/// Shared host-side payload describing a command execution result.
/// </summary>
internal sealed record KernelCommandRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    bool TimedOut);
