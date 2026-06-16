namespace TianShu.Cli.Terminal;

/// <summary>
/// Describes modifier keys observed by the TianShu terminal composer.
/// 描述 TianShu 终端输入状态机可感知的组合键。
/// </summary>
[Flags]
internal enum TerminalKeyModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Control = 4,
}

/// <summary>
/// Describes normalized key kinds consumed by the TianShu terminal composer.
/// 描述 TianShu 终端输入状态机消费的归一化按键类型。
/// </summary>
internal enum TerminalKeyKind
{
    Character,
    Enter,
    Backspace,
    Delete,
    LeftArrow,
    RightArrow,
    UpArrow,
    DownArrow,
    Home,
    End,
    Tab,
    Escape,
    ControlC,
}

/// <summary>
/// Represents one normalized terminal input key without binding to a concrete console framework.
/// 表示一个不绑定具体控制台框架的归一化终端按键。
/// </summary>
internal readonly record struct TerminalInputKey(
    TerminalKeyKind Kind,
    char? Character = null,
    TerminalKeyModifiers Modifiers = TerminalKeyModifiers.None)
{
    /// <summary>
    /// Creates a printable character key.
    /// 创建一个可打印字符按键。
    /// </summary>
    public static TerminalInputKey FromCharacter(char value)
        => new(TerminalKeyKind.Character, value);
}
