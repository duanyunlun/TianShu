using System.Text.Json.Serialization;
using TianShu.Contracts.Primitives;

namespace TianShu.Contracts.Execution;

/// <summary>
/// 控制平面命令执行终端尺寸模型。
/// Control-plane model describing a command-execution terminal size.
/// </summary>
public sealed record ControlPlaneCommandExecutionTerminalSize
{
    /// <summary>
    /// 行数。
    /// Terminal rows.
    /// </summary>
    public ushort Rows { get; init; }

    /// <summary>
    /// 列数。
    /// Terminal columns.
    /// </summary>
    public ushort Cols { get; init; }
}

/// <summary>
/// 控制平面命令执行结果模型。
/// Control-plane result model for command execution.
/// </summary>
public sealed record ControlPlaneCommandExecutionResult
{
    /// <summary>
    /// 是否成功启动。
    /// Indicates whether the command started successfully.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Started { get; init; }

    /// <summary>
    /// 运行时分配的进程标识。
    /// Runtime-assigned process identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProcessId { get; init; }

    /// <summary>
    /// 本地进程号。
    /// Local process ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pid { get; init; }

    /// <summary>
    /// 退出码。
    /// Exit code.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExitCode { get; init; }

    /// <summary>
    /// 标准输出快照。
    /// Stdout snapshot.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stdout { get; init; }

    /// <summary>
    /// 标准错误快照。
    /// Stderr snapshot.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stderr { get; init; }
}

/// <summary>
/// 控制平面命令执行受理结果。
/// Control-plane acknowledgement result for command-execution commands.
/// </summary>
public sealed record ControlPlaneCommandExecutionCommandAcceptedResult;

/// <summary>
/// 控制平面 code mode 输出项。
/// Control-plane code-mode output item.
/// </summary>
public sealed record ControlPlaneCodeModeOutputItem
{
    /// <summary>
    /// 输出项类型。
    /// Output item type.
    /// </summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// 文本内容。
    /// Text content.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>
    /// 图片地址。
    /// Image URL.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; init; }

    /// <summary>
    /// 输出细节。
    /// Output detail payload.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }
}

/// <summary>
/// 控制平面 code mode 操作结果。
/// Control-plane result for a code-mode operation.
/// </summary>
public sealed record ControlPlaneCodeModeResult
{
    /// <summary>
    /// 操作是否成功。
    /// Indicates whether the operation succeeded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Success { get; init; }

    /// <summary>
    /// 当前状态。
    /// Current status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// 所属线程标识。
    /// Owning thread identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadId? ThreadId { get; init; }

    /// <summary>
    /// 所属轮次标识。
    /// Owning turn identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TurnId? TurnId { get; init; }

    /// <summary>
    /// 对应 cell 标识。
    /// Associated cell identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CellId { get; init; }

    /// <summary>
    /// 汇总输出文本。
    /// Aggregated output text.
    /// </summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>
    /// 结构化输出项。
    /// Structured output items.
    /// </summary>
    public IReadOnlyList<ControlPlaneCodeModeOutputItem> ContentItems { get; init; } = Array.Empty<ControlPlaneCodeModeOutputItem>();
}
