using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using TianShu.Contracts.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Provider.Abstractions;

/// <summary>
/// 从用户级 provider package manifest 预加载 provider adapter 程序集。
/// Preloads provider adapter assemblies from user-level provider package manifests.
/// </summary>
public static class ProviderPackageAssemblyPreloader
{
    public const string ProviderDirectoryName = "modules/model/provider-adapters";
    public const string ManifestFileName = "provider.toml";
    private const string CurrentTianShuVersion = "0.1.0";

    /// <summary>
    /// 扫描并预加载 provider package manifest 中启用的 assembly adapter。
    /// Scans and preloads enabled assembly adapters declared by provider package manifests.
    /// </summary>
    /// <param name="homeDirectory">TianShu home；为空时按环境变量和用户 home 推导。TianShu home; when blank, derives it from environment and user home.</param>
    /// <returns>预加载结果。Preload result.</returns>
    public static ProviderPackageAssemblyPreloadResult TryLoadProviderPackages(string? homeDirectory = null)
    {
        var root = ResolveHomeDirectory(homeDirectory);
        var providerRoot = TianShuRuntimeLayoutPaths.ResolveModulePathFromHome(root, "model", "provider-adapters");
        return TryLoadProviderPackagesFromRoot(root, providerRoot);
    }

    /// <summary>
    /// 按调用方已经解析出的 Provider 包目录扫描并预加载程序集。
    /// Scans and preloads assemblies from a caller-resolved provider package directory.
    /// </summary>
    public static ProviderPackageAssemblyPreloadResult TryLoadProviderPackagesFromRoot(string homeDirectory, string providerRootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerRootDirectory);

        var root = Path.GetFullPath(homeDirectory);
        var providerRoot = Path.GetFullPath(providerRootDirectory);
        List<string> loadedAssemblies = [];
        List<ProviderPackageAssemblyPreloadIssue> issues = [];

        if (!Directory.Exists(providerRoot))
        {
            return new ProviderPackageAssemblyPreloadResult(root, providerRoot, loadedAssemblies, issues);
        }

        foreach (var manifestPath in EnumerateManifestPaths(providerRoot))
        {
            TryLoadManifest(manifestPath, loadedAssemblies, issues);
        }

        return new ProviderPackageAssemblyPreloadResult(root, providerRoot, loadedAssemblies, issues);
    }

    private static void TryLoadManifest(
        string manifestPath,
        ICollection<string> loadedAssemblies,
        ICollection<ProviderPackageAssemblyPreloadIssue> issues)
    {
        TomlTable manifest;
        try
        {
            manifest = Toml.ToModel(File.ReadAllText(manifestPath)) as TomlTable
                       ?? throw new InvalidOperationException("provider manifest 根节点必须是 TOML table。");
        }
        catch (Exception ex)
        {
            issues.Add(new ProviderPackageAssemblyPreloadIssue(
                manifestPath,
                null,
                "parse_failed",
                $"无法解析 provider package manifest：{ex.Message}"));
            return;
        }

        if (!ReadBoolean(manifest, "enabled", fallback: true))
        {
            return;
        }

        if (!IsCompatible(manifest, out var compatibilityReason))
        {
            issues.Add(new ProviderPackageAssemblyPreloadIssue(
                manifestPath,
                null,
                "version_incompatible",
                $"provider package manifest 不兼容：{compatibilityReason}"));
            return;
        }

        var packageDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        foreach (var adapter in ReadAdapters(manifest))
        {
            var adapterId = ReadString(adapter, "id");
            if (!ReadBoolean(adapter, "enabled", fallback: true))
            {
                continue;
            }

            var adapterType = NormalizeType(ReadString(adapter, "type"), "assembly");
            if (!string.Equals(adapterType, "assembly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var assemblyPath = ReadString(adapter, "assembly_path");
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                issues.Add(new ProviderPackageAssemblyPreloadIssue(
                    manifestPath,
                    adapterId,
                    "assembly_path_missing",
                    "provider adapter 条目缺少 assembly_path。"));
                continue;
            }

            var fullPath = Path.IsPathFullyQualified(assemblyPath)
                ? Path.GetFullPath(assemblyPath)
                : Path.GetFullPath(Path.Combine(packageDirectory, assemblyPath));

            if (!File.Exists(fullPath))
            {
                issues.Add(new ProviderPackageAssemblyPreloadIssue(
                    manifestPath,
                    adapterId,
                    "assembly_not_found",
                    $"provider adapter 程序集不存在：{fullPath}"));
                continue;
            }

            try
            {
                LoadAssembly(fullPath);
                loadedAssemblies.Add(fullPath);
            }
            catch (Exception ex) when (ex is FileLoadException or BadImageFormatException or FileNotFoundException)
            {
                issues.Add(new ProviderPackageAssemblyPreloadIssue(
                    manifestPath,
                    adapterId,
                    "assembly_load_failed",
                    $"无法加载 provider adapter 程序集：{ex.Message}"));
            }
        }
    }

    private static Assembly LoadAssembly(string fullPath)
    {
        return LoadAssemblyCore(fullPath);
    }

    [UnconditionalSuppressMessage(
        "SingleFile",
        "IL3000:Avoid accessing Assembly file path when publishing as a single file",
        Justification = "Provider adapters are external package assemblies loaded from manifest paths; the Location check only deduplicates non-bundled assemblies.")]
    private static Assembly LoadAssemblyCore(string fullPath)
    {
        var existing = AssemblyLoadContext.All
            .SelectMany(static context => context.Assemblies)
            .FirstOrDefault(assembly =>
            {
                try
                {
                    return !string.IsNullOrWhiteSpace(assembly.Location)
                           && string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase);
                }
                catch (NotSupportedException)
                {
                    return false;
                }
            });

        return existing ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static IReadOnlyList<TomlTable> ReadAdapters(TomlTable manifest)
    {
        if (manifest.TryGetValue("adapters", out var rawAdapters) && rawAdapters is TomlTableArray adapterArray)
        {
            return adapterArray.OfType<TomlTable>().ToArray();
        }

        return [];
    }

    private static IEnumerable<string> EnumerateManifestPaths(string providerRoot)
        => Directory.EnumerateDirectories(providerRoot)
            .Select(directory => Path.Combine(directory, ManifestFileName))
            .Where(File.Exists)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);

    private static bool ReadBoolean(TomlTable table, string key, bool fallback)
        => table.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static string NormalizeType(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static bool IsCompatible(TomlTable manifest, out string? reason)
    {
        reason = null;
        var minimumText = ReadString(manifest, "min_tianshu_version");
        if (string.IsNullOrWhiteSpace(minimumText))
        {
            return true;
        }

        if (!Version.TryParse(minimumText, out var minimum))
        {
            reason = $"min_tianshu_version 不是有效版本：{minimumText}";
            return false;
        }

        if (!Version.TryParse(CurrentTianShuVersion, out var current))
        {
            reason = $"当前 TianShu 版本不是有效版本：{CurrentTianShuVersion}";
            return false;
        }

        if (current < minimum)
        {
            reason = $"需要 TianShu >= {minimum}，当前为 {current}";
            return false;
        }

        return true;
    }

    private static string ResolveHomeDirectory(string? homeDirectory)
    {
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            return Path.GetFullPath(homeDirectory);
        }

        return Path.GetFullPath(TianShuRuntimeLayoutPaths.ResolveTianShuHomePath());
    }
}

/// <summary>
/// Provider package assembly 预加载结果。
/// Provider package assembly preload result.
/// </summary>
public sealed record ProviderPackageAssemblyPreloadResult(
    string HomeDirectory,
    string ProviderRootDirectory,
    IReadOnlyList<string> LoadedAssemblies,
    IReadOnlyList<ProviderPackageAssemblyPreloadIssue> Issues);

/// <summary>
/// Provider package assembly 预加载问题。
/// Provider package assembly preload issue.
/// </summary>
public sealed record ProviderPackageAssemblyPreloadIssue(
    string ManifestPath,
    string? AdapterId,
    string Code,
    string Message);
