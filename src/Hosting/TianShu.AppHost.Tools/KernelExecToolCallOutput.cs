namespace TianShu.AppHost.Tools;

/// <summary>
/// 表示本地 shell / 进程执行的标准化结果，用于宿主工具执行链与网络代理补写链共享。
/// Represents the normalized result of a local shell/process execution shared by host-tool execution and managed-network post-processing.
/// </summary>
internal sealed record KernelExecToolCallOutput(
    int ExitCode,
    string Stdout,
    string Stderr,
    string AggregatedOutput,
    TimeSpan Duration,
    bool TimedOut);

/// <summary>
/// 表示执行本地 shell 命令的委托签名，便于测试替身与不同宿主执行器注入。
/// Defines the delegate signature for local shell execution so tests and alternate host runners can be injected.
/// </summary>
internal delegate Task<KernelExecToolCallOutput> KernelExecRunnerDelegate(
    IReadOnlyList<string> command,
    string cwd,
    int timeoutMs,
    IReadOnlyDictionary<string, string>? environment,
    CancellationToken cancellationToken);
