using TianShu.Contracts.Modules;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 发现并解析通用 TianShu Module manifest；该类型只产出发现快照，不加载模块实现。
/// Discovers and parses generic TianShu Module manifests; this type only produces discovery snapshots and never loads implementations.
/// </summary>
public sealed class TianShuModuleManifestDiscovery
{
    public const string ModuleDirectoryName = "modules";
    public const string ManifestFileName = "module.toml";

    public ModuleDiscoverySnapshot Load(
        string rootDirectory,
        IReadOnlyList<ModuleDescriptor>? builtInDescriptors = null,
        IReadOnlyList<ModuleDiscoveryRoot>? additionalRoots = null,
        IReadOnlySet<string>? disabledModuleIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        var root = Path.GetFullPath(rootDirectory);
        var roots = BuildRoots(root, builtInDescriptors, additionalRoots);
        var issues = new List<ModuleDiscoveryIssue>();
        var candidates = new List<ModuleDiscoveryCandidate>();

        if (builtInDescriptors is not null)
        {
            var builtInRoot = roots.FirstOrDefault(static item => item.SourceKind == ModuleDiscoverySourceKind.BuiltIn);
            if (builtInRoot is not null && builtInRoot.Enabled)
            {
                candidates.AddRange(builtInDescriptors.Select(descriptor => FromBuiltInDescriptor(descriptor, builtInRoot)));
            }
        }

        foreach (var discoveryRoot in roots.Where(static item => item.SourceKind != ModuleDiscoverySourceKind.BuiltIn && item.Enabled))
        {
            if (!Directory.Exists(discoveryRoot.Path))
            {
                continue;
            }

            foreach (var manifestPath in Directory
                         .EnumerateFiles(discoveryRoot.Path, ManifestFileName, SearchOption.AllDirectories)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    candidates.Add(ReadCandidate(manifestPath, discoveryRoot));
                }
                catch (Exception ex)
                {
                    issues.Add(new ModuleDiscoveryIssue(
                        "module_manifest.parse_failed",
                        $"无法解析 Module manifest：{ex.Message}",
                        ModuleDiscoveryIssueSeverity.Error,
                        manifestPath: Path.GetFullPath(manifestPath)));
                }
            }
        }

        return ModuleDiscoveryResolver.Resolve(roots, candidates, issues, disabledModuleIds);
    }

    public static string ResolveModuleRootDirectory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return Path.Combine(Path.GetFullPath(rootDirectory), ModuleDirectoryName);
    }

    private static IReadOnlyList<ModuleDiscoveryRoot> BuildRoots(
        string rootDirectory,
        IReadOnlyList<ModuleDescriptor>? builtInDescriptors,
        IReadOnlyList<ModuleDiscoveryRoot>? additionalRoots)
    {
        var roots = new List<ModuleDiscoveryRoot>();
        if (builtInDescriptors is { Count: > 0 })
        {
            roots.Add(new ModuleDiscoveryRoot(
                "builtin",
                "builtin://modules",
                ModuleDiscoverySourceKind.BuiltIn,
                ModuleTrustLevel.BuiltIn,
                priority: 0));
        }

        roots.Add(new ModuleDiscoveryRoot(
            "user-home",
            ResolveModuleRootDirectory(rootDirectory),
            ModuleDiscoverySourceKind.UserHome,
            ModuleTrustLevel.UserInstalled,
            priority: 10));

        if (additionalRoots is not null)
        {
            roots.AddRange(additionalRoots);
        }

        return roots;
    }

    private static ModuleDiscoveryCandidate FromBuiltInDescriptor(ModuleDescriptor descriptor, ModuleDiscoveryRoot root)
    {
        var manifest = new ModuleManifestProjection(
            descriptor.ModuleId,
            descriptor.Kind,
            descriptor.DisplayName,
            descriptor.Version,
            new ModuleManifestSource($"builtin://{descriptor.ModuleId}", root),
            enabled: true,
            priority: 0,
            minimumTianShuVersion: descriptor.MinimumTianShuVersion,
            capabilities: descriptor.Capabilities.Select(static capability => capability.CapabilityId).ToArray(),
            diagnostics: descriptor.Audit.EventKinds,
            implementationBinding: descriptor.ImplementationBinding);

        return new ModuleDiscoveryCandidate(manifest, descriptor);
    }

    private static ModuleDiscoveryCandidate ReadCandidate(string manifestPath, ModuleDiscoveryRoot root)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var table = TomlTable.From(Toml.Parse(File.ReadAllText(fullPath), fullPath));
        var moduleId = RequiredString(table, "id");
        var version = RequiredString(table, "version");
        var kind = ParseKind(ReadString(table, "kind"));
        var displayName = ReadString(table, "display_name") ?? moduleId;
        var implementation = ReadImplementation(table);
        var manifest = new ModuleManifestProjection(
            moduleId,
            kind,
            displayName,
            version,
            new ModuleManifestSource(fullPath, root),
            enabled: ReadBoolean(table, "enabled") ?? true,
            priority: ReadInteger(table, "priority") ?? 0,
            sdkContractVersion: ReadString(table, "sdk_contract_version"),
            minimumTianShuVersion: ReadString(table, "min_tianshu_version"),
            capabilities: TianShuExtensionManifestCommon.ReadStringArray(table, "capabilities"),
            diagnostics: TianShuExtensionManifestCommon.ReadStringArray(table, "diagnostics"),
            implementationBinding: implementation,
            capabilitySchemaVersion: ReadString(table, "capability_schema_version"));

        return new ModuleDiscoveryCandidate(manifest);
    }

    private static ModuleImplementationBinding? ReadImplementation(TomlTable table)
    {
        if (!table.TryGetValue("implementation", out var value) || value is not TomlTable implementation)
        {
            return null;
        }

        var projectName = ReadString(implementation, "project");
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return null;
        }

        return new ModuleImplementationBinding(
            projectName,
            ReadString(implementation, "type"),
            ReadString(implementation, "package_id"));
    }

    private static ModuleKind ParseKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ModuleKind.Unspecified;
        }

        var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        foreach (var kind in Enum.GetValues<ModuleKind>())
        {
            var name = kind.ToString().Replace("_", string.Empty, StringComparison.Ordinal);
            if (string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return ModuleKind.Unspecified;
    }

    private static string RequiredString(TomlTable table, string key)
    {
        var value = ReadString(table, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"缺少必需字段：{key}");
        }

        return value;
    }

    private static string? ReadString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as string : null;

    private static bool? ReadBoolean(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? value as bool? : null;

    private static int? ReadInteger(TomlTable table, string key)
        => table.TryGetValue(key, out var value) ? Convert.ToInt32(value) : null;
}
