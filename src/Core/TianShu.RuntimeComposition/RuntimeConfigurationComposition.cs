using TianShu.Configuration;
using TianShu.Contracts.Sessions;

namespace TianShu.RuntimeComposition;

/// <summary>
/// TianShu 运行时配置加载组合入口。
/// Runtime composition entry point for TianShu configuration loading.
/// </summary>
public sealed class RuntimeConfigurationComposition
{
    private readonly TianShuTomlConfigurationLoader loader = new();

    /// <summary>
    /// 加载有效配置快照。
    /// Loads an effective configuration snapshot.
    /// </summary>
    public ResolvedTianShuConfig Load(
        string? configFilePath,
        string? profileOverride,
        IReadOnlyDictionary<string, string>? configOverrides = null,
        string? workingDirectory = null)
        => loader.Load(configFilePath, profileOverride, configOverrides, workingDirectory);

    /// <summary>
    /// 将配置快照应用到 runtime 初始化命令。
    /// Applies a configuration snapshot to the runtime initialization command.
    /// </summary>
    public static void ApplyToOptions(ControlPlaneInitializeRuntimeCommand options, ResolvedTianShuConfig config)
        => TianShuTomlConfigurationLoader.ApplyToOptions(options, config);

    /// <summary>
    /// 解析默认配置路径；便携包内运行时优先返回包根配置，否则回退用户级配置。
    /// Resolves the default config path; portable package execution prefers package-root config, otherwise user-level config.
    /// </summary>
    public static string ResolveDefaultPath()
        => TianShuTomlConfigurationLoader.ResolveDefaultPath(AppContext.BaseDirectory);

    /// <summary>
    /// 从指定程序目录解析默认配置路径，主要供便携包布局测试和宿主探测使用。
    /// Resolves the default config path from a specific program directory, mainly for portable layout tests and host probing.
    /// </summary>
    public static string ResolveDefaultPath(string? programDirectory)
        => TianShuTomlConfigurationLoader.ResolveDefaultPath(programDirectory);
}
