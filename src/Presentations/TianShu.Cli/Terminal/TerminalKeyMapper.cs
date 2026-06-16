namespace TianShu.Cli.Terminal;

/// <summary>
/// Maps framework-specific console keys into TianShu terminal composer keys.
/// 将具体控制台按键映射为 TianShu 终端输入状态机的归一化按键。
/// </summary>
internal static class TerminalKeyMapper
{
    public static TerminalInputKey? FromConsoleKeyInfo(ConsoleKeyInfo keyInfo, bool treatPlainEnterAsNewLine = false)
    {
        var modifiers = ToModifiers(keyInfo.Modifiers);
        if ((modifiers & TerminalKeyModifiers.Control) == TerminalKeyModifiers.Control)
        {
            if (keyInfo.Key == ConsoleKey.C)
            {
                return new TerminalInputKey(TerminalKeyKind.ControlC, Modifiers: modifiers);
            }

            if (keyInfo.Key == ConsoleKey.J)
            {
                return new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: modifiers);
            }
        }

        return keyInfo.Key switch
        {
            ConsoleKey.Enter when treatPlainEnterAsNewLine && modifiers == TerminalKeyModifiers.None =>
                new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: TerminalKeyModifiers.Shift),
            ConsoleKey.Enter => new TerminalInputKey(TerminalKeyKind.Enter, Modifiers: modifiers),
            ConsoleKey.Backspace => new TerminalInputKey(TerminalKeyKind.Backspace, Modifiers: modifiers),
            ConsoleKey.Delete => new TerminalInputKey(TerminalKeyKind.Delete, Modifiers: modifiers),
            ConsoleKey.LeftArrow => new TerminalInputKey(TerminalKeyKind.LeftArrow, Modifiers: modifiers),
            ConsoleKey.RightArrow => new TerminalInputKey(TerminalKeyKind.RightArrow, Modifiers: modifiers),
            ConsoleKey.UpArrow => new TerminalInputKey(TerminalKeyKind.UpArrow, Modifiers: modifiers),
            ConsoleKey.DownArrow => new TerminalInputKey(TerminalKeyKind.DownArrow, Modifiers: modifiers),
            ConsoleKey.Home => new TerminalInputKey(TerminalKeyKind.Home, Modifiers: modifiers),
            ConsoleKey.End => new TerminalInputKey(TerminalKeyKind.End, Modifiers: modifiers),
            ConsoleKey.Tab => new TerminalInputKey(TerminalKeyKind.Tab, Modifiers: modifiers),
            ConsoleKey.Escape => new TerminalInputKey(TerminalKeyKind.Escape, Modifiers: modifiers),
            _ when !char.IsControl(keyInfo.KeyChar) => TerminalInputKey.FromCharacter(keyInfo.KeyChar),
            _ => null,
        };
    }

    private static TerminalKeyModifiers ToModifiers(ConsoleModifiers modifiers)
    {
        var result = TerminalKeyModifiers.None;
        if ((modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift)
        {
            result |= TerminalKeyModifiers.Shift;
        }

        if ((modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt)
        {
            result |= TerminalKeyModifiers.Alt;
        }

        if ((modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control)
        {
            result |= TerminalKeyModifiers.Control;
        }

        return result;
    }
}
