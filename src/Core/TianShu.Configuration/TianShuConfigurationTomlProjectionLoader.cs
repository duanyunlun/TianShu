using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 从 TOML 文件读取只读配置投影。
/// Loads read-only configuration projections from TOML files.
/// </summary>
public sealed class TianShuConfigurationTomlProjectionLoader
{
    public const string EnvironmentConfigurationPrefix = "TIANSHU_CONFIG__";

    private readonly TianShuConfigurationProjectionBuilder builder;

    public TianShuConfigurationTomlProjectionLoader(TianShuConfigurationProjectionBuilder? builder = null)
    {
        this.builder = builder ?? new TianShuConfigurationProjectionBuilder();
    }

    public ConfigurationProjection LoadFile(string path, ConfigurationSourceKind sourceKind = ConfigurationSourceKind.User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var source = new ConfigurationSourceLayer
        {
            Id = sourceKind.ToString().ToLowerInvariant(),
            Kind = sourceKind,
            DisplayName = GetSourceDisplayName(sourceKind),
            Path = fullPath,
            Order = GetSourceOrder(sourceKind),
            Exists = File.Exists(fullPath),
            IsWritable = sourceKind is ConfigurationSourceKind.User or ConfigurationSourceKind.Project or ConfigurationSourceKind.WorkingDirectory,
        };

        var values = File.Exists(fullPath)
            ? ReadFlattenedToml(fullPath)
            : new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);

        return builder.Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                new ConfigurationLayerSnapshot
                {
                    Source = source,
                    Values = values,
                },
            ],
        });
    }

    /// <summary>
    /// 读取配置文件，并把显式 `TIANSHU_CONFIG__` 环境变量作为最高优先级 source layer。
    /// Loads a configuration file and overlays explicit `TIANSHU_CONFIG__` environment variables as a source layer.
    /// </summary>
    public ConfigurationProjection LoadFileWithEnvironmentLayer(
        string path,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        ConfigurationSourceKind sourceKind = ConfigurationSourceKind.User)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var layers = new List<ConfigurationLayerSnapshot>
        {
            BuildFileLayer(fullPath, sourceKind),
        };
        var environmentValues = ReadExplicitEnvironmentValues(environmentVariables ?? ReadProcessEnvironment());
        if (environmentValues.Count > 0)
        {
            layers.Add(new ConfigurationLayerSnapshot
            {
                Source = new ConfigurationSourceLayer
                {
                    Id = "environment",
                    Kind = ConfigurationSourceKind.Environment,
                    DisplayName = GetSourceDisplayName(ConfigurationSourceKind.Environment),
                    Order = GetSourceOrder(ConfigurationSourceKind.Environment),
                    Exists = true,
                    IsWritable = false,
                },
                Values = environmentValues,
            });
        }

        return builder.Build(new ConfigurationProjectionRequest
        {
            Layers = layers,
        });
    }

    /// <summary>
    /// 读取用户主配置，并把用户级模块文件纳入投影。
    /// Loads the user root configuration with user-level module files.
    /// </summary>
    public ConfigurationProjection LoadUserFileWithModules(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var layers = new List<ConfigurationLayerSnapshot>();
        AddUserModuleDefaultLayers(layers, fullPath);

        var rootValues = File.Exists(fullPath)
            ? ReadFlattenedToml(fullPath)
            : new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);

        layers.Add(new ConfigurationLayerSnapshot
        {
            Source = new ConfigurationSourceLayer
            {
                Id = "user",
                Kind = ConfigurationSourceKind.User,
                DisplayName = GetSourceDisplayName(ConfigurationSourceKind.User),
                Path = fullPath,
                Order = GetSourceOrder(ConfigurationSourceKind.User),
                Exists = File.Exists(fullPath),
                IsWritable = true,
            },
            Values = rootValues,
        });

        foreach (var providerInstancePath in TianShuKnownModuleConfigurationPaths.ResolveProviderInstanceModulePaths(fullPath, rootValues))
        {
            var providerInstanceFullPath = Path.GetFullPath(providerInstancePath);
            layers.Add(new ConfigurationLayerSnapshot
            {
                Source = new ConfigurationSourceLayer
                {
                    Id = $"user-provider-instances:{Path.GetFileNameWithoutExtension(providerInstanceFullPath)}",
                    Kind = ConfigurationSourceKind.User,
                    DisplayName = "用户 Provider 实例模块",
                    Path = providerInstanceFullPath,
                    Order = GetSourceOrder(ConfigurationSourceKind.User) + 1,
                    Exists = true,
                    IsWritable = true,
                },
                Values = ReadFlattenedToml(providerInstanceFullPath),
            });
        }

        return builder.Build(new ConfigurationProjectionRequest
        {
            Layers = layers,
        });
    }

    /// <summary>
    /// 读取用户主配置，并把用户级模型路由方案模块文件纳入投影。
    /// Loads the user root configuration with user-level model route set module files.
    /// </summary>
    public ConfigurationProjection LoadUserFileWithModelRouteSetModules(string path)
        => LoadUserFileWithModules(path);

    private static void AddUserModuleDefaultLayers(List<ConfigurationLayerSnapshot> layers, string fullPath)
    {
        foreach (var modulePath in TianShuKnownModuleConfigurationPaths.EnumerateDefaultLayerModuleFiles(fullPath))
        {
            var moduleFullPath = Path.GetFullPath(modulePath);
            layers.Add(new ConfigurationLayerSnapshot
            {
                Source = new ConfigurationSourceLayer
                {
                    Id = BuildUserModuleSourceId(fullPath, moduleFullPath),
                    Kind = ConfigurationSourceKind.User,
                    DisplayName = "用户模块配置",
                    Path = moduleFullPath,
                    Order = GetSourceOrder(ConfigurationSourceKind.User) - 1,
                    Exists = true,
                    IsWritable = true,
                },
                Values = ReadFlattenedToml(moduleFullPath),
            });
        }
    }

    private static string BuildUserModuleSourceId(string configPath, string modulePath)
    {
        var homePath = Path.GetDirectoryName(Path.GetFullPath(configPath));
        var relativePath = !string.IsNullOrWhiteSpace(homePath)
            ? Path.GetRelativePath(homePath!, modulePath)
            : Path.GetFileName(modulePath);
        var normalized = relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        return $"user-module:{normalized}";
    }

    public static IReadOnlyDictionary<string, StructuredValue> ReadFlattenedToml(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var text = File.ReadAllText(path);
        var table = TomlTable.From(Toml.Parse(text, path));

        var values = new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);
        FlattenTable(table, prefix: null, values);
        return values;
    }

    public static IReadOnlyDictionary<string, StructuredValue> ReadExplicitEnvironmentValues(
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        ArgumentNullException.ThrowIfNull(environmentVariables);

        var values = new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in environmentVariables)
        {
            if (!pair.Key.StartsWith(EnvironmentConfigurationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = pair.Key[EnvironmentConfigurationPrefix.Length..]
                .Trim('_')
                .Replace("__", ".", StringComparison.Ordinal)
                .ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            values[key] = ParseEnvironmentValue(pair.Value);
        }

        return values;
    }

    private static void FlattenTable(TomlTable table, string? prefix, Dictionary<string, StructuredValue> values)
    {
        foreach (var pair in table)
        {
            var key = string.IsNullOrWhiteSpace(prefix) ? pair.Key : $"{prefix}.{pair.Key}";
            if (pair.Value is TomlTable child)
            {
                FlattenTable(child, key, values);
                continue;
            }

            values[key] = ToStructuredValue(pair.Value);
        }
    }

    private static StructuredValue ToStructuredValue(object? value)
        => value switch
        {
            null => StructuredValue.Null,
            TomlArray array => StructuredValue.FromArray(array.Select(ToStructuredValue).ToArray()),
            TomlTable table => StructuredValue.FromObject(table.ToDictionary(static pair => pair.Key, static pair => ToStructuredValue(pair.Value), StringComparer.Ordinal)),
            _ => StructuredValue.FromPlainObject(value),
        };

    private static ConfigurationLayerSnapshot BuildFileLayer(string fullPath, ConfigurationSourceKind sourceKind)
        => new()
        {
            Source = new ConfigurationSourceLayer
            {
                Id = sourceKind.ToString().ToLowerInvariant(),
                Kind = sourceKind,
                DisplayName = GetSourceDisplayName(sourceKind),
                Path = fullPath,
                Order = GetSourceOrder(sourceKind),
                Exists = File.Exists(fullPath),
                IsWritable = sourceKind is ConfigurationSourceKind.User or ConfigurationSourceKind.Project or ConfigurationSourceKind.WorkingDirectory,
            },
            Values = File.Exists(fullPath)
                ? ReadFlattenedToml(fullPath)
                : new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase),
        };

    private static IReadOnlyDictionary<string, string> ReadProcessEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static StructuredValue ParseEnvironmentValue(string? value)
    {
        var text = value ?? string.Empty;
        if (bool.TryParse(text, out var booleanValue))
        {
            return StructuredValue.FromBoolean(booleanValue);
        }

        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)
            || decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return StructuredValue.FromNumber(text);
        }

        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            var items = text[1..^1]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(static item => StructuredValue.FromString(item.Trim('"')))
                .ToArray();
            return StructuredValue.FromArray(items);
        }

        return StructuredValue.FromString(text);
    }

    private static string GetSourceDisplayName(ConfigurationSourceKind kind)
        => kind switch
        {
            ConfigurationSourceKind.System => "系统配置",
            ConfigurationSourceKind.User => "用户配置",
            ConfigurationSourceKind.Project => "项目配置",
            ConfigurationSourceKind.WorkingDirectory => "当前目录配置",
            ConfigurationSourceKind.CommandLine => "命令行覆盖",
            ConfigurationSourceKind.Environment => "环境变量覆盖",
            ConfigurationSourceKind.Imported => "外部导入",
            ConfigurationSourceKind.Session => "会话快照",
            _ => "内置默认",
        };

    private static int GetSourceOrder(ConfigurationSourceKind kind)
        => kind switch
        {
            ConfigurationSourceKind.BuiltIn => 0,
            ConfigurationSourceKind.System => 10,
            ConfigurationSourceKind.User => 20,
            ConfigurationSourceKind.Project => 30,
            ConfigurationSourceKind.WorkingDirectory => 40,
            ConfigurationSourceKind.Session => 50,
            ConfigurationSourceKind.CommandLine => 60,
            ConfigurationSourceKind.Environment => 70,
            ConfigurationSourceKind.Imported => 80,
            _ => 100,
        };
}
