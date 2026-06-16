using TianShu.Provider.Abstractions;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider bootstrap 运行时注册表。
/// Runtime registry for provider bootstraps.
/// </summary>
public static class ProviderRuntimeBootstrapRegistry
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IProviderRuntimeBootstrap> explicitBootstraps = new(StringComparer.OrdinalIgnoreCase);
    private static Lazy<IReadOnlyDictionary<string, IProviderRuntimeBootstrap>> bootstraps = CreateLazyBootstraps();

    public static string DefaultProtocolAdapterId => GetDefaultProtocolAdapterId();

    /// <summary>
    /// 获取当前 provider bootstrap 注册表的默认协议适配器标识。
    /// Gets the default protocol adapter identifier from the current provider bootstrap registry.
    /// </summary>
    /// <returns>默认协议适配器标识。Default protocol adapter identifier.</returns>
    public static string GetDefaultProtocolAdapterId()
        => Resolve(null).ProtocolAdapterId;

    /// <summary>
    /// 获取当前已注册并受支持的协议适配器标识集合。
    /// Gets the currently registered and supported protocol adapter identifiers.
    /// </summary>
    /// <returns>受支持的协议适配器标识集合。Supported protocol adapter identifiers.</returns>
    public static IReadOnlyList<string> GetSupportedProtocolAdapterIds()
        => CurrentBootstraps.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// 生成统一的“不受支持协议适配器”错误文案。
    /// Builds the canonical unsupported protocol adapter error message.
    /// </summary>
    /// <param name="protocolAdapterId">当前收到的协议适配器标识。Protocol adapter identifier received from the caller.</param>
    /// <returns>统一错误文案。Canonical error message.</returns>
    public static string BuildUnsupportedProtocolAdapterMessage(string? protocolAdapterId)
        => $"仅支持协议适配器 `{string.Join("`, `", GetSupportedProtocolAdapterIds())}`，当前值为 `{Normalize(protocolAdapterId)}`。";

    /// <summary>
    /// 为指定协议适配器创建运行态 provider 组件快照。
    /// Creates the runtime provider component snapshot for the specified protocol adapter.
    /// </summary>
    /// <param name="protocolAdapterId">协议适配器标识。Protocol adapter identifier.</param>
    /// <returns>运行态 provider 组件快照。Runtime provider component snapshot.</returns>
    public static ProviderRuntimeState CreateRuntimeState(string? protocolAdapterId)
        => new(Resolve(protocolAdapterId));

    /// <summary>
    /// 显式注册运行态 provider bootstrap，供 NativeAOT CLI 或受控宿主跳过程序集扫描时使用。
    /// Explicitly registers a runtime provider bootstrap for NativeAOT CLI or controlled hosts that skip assembly scanning.
    /// </summary>
    /// <param name="bootstrap">运行态 provider bootstrap。Runtime provider bootstrap.</param>
    /// <returns>注册后的协议适配器标识集合。Protocol adapter identifiers after registration.</returns>
    public static IReadOnlyList<string> Register(IProviderRuntimeBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        lock (SyncRoot)
        {
            RegisterExplicitBootstrap(bootstrap);
            bootstraps = CreateLazyBootstraps();
            return explicitBootstraps.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        }
    }

    public static IProviderRuntimeBootstrap Resolve(string? protocolAdapterId)
    {
        var normalized = Normalize(protocolAdapterId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CurrentBootstraps.Values.First();
        }

        if (CurrentBootstraps.TryGetValue(normalized, out var bootstrap))
        {
            return bootstrap;
        }

        throw new InvalidOperationException(BuildUnsupportedProtocolAdapterMessage(normalized));
    }

    public static string NormalizeProtocolAdapterId(string? protocolAdapterId, string source)
    {
        var normalized = Normalize(protocolAdapterId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{source} 缺少协议适配器标识。");
        }

        return Resolve(normalized).ProtocolAdapterId;
    }

    /// <summary>
    /// 规范化可选协议适配器标识；空值保持为空，其余值返回受支持的规范标识。
    /// Normalizes an optional protocol adapter identifier; blank values stay null, others return the supported canonical identifier.
    /// </summary>
    /// <param name="protocolAdapterId">待规范化的协议适配器标识。Protocol adapter identifier to normalize.</param>
    /// <param name="source">调用来源，用于错误提示。Caller source used in error messages.</param>
    /// <returns>规范化后的协议适配器标识，或空值。Canonical protocol adapter identifier, or null.</returns>
    public static string? NormalizeOptionalProtocolAdapterId(string? protocolAdapterId, string source)
    {
        var normalized = Normalize(protocolAdapterId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Resolve(normalized).ProtocolAdapterId;
    }

    /// <summary>
    /// 重建 provider runtime bootstrap 注册表。
    /// Rebuilds the provider runtime bootstrap registry.
    /// </summary>
    /// <returns>重建后的协议适配器标识集合。Protocol adapter identifiers after rebuild.</returns>
    public static IReadOnlyList<string> Reload()
    {
        lock (SyncRoot)
        {
            bootstraps = CreateLazyBootstraps();
            return GetSupportedProtocolAdapterIds();
        }
    }

    private static IReadOnlyDictionary<string, IProviderRuntimeBootstrap> CurrentBootstraps => bootstraps.Value;

    private static Lazy<IReadOnlyDictionary<string, IProviderRuntimeBootstrap>> CreateLazyBootstraps()
        => new(LoadBootstraps, LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyDictionary<string, IProviderRuntimeBootstrap> LoadBootstraps()
        => ProviderBootstrapLoader.LoadBootstraps<IProviderRuntimeBootstrap>(
            static bootstrap => bootstrap.ProtocolAdapterId,
            static protocolAdapterId => $"检测到重复的协议适配器 bootstrap：{protocolAdapterId}",
            "未找到任何执行 provider bootstrap，请确认当前宿主已引用至少一个 TianShu.Provider.* 项目。",
            explicitBootstraps.Values);

    private static void RegisterExplicitBootstrap(IProviderRuntimeBootstrap bootstrap)
    {
        var protocolAdapterId = Normalize(bootstrap.ProtocolAdapterId);
        if (string.IsNullOrWhiteSpace(protocolAdapterId))
        {
            throw new InvalidOperationException("provider runtime bootstrap 缺少协议适配器标识。");
        }

        if (explicitBootstraps.TryGetValue(protocolAdapterId, out var existing)
            && existing.GetType() != bootstrap.GetType())
        {
            throw new InvalidOperationException($"检测到重复的协议适配器 bootstrap：{protocolAdapterId}");
        }

        explicitBootstraps[protocolAdapterId] = bootstrap;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
