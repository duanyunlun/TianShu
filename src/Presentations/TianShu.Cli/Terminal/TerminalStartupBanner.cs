using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.RuntimeComposition;

namespace TianShu.Cli.Terminal;

/// <summary>
/// Builds the TianShu chat startup card rendered in a plain terminal transcript.
/// 生成天枢 chat 在普通终端 transcript 中展示的启动卡片。
/// </summary>
internal static class TerminalStartupBanner
{
    public static string Build(ChatCommandOptions options)
        => StartupLogoRenderer.Build(BuildModel(options), styled: false);

    public static string BuildStyled(ChatCommandOptions options)
        => StartupLogoRenderer.Build(BuildModel(options), styled: true);

    public static string BuildTip(bool styled)
        => StartupPlaceholderRenderer.BuildTip(styled);

    public static string BuildPlaceholder(ChatCommandOptions options, bool styled)
        => StartupPlaceholderRenderer.BuildPlaceholder(styled);

    public static string BuildFooter(ChatCommandOptions options, bool styled)
        => StartupPlaceholderRenderer.BuildFooter(BuildModel(options), styled);

    public static StartupLogoModel BuildLogoModel(ChatCommandOptions options)
        => BuildModel(options);

    private static StartupLogoModel BuildModel(ChatCommandOptions options)
    {
        var config = TryLoadDisplayConfig(options);
        var directory = FormatHomeRelativePath(options.WorkingDirectory);
        var runtimeContext = ResolveRuntimeContextDisplay(options, config);
        var version = typeof(InteractiveChatRunner).Assembly.GetName().Version?.ToString(fieldCount: 3) ?? "dev";

        return new StartupLogoModel(
            "天枢 TianShu",
            version,
            directory,
            ResolveProtocolDisplay(options, config),
            runtimeContext.Approval,
            runtimeContext.Sandbox,
            StartupPlaceholderRenderer.TipText);
    }

    private static ResolvedTianShuConfig? TryLoadDisplayConfig(ChatCommandOptions options)
    {
        try
        {
            return new RuntimeConfigurationComposition().Load(
                options.ConfigFilePath,
                options.ProfileName,
                options.ConfigOverrides,
                options.WorkingDirectory);
        }
        catch
        {
            return null;
        }
    }

    private static (string Approval, string Sandbox) ResolveRuntimeContextDisplay(ChatCommandOptions options, ResolvedTianShuConfig? config)
    {
        if (options.DangerouslyBypassApprovalsAndSandbox)
        {
            return ("never", "danger-full-access");
        }

        if (options.FullAuto)
        {
            return (
                NormalizeDisplayValue(options.RuntimeApprovalPolicy, "never"),
                NormalizeDisplayValue(options.RuntimeSandboxMode, "workspace-write"));
        }

        if (options.ApproveAll)
        {
            return (
                "approve-all",
                string.IsNullOrWhiteSpace(options.RuntimeSandboxMode)
                    ? NormalizeDisplayValue(config?.SandboxMode, "config default")
                    : options.RuntimeSandboxMode);
        }

        var approval = string.IsNullOrWhiteSpace(options.RuntimeApprovalPolicy)
            ? NormalizeDisplayValue(config?.ApprovalPolicy, "config default")
            : options.RuntimeApprovalPolicy;
        var sandbox = string.IsNullOrWhiteSpace(options.RuntimeSandboxMode)
            ? NormalizeDisplayValue(config?.SandboxMode, "config default")
            : options.RuntimeSandboxMode;
        return (approval, sandbox);
    }

    private static string ResolveProtocolDisplay(ChatCommandOptions options, ResolvedTianShuConfig? config)
        => string.IsNullOrWhiteSpace(options.RuntimeProviderWireApi)
            ? NormalizeDisplayValue(config?.ProviderWireApi, "config default")
            : options.RuntimeProviderWireApi;

    private static string NormalizeDisplayValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string FormatHomeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "~";
        }

        var fullPath = Path.GetFullPath(path);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            var fullHome = Path.GetFullPath(home);
            if (string.Equals(fullPath, fullHome, StringComparison.OrdinalIgnoreCase))
            {
                return "~";
            }

            var homePrefix = fullHome.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return "~" + Path.DirectorySeparatorChar + fullPath[homePrefix.Length..];
            }
        }

        return fullPath;
    }
}
