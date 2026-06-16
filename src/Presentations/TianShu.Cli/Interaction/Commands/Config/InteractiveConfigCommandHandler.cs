using System.ComponentModel;
using System.Diagnostics;
using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.ControlPlane;
using TianShu.Contracts.Conversations;
using TianShu.Contracts.Catalog;
using TianShu.Contracts.Primitives;
using TianShu.Execution.Runtime;
using ConfigHomePathUtilities = TianShu.Configuration.TianShuHomePathUtilities;

namespace TianShu.Cli.Interaction.Commands.Config;

/// <summary>
/// Handles interactive configuration commands such as /config reload and /reload.
/// 处理 /config reload 与 /reload 等交互式配置命令。
/// </summary>
internal sealed class InteractiveConfigCommandHandler
{
    private const string ConfigGuiExecutableName = "TianShu.ConfigGui.exe";

    public async Task HandleConfigCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string rest,
        InteractiveConfigCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subcommand = ReadFirstToken(rest, out var remainder).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(subcommand) || string.Equals(subcommand, "gui", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(remainder))
            {
                context.WriteLine("用法：/config 或 /config reload", true);
                return;
            }

            LaunchConfigGui(context);
            return;
        }

        if (!string.Equals(subcommand, "reload", StringComparison.Ordinal) || !string.IsNullOrWhiteSpace(remainder))
        {
            context.WriteLine("用法：/config 或 /config reload", true);
            return;
        }

        await HandleConfigReloadCommandAsync(runtime, options, string.Empty, context, cancellationToken).ConfigureAwait(false);
    }

    public async Task HandleConfigReloadCommandAsync(
        IExecutionRuntime runtime,
        ChatCommandOptions options,
        string rest,
        InteractiveConfigCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrWhiteSpace(rest))
        {
            context.WriteLine("用法：/reload 或 /config reload", true);
            return;
        }

        if (context.HasRunningConversation())
        {
            context.WriteLine("当前回合仍在运行，请先 /wait-complete 或 /interrupt 后再刷新配置。", true);
            return;
        }

        ControlPlaneProviderPackageReloadResult? providerReloadResult;
        try
        {
            providerReloadResult = await ReloadProviderPackagesAsync(runtime, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileLoadException or BadImageFormatException or InvalidOperationException)
        {
            context.WriteControlPlaneLine($"刷新模型 Provider 包失败：{ex.Message}", true);
            return;
        }

        ResolvedTianShuConfig resolvedConfig;
        try
        {
            resolvedConfig = context.LoadResolvedConfig(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            context.WriteControlPlaneLine($"刷新配置失败：{ex.Message}", true);
            return;
        }

        var reloadSummary = ResolveReloadSummary(options, resolvedConfig);

        var currentThreadId = context.GetCurrentThreadId();
        if (currentThreadId is null)
        {
            context.SetCurrentDisplayModel(reloadSummary.Model);
            context.WriteControlPlaneLine(
                $"已刷新配置：model={FormatConfigValue(reloadSummary.Model)}, provider={FormatConfigValue(reloadSummary.Provider)}。{FormatProviderReloadSummary(providerReloadResult)}下一条消息会使用最新模型路由配置创建线程。",
                false);
            return;
        }

        var resumed = await TianShuControlPlaneClientFactory.Create(runtime).Conversations.ResumeThreadAsync(
                new ControlPlaneResumeThreadCommand
                {
                    ThreadId = new ThreadId(currentThreadId),
                    Model = Normalize(options.RuntimeModel),
                    ModelProvider = Normalize(options.RuntimeModelProvider),
                    WorkingDirectory = options.WorkingDirectory,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (resumed is null)
        {
            context.WriteControlPlaneLine("刷新当前会话配置失败。", true);
            return;
        }

        options.RuntimeModel = Normalize(resumed.Thread.SessionConfiguration?.Model) ?? options.RuntimeModel;
        options.RuntimeModelProvider = Normalize(resumed.Thread.SessionConfiguration?.ModelProvider)
            ?? Normalize(resumed.Thread.SessionConfiguration?.ModelProviderId)
            ?? Normalize(resumed.Thread.ModelProvider)
            ?? options.RuntimeModelProvider;
        context.SetSessionActiveThreadId(resumed.Thread.ThreadId.Value);
        context.SetCurrentDisplayModel(options.RuntimeModel);
        context.MarkTerminalTurn();
        var resumedSummary = new ConfigReloadSummary(options.RuntimeModel, options.RuntimeModelProvider);
        context.WriteControlPlaneLine(
            $"已刷新当前会话配置：model={FormatConfigValue(resumedSummary.Model)}, provider={FormatConfigValue(resumedSummary.Provider)}。{FormatProviderReloadSummary(providerReloadResult)}",
            false);
    }

    private static ConfigReloadSummary ResolveReloadSummary(ChatCommandOptions options, ResolvedTianShuConfig resolvedConfig)
    {
        var runtimeModel = Normalize(options.RuntimeModel);
        var runtimeProvider = Normalize(options.RuntimeModelProvider);
        if (runtimeModel is not null || runtimeProvider is not null)
        {
            return new ConfigReloadSummary(runtimeModel, runtimeProvider);
        }

        var diagnostic = TianShuModelRouteSetDefaults.BuildRouteDiagnostic(
            resolvedConfig.RawConfig,
            TianShuModelRouteSetDefaults.DefaultRouteKind);
        if (diagnostic.RouteSetIsVirtual)
        {
            return new ConfigReloadSummary(null, null);
        }

        return new ConfigReloadSummary(
            Normalize(diagnostic.PreferredCandidate?.Model),
            Normalize(diagnostic.PreferredCandidate?.Provider));
    }

    private static async Task<ControlPlaneProviderPackageReloadResult> ReloadProviderPackagesAsync(
        IExecutionRuntime runtime,
        CancellationToken cancellationToken)
    {
        return await TianShuControlPlaneClientFactory.Create(runtime)
            .Catalog
            .ReloadProviderPackagesAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal static string? ResolveConfigGuiExecutable()
    {
        foreach (var candidate in EnumerateConfigGuiExecutableCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static bool StartConfigGuiProcess(string executablePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
        };
        using var process = Process.Start(startInfo);
        return process is not null;
    }

    private static void LaunchConfigGui(InteractiveConfigCommandContext context)
    {
        var executablePath = context.ResolveConfigGuiExecutable();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            context.WriteControlPlaneLine(
                $"未找到 ConfigGUI。请确认 `{ConfigGuiExecutableName}` 已安装到天枢 CLI 同目录或 `{GetDefaultUserBinDirectory()}`。",
                true);
            return;
        }

        try
        {
            if (!context.LaunchConfigGui(executablePath))
            {
                context.WriteControlPlaneLine($"启动 ConfigGUI 失败：{executablePath}", true);
                return;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            context.WriteControlPlaneLine($"启动 ConfigGUI 失败：{ex.Message}", true);
            return;
        }

        context.WriteControlPlaneLine($"已启动 ConfigGUI：{executablePath}", false);
    }

    private static IEnumerable<string> EnumerateConfigGuiExecutableCandidates()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield return Path.Combine(baseDirectory, ConfigGuiExecutableName);
        }

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(processDirectory))
        {
            yield return Path.Combine(processDirectory, ConfigGuiExecutableName);
        }

        var configuredHome = ConfigHomePathUtilities.ResolveTianShuHomePath();
        if (!string.IsNullOrWhiteSpace(configuredHome))
        {
            yield return Path.Combine(Path.GetFullPath(configuredHome), "bin", ConfigGuiExecutableName);
        }

        yield return Path.Combine(GetDefaultUserBinDirectory(), ConfigGuiExecutableName);
    }

    private static string GetDefaultUserBinDirectory()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? Path.Combine(Environment.CurrentDirectory, ".tianshu", "bin")
            : Path.Combine(profile, ".tianshu", "bin");
    }

    private static string ReadFirstToken(string text, out string remainder)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            remainder = string.Empty;
            return string.Empty;
        }

        var index = text.IndexOf(' ', StringComparison.Ordinal);
        if (index < 0)
        {
            remainder = string.Empty;
            return text;
        }

        remainder = text[(index + 1)..].Trim();
        return text[..index];
    }

    private static string FormatConfigValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<unset>" : value;

    private static string FormatProviderReloadSummary(ControlPlaneProviderPackageReloadResult? result)
    {
        if (result is null)
        {
            return string.Empty;
        }

        return $"模型 Provider 包已刷新：加载或复用 {result.LoadedAssemblyCount} 个程序集，当前 adapter={result.SupportedProtocolAdapterIds.Count}，问题={result.IssueCount}。";
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ConfigReloadSummary(string? Model, string? Provider);

internal sealed record InteractiveConfigCommandContext(
    Func<ChatCommandOptions, ResolvedTianShuConfig> LoadResolvedConfig,
    Func<bool> HasRunningConversation,
    Func<string?> GetCurrentThreadId,
    Action<string?> SetSessionActiveThreadId,
    Action<string?> SetCurrentDisplayModel,
    Action MarkTerminalTurn,
    Action<string, bool> WriteLine,
    Action<string, bool> WriteControlPlaneLine,
    Func<string?> ResolveConfigGuiExecutable,
    Func<string, bool> LaunchConfigGui);
