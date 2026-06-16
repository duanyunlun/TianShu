namespace TianShu.Provider.Abstractions;

/// <summary>
/// provider bootstrap 组装 southbound CLI 参数时可见的最小上下文。
/// Minimal context exposed to provider bootstraps when composing southbound CLI arguments.
/// </summary>
public sealed class ProviderRuntimeCliArguments
{
    /// <summary>
    /// 需要透传到 southbound CLI 的配置覆盖项。
    /// Config overrides that should be forwarded to the southbound CLI.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ConfigOverrides { get; init; }

    /// <summary>
    /// 当前激活的 profile 名称。
    /// Active profile name.
    /// </summary>
    public string? ProfileName { get; init; }

    /// <summary>
    /// profile 是否已由宿主侧配置解析得到。
    /// Whether the profile was already resolved from host-side config loading.
    /// </summary>
    public bool ProfileNameResolvedFromConfig { get; init; }

    /// <summary>
    /// 当前显式指定的 config 文件路径。
    /// Explicit config file path currently selected.
    /// </summary>
    public string? ConfigFilePath { get; init; }

    /// <summary>
    /// 宿主约定的默认 config 文件路径。
    /// Host-conventional default config file path.
    /// </summary>
    public string? DefaultConfigFilePath { get; init; }
}
