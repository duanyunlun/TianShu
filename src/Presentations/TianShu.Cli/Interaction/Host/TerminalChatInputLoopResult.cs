namespace TianShu.Cli.Interaction.Host;

/// <summary>
/// Describes whether the terminal input loop should end the surrounding chat session.
/// 描述终端输入循环是否要求结束外层 chat 会话。
/// </summary>
internal readonly record struct TerminalChatInputLoopResult(bool ShouldExit)
{
    public static TerminalChatInputLoopResult Continue { get; } = new(false);

    public static TerminalChatInputLoopResult ExitRequested { get; } = new(true);
}
