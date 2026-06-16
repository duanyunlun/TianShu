namespace TianShu.Cli.Terminal;

/// <summary>
/// Renders startup prompt placeholder, footer and tip lines.
/// 渲染启动后的输入 placeholder、footer 与提示行。
/// </summary>
internal static class StartupPlaceholderRenderer
{
    public const string TipText = "输入 /help 查看命令，/model-route 切换模型路由方案，@file 附加上下文。";

    public static string BuildTip(bool styled)
    {
        var plain = $"提示：{TipText}";
        return styled
            ? $"{TerminalAnsi.BoldText("提示：")}输入 {TerminalAnsi.BlueText("/help")} 查看命令，{TerminalAnsi.BlueText("/model-route")} 切换模型路由方案，{TerminalAnsi.DimText("@file")} 附加上下文。"
            : plain;
    }

    public static string BuildPlaceholder(bool styled)
    {
        var text = "问天枢，或输入 /help 与 @filename";
        return styled ? TerminalAnsi.DimText(text) : text;
    }

    public static string BuildFooter(StartupLogoModel model, bool styled)
    {
        var text = $"工作区：{model.Directory}";
        return styled ? TerminalAnsi.DimText(text) : text;
    }
}
