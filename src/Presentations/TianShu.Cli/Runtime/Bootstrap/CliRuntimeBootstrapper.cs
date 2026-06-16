using TianShu.AppHost.Configuration;
using TianShu.Configuration;
using TianShu.Contracts.Sessions;
using TianShu.RuntimeComposition;

namespace TianShu.Cli;

internal sealed class CliRuntimeBootstrapResult
{
    public required ControlPlaneInitializeRuntimeCommand RuntimeOptions { get; init; }

    public string? AppHostProjectPath { get; init; }

    public required ResolvedTianShuConfig ResolvedConfig { get; init; }
}

internal static class CliRuntimeBootstrapper
{
    public static CliRuntimeBootstrapResult Prepare(CliRuntimeCommandOptions commandOptions)
    {
        ArgumentNullException.ThrowIfNull(commandOptions);

        if (!Directory.Exists(commandOptions.WorkingDirectory))
        {
            throw new DirectoryNotFoundException($"工作目录不存在：{commandOptions.WorkingDirectory}");
        }

        var runtimeOptions = commandOptions.ToRuntimeOptions();
        var appHostResolution = CliAppHostLaunchResolver.Resolve(commandOptions.AppHostProjectPath, commandOptions.WorkingDirectory);
        if (!appHostResolution.IsResolved)
        {
            throw new FileNotFoundException("未找到 TianShu 宿主启动入口。", commandOptions.AppHostProjectPath);
        }

        CliAppHostLaunchResolver.ApplyToRuntimeOptions(runtimeOptions, appHostResolution);

        var loader = new RuntimeConfigurationComposition();
        var resolvedConfig = loader.Load(runtimeOptions.ConfigFilePath, runtimeOptions.ProfileName, runtimeOptions.ConfigOverrides, runtimeOptions.WorkingDirectory);
        RuntimeConfigurationComposition.ApplyToOptions(runtimeOptions, resolvedConfig);

        return new CliRuntimeBootstrapResult
        {
            RuntimeOptions = runtimeOptions,
            AppHostProjectPath = appHostResolution.AppHostProjectPath,
            ResolvedConfig = resolvedConfig,
        };
    }

    public static string? ResolveAppHostProjectPath(string? configuredPath, string workingDirectory)
        => CliAppHostLaunchResolver.ResolveAppHostProjectPath(configuredPath, workingDirectory);
}
