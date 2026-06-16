namespace TianShu.Cli.Terminal;

/// <summary>
/// Renders the typed startup logo model into terminal transcript lines.
/// 将强类型启动 Logo 模型渲染为终端 transcript 文本。
/// </summary>
internal static class StartupLogoRenderer
{
    private const int StartupBannerWidth = 72;

    public static string Build(StartupLogoModel model, bool styled)
    {
        var permission = FormatPermission(model.Approval, model.Sandbox);
        var productLinePlain = $"{model.ProductName}  v{model.Version}";
        var productLineStyled = styled
            ? $"{TerminalAnsi.Style(model.ProductName, TerminalAnsi.SkyBlue + TerminalAnsi.Bold)}  {TerminalAnsi.DimText($"v{model.Version}")}"
            : productLinePlain;
        var directoryLinePlain = $"工作区：   {model.Directory}";
        var directoryLineStyled = styled
            ? $"{TerminalAnsi.DimText("工作区：")}   {model.Directory}"
            : directoryLinePlain;
        var permissionLinePlain = $"权限：     {permission}";
        var permissionLineStyled = styled
            ? $"{TerminalAnsi.DimText("权限：")}     {FormatStyledPermission(permission)}"
            : permissionLinePlain;
        var tipLinePlain = $"提示：     {model.Tip}";
        var tipLineStyled = styled
            ? $"{TerminalAnsi.DimText("提示：")}     {FormatStyledTip(model.Tip)}"
            : tipLinePlain;

        return string.Join(
            Environment.NewLine,
            [
                BuildTopLine(styled),
                BuildContentLine(productLinePlain, productLineStyled, styled),
                BuildContentLine(directoryLinePlain, directoryLineStyled, styled),
                BuildContentLine(permissionLinePlain, permissionLineStyled, styled),
                BuildContentLine(tipLinePlain, tipLineStyled, styled),
                BuildBottomLine(styled),
                string.Empty,
            ]);
    }

    private static string BuildTopLine(bool styled)
    {
        var line = new string('━', StartupBannerWidth);
        return styled ? TerminalAnsi.DimText(line) : line;
    }

    private static string BuildContentLine(string text, string? styledText, bool styled)
    {
        var normalizedText = Truncate(text, StartupBannerWidth);
        var content = styled ? (styledText ?? normalizedText) : normalizedText;
        return content;
    }

    private static string BuildBottomLine(bool styled)
    {
        var line = new string('━', StartupBannerWidth);
        return styled ? TerminalAnsi.DimText(line) : line;
    }

    private static string FormatPermission(string approval, string sandbox)
    {
        var approvalText = FormatApproval(approval);
        var sandboxText = FormatSandbox(sandbox);
        if (string.Equals(approvalText, "配置默认", StringComparison.Ordinal)
            && string.Equals(sandboxText, "配置默认", StringComparison.Ordinal))
        {
            return "配置默认";
        }

        if (string.Equals(sandboxText, "配置默认", StringComparison.Ordinal))
        {
            return $"{approvalText} / 配置默认";
        }

        if (string.Equals(approvalText, "配置默认", StringComparison.Ordinal))
        {
            return sandboxText;
        }

        return $"{sandboxText}（{approvalText}）";
    }

    private static string FormatStyledPermission(string permission)
        => permission.Contains("完全访问", StringComparison.Ordinal)
            ? TerminalAnsi.YellowText(permission)
            : permission;

    private static string FormatStyledTip(string tip)
        => tip
            .Replace("/help", TerminalAnsi.BlueText("/help"), StringComparison.Ordinal)
            .Replace("/model-route", TerminalAnsi.BlueText("/model-route"), StringComparison.Ordinal)
            .Replace("@file", TerminalAnsi.DimText("@file"), StringComparison.Ordinal);

    private static string FormatApproval(string value)
    {
        var normalized = NormalizeMode(value);
        return normalized switch
        {
            "never" => "无需审批",
            "on-request" => "按需审批",
            "on-failure" => "失败时审批",
            "approve-all" => "自动批准",
            "untrusted" => "不受信任",
            "config default" => "配置默认",
            _ => string.IsNullOrWhiteSpace(value) ? "配置默认" : value,
        };
    }

    private static string FormatSandbox(string value)
    {
        var normalized = NormalizeMode(value);
        return normalized switch
        {
            "danger-full-access" => "完全访问",
            "workspace-write" => "工作区写入",
            "read-only" => "只读",
            "config default" => "配置默认",
            _ => string.IsNullOrWhiteSpace(value) ? "配置默认" : value,
        };
    }

    private static string NormalizeMode(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return maxLength <= 1
            ? value[..Math.Max(0, maxLength)]
            : value[..(maxLength - 1)] + "…";
    }
}
