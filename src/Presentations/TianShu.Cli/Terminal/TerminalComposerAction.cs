namespace TianShu.Cli.Terminal;

/// <summary>
/// Describes actions emitted by the terminal composer after handling a key.
/// 描述终端输入状态机处理按键后产生的动作。
/// </summary>
internal enum TerminalComposerActionKind
{
    None,
    Submit,
    Exit,
}

/// <summary>
/// Distinguishes how submitted terminal text should be interpreted while a turn is running.
/// 区分终端提交文本在回合运行中应被解释为普通发送、排队还是引导。
/// </summary>
internal enum TerminalSubmitIntent
{
    Standard,
    Queue,
    Steer,
}

/// <summary>
/// Represents a normalized composer action that the chat runner can translate into existing commands.
/// 表示 chat runner 可转换为既有命令的归一化输入动作。
/// </summary>
internal readonly record struct TerminalComposerAction(
    TerminalComposerActionKind Kind,
    string? Text,
    TerminalSubmitIntent SubmitIntent = TerminalSubmitIntent.Standard)
{
    public static TerminalComposerAction None { get; } = new(TerminalComposerActionKind.None, null);

    public static TerminalComposerAction Submit(
        string text,
        TerminalSubmitIntent intent = TerminalSubmitIntent.Standard)
        => new(TerminalComposerActionKind.Submit, text, intent);
}
