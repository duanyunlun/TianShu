using System.Text.Json;
using TianShu.Configuration;
using TianShuPromptConfigLayer = TianShu.Configuration.TianShuPromptConfigLayer;
using TianShuPromptConfigUtilities = TianShu.Configuration.TianShuPromptConfigUtilities;

namespace TianShu.AppHost.Configuration;

/// <summary>
/// 配置读取快照构建与 request override 合并辅助件。
/// Helpers for config-read snapshot construction and request-override merging.
/// </summary>
internal static class KernelConfigSnapshotUtilities
{
    private const string LegacyManagedConfigMigrationOnlyDisabledReason = "legacy managed_config.toml 只作为迁移诊断层展示，不再参与正式配置合并。";

    public static KernelConfigReadSnapshot BuildConfigReadSnapshot(
        bool includeLayers,
        string? cwd,
        Dictionary<string, string> processOverrideValues,
        string userConfigPath)
    {
        var orderedLayers = new List<KernelConfigReadLayer>();
        var systemConfigPath = TianShuConfigTomlPathResolver.ResolveSystemConfigTomlPath();
        var normalizedUserConfigPath = Path.GetFullPath(userConfigPath);
        var normalizedCwd = TianShuConfigTomlPathResolver.NormalizeDirectory(cwd);

        var systemConfig = KernelConfigObjectUtilities.ReadTomlConfigObject(systemConfigPath, suppressErrors: false);
        orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
            new
            {
                type = "system",
                file = systemConfigPath,
            },
            systemConfig));

        AddUserModuleConfigLayers(
            orderedLayers,
            normalizedUserConfigPath,
            ["model", "route-sets"],
            "userModelRouteSet");
        AddUserModuleConfigLayers(
            orderedLayers,
            normalizedUserConfigPath,
            ["model", "protocol-rules"],
            "userModelProtocolRuleSet");

        var userConfig = KernelConfigObjectUtilities.ReadTomlConfigObject(normalizedUserConfigPath, suppressErrors: false);
        orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
            new
            {
                type = "user",
                file = normalizedUserConfigPath,
            },
            userConfig));

        AddUserProviderInstanceConfigLayers(orderedLayers, normalizedUserConfigPath, userConfig);

        var processOverrideConfig = KernelConfigWriteUtilities.BuildConfigObject(processOverrideValues);
        var sessionFlagsConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (processOverrideConfig.Count > 0)
        {
            KernelConfigReadLayerUtilities.MergeConfigObjects(sessionFlagsConfig, processOverrideConfig);
        }

        var nonProjectConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var layer in orderedLayers)
        {
            KernelConfigReadLayerUtilities.MergeConfigObjects(nonProjectConfig, layer.Config);
        }

        if (sessionFlagsConfig.Count > 0)
        {
            KernelConfigReadLayerUtilities.MergeConfigObjects(nonProjectConfig, sessionFlagsConfig);
        }

        var projectRootMarkers = TianShuProjectRootResolver.ResolveProjectRootMarkersStrict(nonProjectConfig);
        var projectLayerDirectories = TianShuConfigTomlPathResolver.EnumerateProjectLayerDirectories(normalizedCwd, projectRootMarkers);
        foreach (var dotTianShuFolder in projectLayerDirectories)
        {
            var layerDirectory = Directory.GetParent(dotTianShuFolder)?.FullName;
            var projectConfigPath = Path.Combine(dotTianShuFolder, "tianshu.toml");
            var projectConfigPathExists = File.Exists(projectConfigPath);
            var disabledReason = TianShuProjectRootResolver.ResolveProjectConfigDisabledReason(
                layerDirectory: layerDirectory,
                config: nonProjectConfig,
                projectRootMarkers: projectRootMarkers,
                userConfigPath: normalizedUserConfigPath);
            var projectConfig = projectConfigPathExists
                ? KernelConfigObjectUtilities.ReadTomlConfigObject(projectConfigPath, suppressErrors: !string.IsNullOrWhiteSpace(disabledReason))
                : new Dictionary<string, object?>(StringComparer.Ordinal);
            orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
                new
                {
                    type = "project",
                    dotTianShuFolder = dotTianShuFolder,
                },
                projectConfig,
                projectConfigPathExists ? disabledReason : null));
        }

        if (sessionFlagsConfig.Count > 0)
        {
            orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
                new
                {
                    type = "sessionFlags",
                    source = "cli",
                },
                sessionFlagsConfig));
        }

        var managedConfigPath = TianShuConfigTomlPathResolver.ResolveLegacyManagedConfigTomlPath();
        var managedConfigPathExists = File.Exists(managedConfigPath);
        if (managedConfigPathExists)
        {
            var managedConfig = KernelConfigObjectUtilities.ReadTomlConfigObject(managedConfigPath);
            orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
                new
                {
                    type = "legacyManagedConfigTomlFromFile",
                    file = managedConfigPath,
                },
                managedConfig,
                LegacyManagedConfigMigrationOnlyDisabledReason));
        }

        var effectiveConfig = new Dictionary<string, object?>(StringComparer.Ordinal);
        var origins = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var layer in orderedLayers)
        {
            if (!string.IsNullOrWhiteSpace(layer.DisabledReason))
            {
                continue;
            }

            KernelConfigReadLayerUtilities.MergeConfigObjects(effectiveConfig, layer.Config);
            KernelConfigReadLayerUtilities.AssignConfigOrigins(layer.Config, null, new
            {
                name = layer.Name,
                version = layer.Version,
            }, origins);
        }

        TianShuPromptConfigUtilities.ApplyPromptConfigLayer(effectiveConfig, BuildPromptConfigLayers(orderedLayers), normalizedCwd);

        object? layers = null;
        if (includeLayers)
        {
            layers = orderedLayers
                .AsEnumerable()
                .Reverse()
                .Select(static layer => new
                {
                    name = layer.Name,
                    version = layer.Version,
                    config = layer.Config,
                    disabledReason = layer.DisabledReason,
                })
                .ToArray();
        }

        var hasPersistentConfig = File.Exists(systemConfigPath)
            || File.Exists(normalizedUserConfigPath)
            || orderedLayers.Any(static layer =>
            {
                var metadata = JsonSerializer.SerializeToElement(layer.Name);
                var type = ReadMetadataString(metadata, "type");
                return string.Equals(type, "userModelRouteSet", StringComparison.Ordinal)
                       || string.Equals(type, "userModelProtocolRuleSet", StringComparison.Ordinal)
                       || string.Equals(type, "userProviderInstance", StringComparison.Ordinal);
            })
            || projectLayerDirectories.Any(static layerDirectory => File.Exists(Path.Combine(layerDirectory, "tianshu.toml")));

        return new KernelConfigReadSnapshot(
            effectiveConfig,
            origins,
            layers,
            hasPersistentConfig,
            orderedLayers.ToArray());
    }

    public static KernelConfigReadSnapshot ApplyRequestConfigOverrides(
        KernelConfigReadSnapshot snapshot,
        JsonElement? requestOverrideElement)
    {
        if (!requestOverrideElement.HasValue)
        {
            return snapshot;
        }

        var requestConfig = KernelConfigReadLayerUtilities.BuildConfigObjectFromOverrideElement(requestOverrideElement.Value);
        if (requestConfig.Count == 0)
        {
            return snapshot;
        }

        var requestLayer = KernelConfigObjectUtilities.CreateConfigReadLayer(
            new
            {
                type = "sessionFlags",
                source = "request",
            },
            requestConfig);
        var mergedConfig = KernelConfigObjectUtilities.CloneConfigDictionary(snapshot.Config);
        KernelConfigReadLayerUtilities.MergeConfigObjects(mergedConfig, requestLayer.Config);
        var mergedOrigins = KernelConfigObjectUtilities.CloneConfigDictionary(snapshot.Origins);
        KernelConfigReadLayerUtilities.AssignConfigOrigins(requestLayer.Config, null, new
        {
            name = requestLayer.Name,
            version = requestLayer.Version,
        }, mergedOrigins);

        var orderedLayers = snapshot.OrderedLayers.ToList();
        orderedLayers.Add(requestLayer);
        return new KernelConfigReadSnapshot(
            mergedConfig,
            mergedOrigins,
            snapshot.Layers,
            snapshot.HasPersistentConfig,
            orderedLayers.ToArray());
    }

    private static IReadOnlyList<TianShuPromptConfigLayer> BuildPromptConfigLayers(IReadOnlyList<KernelConfigReadLayer> layers)
        => layers.Select(static layer =>
        {
            var metadata = JsonSerializer.SerializeToElement(layer.Name);
            var path = ReadMetadataString(metadata, "file");
            var dotTianShuFolder = ReadMetadataString(metadata, "dotTianShuFolder");
            var directoryPath = !string.IsNullOrWhiteSpace(path)
                ? Path.GetDirectoryName(path!)
                : !string.IsNullOrWhiteSpace(dotTianShuFolder)
                    ? dotTianShuFolder
                    : null;

            return new TianShuPromptConfigLayer(
                path,
                directoryPath,
                layer.Config,
                !string.IsNullOrWhiteSpace(layer.DisabledReason),
                string.IsNullOrWhiteSpace(path) || File.Exists(path!));
        }).ToArray();

    private static string? ReadMetadataString(JsonElement json, string propertyName)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static void AddUserModuleConfigLayers(
        List<KernelConfigReadLayer> orderedLayers,
        string normalizedUserConfigPath,
        string[] moduleSegments,
        string layerType)
    {
        foreach (var moduleConfigPath in EnumerateUserModuleConfigFiles(normalizedUserConfigPath, moduleSegments))
        {
            AddUserModuleConfigLayer(orderedLayers, moduleConfigPath, layerType);
        }
    }

    private static void AddUserProviderInstanceConfigLayers(
        List<KernelConfigReadLayer> orderedLayers,
        string normalizedUserConfigPath,
        Dictionary<string, object?> userConfig)
    {
        foreach (var moduleConfigPath in EnumerateUserProviderInstanceConfigFiles(normalizedUserConfigPath, userConfig))
        {
            AddUserModuleConfigLayer(orderedLayers, moduleConfigPath, "userProviderInstance");
        }
    }

    private static void AddUserModuleConfigLayer(
        List<KernelConfigReadLayer> orderedLayers,
        string moduleConfigPath,
        string layerType)
    {
        var moduleConfig = KernelConfigObjectUtilities.ReadTomlConfigObject(moduleConfigPath, suppressErrors: false);
        orderedLayers.Add(KernelConfigObjectUtilities.CreateConfigReadLayer(
            new
            {
                type = layerType,
                file = moduleConfigPath,
            },
            moduleConfig));
    }

    private static IEnumerable<string> EnumerateUserModuleConfigFiles(
        string normalizedUserConfigPath,
        string[] moduleSegments)
    {
        var moduleDirectory = TianShu.Configuration.TianShuHomePathUtilities.ResolveModulePathFromConfig(
            normalizedUserConfigPath,
            moduleSegments);
        if (!Directory.Exists(moduleDirectory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(moduleDirectory, "*.toml").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetFullPath(path);
        }
    }

    private static IEnumerable<string> EnumerateUserProviderInstanceConfigFiles(
        string normalizedUserConfigPath,
        Dictionary<string, object?> userConfig)
    {
        var moduleDirectory = TianShu.Configuration.TianShuHomePathUtilities.ResolveModulePathFromConfig(
            normalizedUserConfigPath,
            "model",
            "provider-instances");
        if (!Directory.Exists(moduleDirectory))
        {
            yield break;
        }

        var selectedProviderInstance = ReadStringExact(userConfig, "provider_instances");
        if (!string.IsNullOrWhiteSpace(selectedProviderInstance))
        {
            var selectedPath = Path.Combine(moduleDirectory, $"{selectedProviderInstance}.toml");
            if (File.Exists(selectedPath))
            {
                yield return Path.GetFullPath(selectedPath);
            }

            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(moduleDirectory, "*.toml").OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.GetFullPath(path);
        }
    }

    private static string? ReadStringExact(Dictionary<string, object?> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null,
        };
    }
}
