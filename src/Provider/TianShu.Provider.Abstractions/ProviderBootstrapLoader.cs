using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// Provider bootstrap 的受控装载入口。
/// Controlled loading entry point for provider bootstraps.
/// </summary>
public static class ProviderBootstrapLoader
{
    private const string ProviderAssemblyPrefix = "TianShu.Provider.";
    private const string ProviderAbstractionsAssemblyName = "TianShu.Provider.Abstractions";

    /// <summary>
    /// 按 provider 程序集自声明的注册信息装载指定类型的 bootstrap。
    /// Loads bootstraps of the requested type from provider self-declared registrations.
    /// </summary>
    /// <typeparam name="TBootstrap">目标 bootstrap 抽象类型。Target bootstrap abstraction type.</typeparam>
    /// <param name="keySelector">从 bootstrap 提取唯一键。Selects the unique key from each bootstrap.</param>
    /// <param name="duplicateMessageFactory">重复键错误文案工厂。Builds the duplicate-key error message.</param>
    /// <param name="emptyMessage">未发现任何 bootstrap 时的错误文案。Error message used when no bootstrap is found.</param>
    /// <returns>按唯一键索引的 bootstrap 字典。Bootstrap map indexed by unique key.</returns>
    public static IReadOnlyDictionary<string, TBootstrap> LoadBootstraps<TBootstrap>(
        Func<TBootstrap, string> keySelector,
        Func<string, string> duplicateMessageFactory,
        string emptyMessage,
        IEnumerable<TBootstrap>? explicitBootstraps = null)
        where TBootstrap : class
    {
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(duplicateMessageFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(emptyMessage);

        Dictionary<string, TBootstrap> bootstraps = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> loadedBootstrapTypes = new(StringComparer.Ordinal);

        if (explicitBootstraps is not null)
        {
            foreach (var bootstrap in explicitBootstraps)
            {
                AddBootstrap(bootstrap, keySelector, duplicateMessageFactory, bootstraps, loadedBootstrapTypes);
            }
        }

        foreach (var assembly in LoadCandidateProviderAssemblies())
        {
            foreach (var registration in assembly.GetCustomAttributes<ProviderBootstrapRegistrationAttribute>())
            {
                var bootstrapType = registration.BootstrapType;
                if (!typeof(TBootstrap).IsAssignableFrom(bootstrapType))
                {
                    continue;
                }

                var bootstrapIdentity = bootstrapType.AssemblyQualifiedName ?? bootstrapType.FullName ?? bootstrapType.Name;
                if (loadedBootstrapTypes.Contains(bootstrapIdentity))
                {
                    continue;
                }

                if (Activator.CreateInstance(bootstrapType) is not TBootstrap bootstrap)
                {
                    throw new InvalidOperationException(
                        $"无法实例化 provider bootstrap `{bootstrapType.FullName}`，请确认其为可构造的公开类型。");
                }

                AddBootstrap(bootstrap, keySelector, duplicateMessageFactory, bootstraps, loadedBootstrapTypes);
            }
        }

        if (bootstraps.Count == 0)
        {
            throw new InvalidOperationException(emptyMessage);
        }

        return bootstraps;
    }

    private static void AddBootstrap<TBootstrap>(
        TBootstrap bootstrap,
        Func<TBootstrap, string> keySelector,
        Func<string, string> duplicateMessageFactory,
        IDictionary<string, TBootstrap> bootstraps,
        ISet<string> loadedBootstrapTypes)
        where TBootstrap : class
    {
        ArgumentNullException.ThrowIfNull(bootstrap);

        var bootstrapType = bootstrap.GetType();
        var bootstrapIdentity = bootstrapType.AssemblyQualifiedName ?? bootstrapType.FullName ?? bootstrapType.Name;
        if (!loadedBootstrapTypes.Add(bootstrapIdentity))
        {
            return;
        }

        var key = keySelector(bootstrap);
        if (!bootstraps.TryAdd(key, bootstrap))
        {
            throw new InvalidOperationException(duplicateMessageFactory(key));
        }
    }

    private static IReadOnlyList<Assembly> LoadCandidateProviderAssemblies()
    {
        Dictionary<string, Assembly> assemblies = new(StringComparer.OrdinalIgnoreCase);
        Queue<AssemblyName> pendingReferences = [];
        HashSet<string> queuedReferences = new(StringComparer.OrdinalIgnoreCase);
        var supportsDynamicAssemblyLoading = RuntimeFeature.IsDynamicCodeSupported;

        foreach (var assembly in AssemblyLoadContext.All.SelectMany(static context => context.Assemblies))
        {
            RegisterLoadedProviderAssembly(assembly, assemblies);
            if (supportsDynamicAssemblyLoading)
            {
                EnqueueProviderReferences(assembly, assemblies, pendingReferences, queuedReferences);
            }
        }

        if (supportsDynamicAssemblyLoading)
        {
            while (pendingReferences.Count > 0)
            {
                var assemblyName = pendingReferences.Dequeue();
                if (assemblies.ContainsKey(assemblyName.Name!))
                {
                    continue;
                }

                var assembly = Assembly.Load(assemblyName);
                assemblies[assemblyName.Name!] = assembly;
                EnqueueProviderReferences(assembly, assemblies, pendingReferences, queuedReferences);
            }

            LoadProviderAssembliesFromLoadedDirectories(assemblies, pendingReferences, queuedReferences);

            while (pendingReferences.Count > 0)
            {
                var assemblyName = pendingReferences.Dequeue();
                if (assemblies.ContainsKey(assemblyName.Name!))
                {
                    continue;
                }

                var assembly = Assembly.Load(assemblyName);
                assemblies[assemblyName.Name!] = assembly;
                EnqueueProviderReferences(assembly, assemblies, pendingReferences, queuedReferences);
            }
        }

        return assemblies.Values
            .OrderBy(static assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RegisterLoadedProviderAssembly(Assembly assembly, IDictionary<string, Assembly> assemblies)
    {
        var assemblyName = assembly.GetName().Name;
        if (!IsProviderLibrary(assemblyName))
        {
            return;
        }

        assemblies.TryAdd(assemblyName!, assembly);
    }

    private static void EnqueueProviderReferences(
        Assembly assembly,
        IDictionary<string, Assembly> assemblies,
        Queue<AssemblyName> pendingReferences,
        ISet<string> queuedReferences)
    {
        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            if (!IsProviderLibrary(reference.Name)
                || assemblies.ContainsKey(reference.Name!)
                || !queuedReferences.Add(reference.Name!))
            {
                continue;
            }

            pendingReferences.Enqueue(reference);
        }
    }

    private static void LoadProviderAssembliesFromLoadedDirectories(
        IDictionary<string, Assembly> assemblies,
        Queue<AssemblyName> pendingReferences,
        ISet<string> queuedReferences)
    {
        var loadedAssemblies = AssemblyLoadContext.All.SelectMany(static context => context.Assemblies).ToArray();
        var directories = new[] { AppContext.BaseDirectory }
            .Where(static directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in directories)
        {
            foreach (var assemblyPath in Directory.EnumerateFiles(directory!, $"{ProviderAssemblyPrefix}*.dll"))
            {
                var candidateName = Path.GetFileNameWithoutExtension(assemblyPath);
                if (!IsProviderLibrary(candidateName)
                    || assemblies.ContainsKey(candidateName))
                {
                    continue;
                }

                var assembly = loadedAssemblies.FirstOrDefault(
                                   loaded => string.Equals(loaded.GetName().Name, candidateName, StringComparison.OrdinalIgnoreCase))
                               ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));

                assemblies[candidateName] = assembly;
                EnqueueProviderReferences(assembly, assemblies, pendingReferences, queuedReferences);
            }
        }
    }

    private static bool IsProviderLibrary(string? assemblyName)
        => !string.IsNullOrWhiteSpace(assemblyName)
           && assemblyName.StartsWith(ProviderAssemblyPrefix, StringComparison.Ordinal)
           && !string.Equals(assemblyName, ProviderAbstractionsAssemblyName, StringComparison.Ordinal);
}
