using System.Globalization;
using System.Text;
using System.Text.Json;
using TianShu.Contracts.Configuration;
using TianShu.Contracts.Primitives;
using Tomlyn;
using Tomlyn.Model;

namespace TianShu.Configuration;

/// <summary>
/// 对 tianshu.toml 执行 typed change set 的预览与应用。
/// Previews and applies typed change sets to tianshu.toml.
/// </summary>
public sealed class TianShuConfigurationTomlChangeApplier
{
    private readonly TianShuConfigurationProjectionBuilder builder;

    public TianShuConfigurationTomlChangeApplier(TianShuConfigurationProjectionBuilder? builder = null)
    {
        this.builder = builder ?? new TianShuConfigurationProjectionBuilder();
    }

    public ConfigurationChangePreview Preview(string path, ConfigurationChangeSet changeSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(changeSet);

        var beforeRoot = ReadRootOrEmpty(path, out var readIssues);
        var afterRoot = CloneRoot(beforeRoot);
        var issues = new List<ConfigurationIssue>(readIssues);
        issues.AddRange(ApplyChangesToRoot(afterRoot, changeSet));

        return new ConfigurationChangePreview
        {
            Before = BuildProjection(path, beforeRoot),
            After = BuildProjection(path, afterRoot),
            Issues = issues,
        };
    }

    public ConfigurationApplyResult Apply(string path, ConfigurationChangeSet changeSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(changeSet);

        var fullPath = Path.GetFullPath(path);
        var root = ReadRootOrEmpty(fullPath, out var readIssues);
        var issues = new List<ConfigurationIssue>(readIssues);
        issues.AddRange(ApplyChangesToRoot(root, changeSet));

        if (issues.Any(static issue => issue.Severity == ConfigurationIssueSeverity.Error))
        {
            return new ConfigurationApplyResult
            {
                Applied = false,
                TargetPath = fullPath,
                Projection = BuildProjection(fullPath, root),
                Issues = issues,
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, TomlTableWriter.Write(root).TrimEnd() + Environment.NewLine);

        return new ConfigurationApplyResult
        {
            Applied = true,
            TargetPath = fullPath,
            Projection = BuildProjection(fullPath, root),
            Issues = issues,
        };
    }

    public ConfigurationApplyResult ApplyRouted(string userConfigPath, ConfigurationChangeSet changeSet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userConfigPath);
        ArgumentNullException.ThrowIfNull(changeSet);

        var fullUserConfigPath = Path.GetFullPath(userConfigPath);
        var groupedChanges = new Dictionary<string, List<ConfigurationChange>>(GetPathComparer());
        foreach (var change in changeSet.Changes)
        {
            var targetPath = TianShuKnownModuleConfigurationPaths.TryResolveWriteTargetPath(fullUserConfigPath, change.Key, out var modulePath)
                ? modulePath
                : fullUserConfigPath;
            var normalizedTargetPath = Path.GetFullPath(targetPath);
            if (!groupedChanges.TryGetValue(normalizedTargetPath, out var changes))
            {
                changes = [];
                groupedChanges[normalizedTargetPath] = changes;
            }

            changes.Add(change);
        }

        var issues = new List<ConfigurationIssue>();
        var targetPaths = new List<string>();
        foreach (var pair in groupedChanges.OrderBy(static pair => pair.Key, GetPathComparer()))
        {
            var result = Apply(pair.Key, new ConfigurationChangeSet
            {
                Changes = pair.Value,
            });
            targetPaths.Add(pair.Key);
            issues.AddRange(result.Issues);
            if (!result.Applied)
            {
                return new ConfigurationApplyResult
                {
                    Applied = false,
                    TargetPath = pair.Key,
                    Projection = new TianShuConfigurationTomlProjectionLoader(builder).LoadUserFileWithModules(fullUserConfigPath),
                    Issues = issues,
                };
            }
        }

        return new ConfigurationApplyResult
        {
            Applied = true,
            TargetPath = targetPaths.Count == 1 ? targetPaths[0] : fullUserConfigPath,
            Projection = new TianShuConfigurationTomlProjectionLoader(builder).LoadUserFileWithModules(fullUserConfigPath),
            Issues = issues,
        };
    }

    public static StructuredValue ParseUserInput(string rawValue, ConfigurationValueKind valueKind)
        => valueKind switch
        {
            ConfigurationValueKind.Boolean => StructuredValue.FromBoolean(bool.Parse(rawValue.Trim())),
            ConfigurationValueKind.Integer => StructuredValue.FromNumber(long.Parse(rawValue.Trim(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            ConfigurationValueKind.Number => StructuredValue.FromNumber(decimal.Parse(rawValue.Trim(), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
            ConfigurationValueKind.Array => ParseJsonStructuredValue(rawValue, JsonValueKind.Array),
            ConfigurationValueKind.Object => ParseJsonStructuredValue(rawValue, JsonValueKind.Object),
            _ => StructuredValue.FromString(rawValue),
        };

    private static StructuredValue ParseJsonStructuredValue(string rawValue, JsonValueKind expectedKind)
    {
        using var document = JsonDocument.Parse(rawValue);
        if (document.RootElement.ValueKind != expectedKind)
        {
            throw new FormatException($"配置值必须是 JSON {expectedKind}。");
        }

        return StructuredValue.FromJsonElement(document.RootElement);
    }

    private ConfigurationProjection BuildProjection(string path, TomlTable root)
    {
        var values = new Dictionary<string, StructuredValue>(StringComparer.OrdinalIgnoreCase);
        FlattenTable(root, prefix: null, values);
        return builder.Build(new ConfigurationProjectionRequest
        {
            Layers =
            [
                new ConfigurationLayerSnapshot
                {
                    Source = new ConfigurationSourceLayer
                    {
                        Id = "user",
                        Kind = ConfigurationSourceKind.User,
                        DisplayName = "用户配置",
                        Path = Path.GetFullPath(path),
                        Order = 20,
                        Exists = File.Exists(path),
                        IsWritable = true,
                    },
                    Values = values,
                },
            ],
        });
    }

    private static TomlTable ReadRootOrEmpty(string path, out IReadOnlyList<ConfigurationIssue> issues)
    {
        issues = Array.Empty<ConfigurationIssue>();
        if (!File.Exists(path))
        {
            return new TomlTable();
        }

        try
        {
            return TomlTable.From(Toml.Parse(File.ReadAllText(path), path));
        }
        catch (Exception ex)
        {
            issues =
            [
                new ConfigurationIssue
                {
                    Severity = ConfigurationIssueSeverity.Error,
                    Code = "config.toml.parse_failed",
                    Message = $"无法解析配置文件：{ex.Message}",
                },
            ];
            return new TomlTable();
        }
    }

    private static TomlTable CloneRoot(TomlTable root)
        => CloneTomlTable(root);

    private static IReadOnlyList<ConfigurationIssue> ApplyChangesToRoot(TomlTable root, ConfigurationChangeSet changeSet)
    {
        var issues = new List<ConfigurationIssue>();
        foreach (var change in changeSet.Changes)
        {
            if (string.IsNullOrWhiteSpace(change.Key))
            {
                issues.Add(new ConfigurationIssue
                {
                    Severity = ConfigurationIssueSeverity.Error,
                    Code = "config.change.key_missing",
                    Message = "配置变更缺少 key。",
                });
                continue;
            }

            var segments = change.Key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            switch (change.Operation)
            {
                case ConfigurationChangeOperation.Set:
                    SetPathValue(root, segments, ToTomlValue(change.Value));
                    break;
                case ConfigurationChangeOperation.Unset:
                case ConfigurationChangeOperation.ResetToDefault:
                    RemovePathValue(root, segments);
                    break;
                default:
                    issues.Add(new ConfigurationIssue
                    {
                        Severity = ConfigurationIssueSeverity.Error,
                        Code = "config.change.operation_unknown",
                        Message = $"不支持的配置变更操作：{change.Operation}",
                        FieldKey = change.Key,
                    });
                    break;
            }
        }

        return issues;
    }

    private static void SetPathValue(TomlTable root, IReadOnlyList<string> segments, object value)
    {
        var current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (current.TryGetValue(segments[index], out var existing) && existing is TomlTable existingTable)
            {
                current = existingTable;
                continue;
            }

            var created = new TomlTable();
            current[segments[index]] = created;
            current = created;
        }

        current[segments[^1]] = value;
    }

    private static void RemovePathValue(TomlTable root, IReadOnlyList<string> segments)
    {
        var stack = new Stack<(TomlTable Table, string Key)>();
        var current = root;
        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (!current.TryGetValue(segments[index], out var next) || next is not TomlTable nextTable)
            {
                return;
            }

            stack.Push((current, segments[index]));
            current = nextTable;
        }

        current.Remove(segments[^1]);
        while (stack.Count > 0 && current.Count == 0)
        {
            var parent = stack.Pop();
            parent.Table.Remove(parent.Key);
            current = parent.Table;
        }
    }

    private static object ToTomlValue(StructuredValue? value)
        => value?.Kind switch
        {
            null or StructuredValueKind.Null => string.Empty,
            StructuredValueKind.String => value.StringValue ?? string.Empty,
            StructuredValueKind.Number => ParseNumber(value.NumberValue),
            StructuredValueKind.Boolean => value.BooleanValue ?? false,
            StructuredValueKind.Array => ToTomlArray(value.Items),
            StructuredValueKind.Object => ToTomlTable(value.Properties),
            _ => value.ToPlainObject() ?? string.Empty,
        };

    private static object ParseNumber(string? value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
            ? integer
            : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : value ?? "0";

    private static TomlArray ToTomlArray(IReadOnlyList<StructuredValue> values)
    {
        var array = new TomlArray();
        foreach (var value in values)
        {
            array.Add(ToTomlValue(value));
        }

        return array;
    }

    private static TomlTable ToTomlTable(IReadOnlyDictionary<string, StructuredValue> values)
    {
        var table = new TomlTable();
        foreach (var pair in values)
        {
            table[pair.Key] = ToTomlValue(pair.Value);
        }

        return table;
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

            values[key] = StructuredValue.FromPlainObject(pair.Value);
        }
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static object? CloneTomlValue(object? value)
        => value switch
        {
            TomlTable table => CloneTomlTable(table),
            TomlArray array => CloneTomlArray(array),
            TomlTableArray tableArray => CloneTomlTableArray(tableArray),
            _ => value,
        };

    private static TomlTable CloneTomlTable(TomlTable table)
    {
        var clone = new TomlTable();
        foreach (var pair in table)
        {
            clone[pair.Key] = CloneTomlValue(pair.Value) ?? string.Empty;
        }

        return clone;
    }

    private static TomlArray CloneTomlArray(TomlArray array)
    {
        var clone = new TomlArray();
        foreach (var item in array)
        {
            clone.Add(CloneTomlValue(item));
        }

        return clone;
    }

    private static TomlTableArray CloneTomlTableArray(TomlTableArray tableArray)
    {
        var clone = new TomlTableArray();
        foreach (var table in tableArray)
        {
            clone.Add(CloneTomlTable(table));
        }

        return clone;
    }

    private static class TomlTableWriter
    {
        public static string Write(TomlTable root)
        {
            var builder = new StringBuilder();
            WriteTable(builder, root, prefix: null);
            return builder.ToString();
        }

        private static void WriteTable(StringBuilder builder, TomlTable table, string? prefix)
        {
            var scalarRows = table.Where(static pair => pair.Value is not TomlTable and not TomlTableArray).ToArray();
            foreach (var pair in scalarRows)
            {
                builder.Append(FormatKeySegment(pair.Key));
                builder.Append(" = ");
                builder.AppendLine(FormatValue(pair.Value));
            }

            foreach (var pair in table.Where(static pair => pair.Value is TomlTable))
            {
                if (builder.Length > 0 && builder[^1] != '\n')
                {
                    builder.AppendLine();
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                var tablePrefix = JoinKey(prefix, pair.Key);
                builder.Append('[');
                builder.Append(tablePrefix);
                builder.AppendLine("]");
                WriteTable(builder, (TomlTable)pair.Value!, tablePrefix);
            }

            foreach (var pair in table.Where(static pair => pair.Value is TomlTableArray))
            {
                var tablePrefix = JoinKey(prefix, pair.Key);
                foreach (var child in (TomlTableArray)pair.Value!)
                {
                    if (builder.Length > 0)
                    {
                        builder.AppendLine();
                    }

                    builder.Append("[[");
                    builder.Append(tablePrefix);
                    builder.AppendLine("]]");
                    WriteTable(builder, child, tablePrefix);
                }
            }
        }

        private static string JoinKey(string? prefix, string key)
            => string.IsNullOrWhiteSpace(prefix)
                ? FormatKeySegment(key)
                : $"{prefix}.{FormatKeySegment(key)}";

        private static string FormatKeySegment(string key)
        {
            if (key.Length > 0 && key.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'))
            {
                return key;
            }

            return FormatString(key);
        }

        private static string FormatValue(object? value)
            => value switch
            {
                null => "\"\"",
                string text => FormatString(text),
                bool boolean => boolean ? "true" : "false",
                byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
                TomlArray array => FormatArray(array),
                TomlTable table => FormatInlineTable(table),
                TomlTableArray => "[]",
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
                _ => FormatString(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty),
            };

        private static string FormatArray(TomlArray array)
            => $"[{string.Join(", ", array.Select(FormatValue))}]";

        private static string FormatInlineTable(TomlTable table)
            => $"{{ {string.Join(", ", table.Select(pair => $"{FormatKeySegment(pair.Key)} = {FormatValue(pair.Value)}"))} }}";

        private static string FormatString(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (var ch in value)
            {
                builder.Append(ch switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\b' => "\\b",
                    '\f' => "\\f",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => ch,
                });
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
