using TianShu.Cli.Terminal;

namespace TianShu.Cli.Tests;

public sealed class TerminalKeyMapperTests
{
    [Fact]
    public void FromConsoleKeyInfo_WhenPrintableCharacter_ReturnsCharacterKey()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('x', ConsoleKey.X, shift: false, alt: false, control: false));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Character, key.Value.Kind);
        Assert.Equal('x', key.Value.Character);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenShiftEnter_ReturnsEnterWithShiftModifier()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: true, alt: false, control: false));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Enter, key.Value.Kind);
        Assert.True((key.Value.Modifiers & TerminalKeyModifiers.Shift) == TerminalKeyModifiers.Shift);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenPlainEnterShouldBeTreatedAsNewLine_ReturnsForcedNewLineSignal()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false),
            treatPlainEnterAsNewLine: true);

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Enter, key.Value.Kind);
        Assert.True((key.Value.Modifiers & TerminalKeyModifiers.Shift) == TerminalKeyModifiers.Shift);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenPlainEnterShouldNotBeTreatedAsNewLine_SubmitsNormally()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false),
            treatPlainEnterAsNewLine: false);

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Enter, key.Value.Kind);
        Assert.Equal(TerminalKeyModifiers.None, key.Value.Modifiers);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenCtrlEnter_ReturnsEnterWithControlModifier()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Enter, key.Value.Kind);
        Assert.True((key.Value.Modifiers & TerminalKeyModifiers.Control) == TerminalKeyModifiers.Control);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenControlCharacterWithoutKnownKey_ReturnsNull()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\u0001', ConsoleKey.A, shift: false, alt: false, control: true));

        Assert.Null(key);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenControlC_ReturnsInterruptKey()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\u0003', ConsoleKey.C, shift: false, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.ControlC, key.Value.Kind);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenControlJ_ReturnsControlEnter()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\n', ConsoleKey.J, shift: false, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Enter, key.Value.Kind);
        Assert.True((key.Value.Modifiers & TerminalKeyModifiers.Control) == TerminalKeyModifiers.Control);
    }

    [Fact]
    public void FromConsoleKeyInfo_WhenTab_ReturnsTab()
    {
        var key = TerminalKeyMapper.FromConsoleKeyInfo(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false));

        Assert.NotNull(key);
        Assert.Equal(TerminalKeyKind.Tab, key.Value.Kind);
    }
}
