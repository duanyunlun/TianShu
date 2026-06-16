namespace TianShu.Cli.Terminal;

/// <summary>
/// Small ANSI styling helpers used only by the human terminal presentation.
/// 仅供 human 终端展示层使用的轻量 ANSI 样式工具。
/// </summary>
internal static class TerminalAnsi
{
    public const string Reset = "\u001b[0m";
    public const string Bold = "\u001b[1m";
    public const string Dim = "\u001b[2m";
    public const string Blue = "\u001b[36m";
    public const string SkyBlue = "\u001b[38;2;135;206;250m";
    public const string Green = "\u001b[32m";
    public const string Magenta = "\u001b[35m";
    public const string Red = "\u001b[31m";
    public const string Yellow = "\u001b[33m";

    public static string Style(string text, string style)
        => string.IsNullOrEmpty(text) ? text : style + text + Reset;

    public static string BoldText(string text)
        => Style(text, Bold);

    public static string DimText(string text)
        => Style(text, Dim);

    public static string BlueText(string text)
        => Style(text, Blue);

    public static string SkyBlueText(string text)
        => Style(text, SkyBlue);

    public static string GreenText(string text)
        => Style(text, Green);

    public static string MagentaText(string text)
        => Style(text, Magenta);

    public static string RedText(string text)
        => Style(text, Red);

    public static string YellowText(string text)
        => Style(text, Yellow);
}
