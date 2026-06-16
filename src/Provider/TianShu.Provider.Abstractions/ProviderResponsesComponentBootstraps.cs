namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider-neutral 的 responses 组件 bootstrap 注册表。
/// Provider-neutral registry for responses component bootstraps.
/// </summary>
public static class ProviderResponsesComponentBootstraps
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, IProviderResponsesComponentBootstrap> explicitBootstraps = new(StringComparer.OrdinalIgnoreCase);
    private static Lazy<IReadOnlyDictionary<string, IProviderResponsesComponentBootstrap>> bootstraps = CreateLazyBootstraps();

    public static IProviderResponsesComponentBootstrap Resolve(string? providerWireApi, string source)
    {
        var normalized = ProviderWireApi.NormalizeOrThrow(providerWireApi, source);
        if (normalized is null)
        {
            normalized = ProviderWireApi.Responses;
        }

        if (CurrentBootstraps.TryGetValue(normalized, out var bootstrap)
            || ReloadedBootstraps().TryGetValue(normalized, out bootstrap))
        {
            return bootstrap;
        }

        throw new InvalidOperationException(
            $"provider wire API `{normalized}` 尚未绑定 responses component bootstrap。");
    }

    public static IReadOnlyDictionary<string, TComponent> BuildComponents<TComponent>(
        Func<IProviderResponsesComponentBootstrap, TComponent> factory)
        where TComponent : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        Dictionary<string, TComponent> components = new(StringComparer.OrdinalIgnoreCase);
        foreach (var bootstrap in CurrentBootstraps.Values)
        {
            components.Add(bootstrap.WireApi, factory(bootstrap));
        }

        return components;
    }

    /// <summary>
    /// 显式注册 responses component bootstrap，供 NativeAOT CLI 或受控宿主跳过程序集扫描时使用。
    /// Explicitly registers a responses component bootstrap for NativeAOT CLI or controlled hosts that skip assembly scanning.
    /// </summary>
    /// <param name="bootstrap">responses component bootstrap。</param>
    /// <returns>注册后的 wire API 标识集合。Wire API identifiers after registration.</returns>
    public static IReadOnlyList<string> Register(IProviderResponsesComponentBootstrap bootstrap)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        lock (SyncRoot)
        {
            RegisterExplicitBootstrap(bootstrap);
            bootstraps = CreateLazyBootstraps();
            return explicitBootstraps.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>
    /// 重建 provider responses component bootstrap 注册表。
    /// Rebuilds the provider responses component bootstrap registry.
    /// </summary>
    /// <returns>重建后的 wire API 标识集合。Wire API identifiers after rebuild.</returns>
    public static IReadOnlyList<string> Reload()
    {
        lock (SyncRoot)
        {
            bootstraps = CreateLazyBootstraps();
            return CurrentBootstraps.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
        }
    }

    private static IReadOnlyDictionary<string, IProviderResponsesComponentBootstrap> CurrentBootstraps => bootstraps.Value;

    private static IReadOnlyDictionary<string, IProviderResponsesComponentBootstrap> ReloadedBootstraps()
    {
        Reload();
        return CurrentBootstraps;
    }

    private static Lazy<IReadOnlyDictionary<string, IProviderResponsesComponentBootstrap>> CreateLazyBootstraps()
        => new(LoadBootstraps, LazyThreadSafetyMode.ExecutionAndPublication);

    private static IReadOnlyDictionary<string, IProviderResponsesComponentBootstrap> LoadBootstraps()
        => ProviderBootstrapLoader.LoadBootstraps<IProviderResponsesComponentBootstrap>(
            static bootstrap => bootstrap.WireApi,
            static wireApi => $"检测到重复的 responses component bootstrap：{wireApi}",
            "未找到任何 responses component bootstrap，请确认当前宿主已引用至少一个 TianShu.Provider.* 项目。",
            explicitBootstraps.Values);

    private static void RegisterExplicitBootstrap(IProviderResponsesComponentBootstrap bootstrap)
    {
        var wireApi = ProviderWireApi.NormalizeOrThrow(bootstrap.WireApi, "provider responses component bootstrap");
        if (wireApi is null)
        {
            throw new InvalidOperationException("responses component bootstrap 缺少 wire API 标识。");
        }

        if (explicitBootstraps.TryGetValue(wireApi, out var existing)
            && existing.GetType() != bootstrap.GetType())
        {
            throw new InvalidOperationException($"检测到重复的 responses component bootstrap：{wireApi}");
        }

        explicitBootstraps[wireApi] = bootstrap;
    }
}
