using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// 控制平面启动命令执行的 typed 命令。
/// Typed control-plane command used to start command execution.
/// </summary>
public sealed record ControlPlaneCommandExecutionStartCommand
{
    /// <summary>
    /// 工作目录；为空时由运行时决定默认目录。
    /// Working directory; when omitted the runtime resolves its default directory.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// 以单条 shell 文本表达的命令内容。
    /// Command represented as a single shell text payload.
    /// </summary>
    public string? CommandText { get; init; }

    /// <summary>
    /// 以参数数组表达的命令内容。
    /// Command represented as an argument array.
    /// </summary>
    public IReadOnlyList<string> CommandArgs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 目标进程标识；用于复用既有终端会话。
    /// Target process identifier used to reuse an existing terminal session.
    /// </summary>
    public string? ProcessId { get; init; }

    /// <summary>
    /// 是否以 TTY 模式启动。
    /// Indicates whether the command should start with TTY enabled.
    /// </summary>
    public bool Tty { get; init; }

    /// <summary>
    /// 初始终端尺寸。
    /// Initial terminal size.
    /// </summary>
    public ControlPlaneCommandExecutionTerminalSize? Size { get; init; }

    /// <summary>
    /// 是否开启 stdin 流式写入。
    /// Indicates whether stdin streaming is enabled.
    /// </summary>
    public bool StreamStdin { get; init; }

    /// <summary>
    /// 是否开启 stdout/stderr 流式输出。
    /// Indicates whether stdout/stderr streaming is enabled.
    /// </summary>
    public bool StreamStdoutStderr { get; init; }

    /// <summary>
    /// 是否以后台任务方式启动。
    /// Indicates whether the command should run in background mode.
    /// </summary>
    public bool Background { get; init; }

    /// <summary>
    /// 是否禁用超时。
    /// Indicates whether timeout handling is disabled.
    /// </summary>
    public bool DisableTimeout { get; init; }

    /// <summary>
    /// 超时时长（毫秒）。
    /// Timeout duration in milliseconds.
    /// </summary>
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// 是否禁用输出大小上限。
    /// Indicates whether the output-size cap is disabled.
    /// </summary>
    public bool DisableOutputCap { get; init; }

    /// <summary>
    /// 输出字节上限。
    /// Output byte cap.
    /// </summary>
    public int? OutputBytesCap { get; init; }

    /// <summary>
    /// 所属线程标识。
    /// Owning thread identifier.
    /// </summary>
    public ThreadId? ThreadId { get; init; }

    /// <summary>
    /// 所属 turn 标识。
    /// Owning turn identifier.
    /// </summary>
    public TurnId? TurnId { get; init; }

    /// <summary>
    /// 对应的 northbound item 标识。
    /// Associated northbound item identifier.
    /// </summary>
    public string? ItemId { get; init; }

    /// <summary>
    /// 审批策略覆盖。
    /// Approval-policy override.
    /// </summary>
    public string? ApprovalPolicy { get; init; }

    /// <summary>
    /// 是否已被预先批准。
    /// Indicates whether the command was pre-approved.
    /// </summary>
    public bool Approved { get; init; }

    /// <summary>
    /// 是否以登录 shell 语义执行。
    /// Indicates whether login-shell semantics should be used.
    /// </summary>
    public bool? Login { get; init; }

    /// <summary>
    /// 环境变量覆盖。
    /// Environment-variable overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// 沙箱配置。
    /// Sandbox configuration payload.
    /// </summary>
    public StructuredValue? Sandbox { get; init; }
}

/// <summary>
/// 控制平面向命令执行会话写入 stdin 的 typed 命令。
/// Typed control-plane command used to write stdin into a command-execution session.
/// </summary>
public sealed record ControlPlaneCommandExecutionWriteCommand
{
    /// <summary>
    /// 目标进程标识。
    /// Target process identifier.
    /// </summary>
    public string ProcessId { get; init; } = string.Empty;

    /// <summary>
    /// 追加写入的 Base64 内容。
    /// Base64-encoded delta payload to append.
    /// </summary>
    public string? DeltaBase64 { get; init; }

    /// <summary>
    /// 是否在本次写入后关闭 stdin。
    /// Indicates whether stdin should be closed after this write.
    /// </summary>
    public bool CloseStdin { get; init; }
}

/// <summary>
/// 控制平面终止命令执行会话的 typed 命令。
/// Typed control-plane command used to terminate a command-execution session.
/// </summary>
public sealed record ControlPlaneCommandExecutionTerminateCommand
{
    /// <summary>
    /// 目标进程标识。
    /// Target process identifier.
    /// </summary>
    public string ProcessId { get; init; } = string.Empty;
}

/// <summary>
/// 控制平面调整命令执行终端尺寸的 typed 命令。
/// Typed control-plane command used to resize a command-execution terminal.
/// </summary>
public sealed record ControlPlaneCommandExecutionResizeCommand
{
    /// <summary>
    /// 目标进程标识。
    /// Target process identifier.
    /// </summary>
    public string ProcessId { get; init; } = string.Empty;

    /// <summary>
    /// 终端尺寸。
    /// Terminal size.
    /// </summary>
    public ControlPlaneCommandExecutionTerminalSize Size { get; init; } = new();
}

/// <summary>
/// 控制平面执行 code mode exec 的 typed 命令。
/// Typed control-plane command used to execute a code-mode cell.
/// </summary>
public sealed record ControlPlaneCodeModeExecCommand
{
    /// <summary>
    /// 所属线程标识。
    /// Owning thread identifier.
    /// </summary>
    public ThreadId ThreadId { get; init; }

    /// <summary>
    /// 待执行的输入内容。
    /// Input payload to execute.
    /// </summary>
    public string Input { get; init; } = string.Empty;

    /// <summary>
    /// 流式让出等待时长（毫秒）。
    /// Cooperative yield duration in milliseconds.
    /// </summary>
    public int? YieldTimeMs { get; init; }

    /// <summary>
    /// 输出 token 上限。
    /// Output token cap.
    /// </summary>
    public int? MaxOutputTokens { get; init; }
}

/// <summary>
/// 控制平面等待 code mode cell 的 typed 命令。
/// Typed control-plane command used to wait on a code-mode cell.
/// </summary>
public sealed record ControlPlaneCodeModeWaitCommand
{
    /// <summary>
    /// 所属线程标识。
    /// Owning thread identifier.
    /// </summary>
    public ThreadId ThreadId { get; init; }

    /// <summary>
    /// 目标 cell 标识。
    /// Target cell identifier.
    /// </summary>
    public string CellId { get; init; } = string.Empty;

    /// <summary>
    /// 流式让出等待时长（毫秒）。
    /// Cooperative yield duration in milliseconds.
    /// </summary>
    public int? YieldTimeMs { get; init; }

    /// <summary>
    /// 最大输出 token 数。
    /// Maximum number of output tokens.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// 是否在等待期间终止运行。
    /// Indicates whether execution should be terminated while waiting.
    /// </summary>
    public bool Terminate { get; init; }
}
