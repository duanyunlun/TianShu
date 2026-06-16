using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// 从 TianShu 配置对象读取 diagnostics 采集选项。
/// Reads diagnostics collection options from TianShu configuration objects.
/// </summary>
public static class DiagnosticCollectionOptionsReader
{
    public static DiagnosticCollectionOptions FromConfig(IReadOnlyDictionary<string, object?>? config)
    {
        if (config is null || !TryReadTable(config, "diagnostics", out var diagnostics))
        {
            return DiagnosticCollectionOptions.Default;
        }

        var enabled = ReadBool(diagnostics, "enabled") ?? DiagnosticCollectionOptions.Default.Enabled;
        var defaultLevel = ReadLevel(diagnostics, "default_level") ?? DiagnosticCollectionOptions.Default.DefaultLevel;
        var modules = ReadModules(diagnostics);
        var artifacts = ReadArtifacts(diagnostics);
        var telemetry = ReadTelemetry(diagnostics);

        return new DiagnosticCollectionOptions
        {
            Enabled = enabled,
            DefaultLevel = defaultLevel,
            Modules = modules,
            Artifacts = artifacts,
            Telemetry = telemetry,
        };
    }

    private static IReadOnlyDictionary<string, DiagnosticModuleCollectionOptions> ReadModules(IReadOnlyDictionary<string, object?> diagnostics)
    {
        if (!TryReadTable(diagnostics, "modules", out var modulesTable))
        {
            return DiagnosticCollectionOptions.Default.Modules;
        }

        var modules = new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in modulesTable)
        {
            if (value is not IReadOnlyDictionary<string, object?> moduleTable)
            {
                continue;
            }

            modules[name] = new DiagnosticModuleCollectionOptions
            {
                Level = ReadLevel(moduleTable, "level"),
                SampleRate = ReadDouble(moduleTable, "sample_rate"),
                MaxItems = ReadInt(moduleTable, "max_items"),
            };
        }

        return modules;
    }

    private static DiagnosticArtifactCollectionOptions ReadArtifacts(IReadOnlyDictionary<string, object?> diagnostics)
    {
        if (!TryReadTable(diagnostics, "artifacts", out var artifacts))
        {
            return DiagnosticCollectionOptions.Default.Artifacts;
        }

        return new DiagnosticArtifactCollectionOptions
        {
            Enabled = ReadBool(artifacts, "enabled") ?? DiagnosticCollectionOptions.Default.Artifacts.Enabled,
            MaxBytes = ReadLong(artifacts, "max_bytes"),
        };
    }

    private static DiagnosticTelemetryOptions ReadTelemetry(IReadOnlyDictionary<string, object?> diagnostics)
    {
        if (!TryReadTable(diagnostics, "telemetry", out var telemetry))
        {
            return DiagnosticCollectionOptions.Default.Telemetry;
        }

        return new DiagnosticTelemetryOptions
        {
            Enabled = ReadBool(telemetry, "enabled") ?? DiagnosticCollectionOptions.Default.Telemetry.Enabled,
            Sinks = ReadStringArray(telemetry, "sinks"),
        };
    }

    private static bool TryReadTable(IReadOnlyDictionary<string, object?> source, string key, out IReadOnlyDictionary<string, object?> table)
    {
        if (source.TryGetValue(key, out var value) && value is IReadOnlyDictionary<string, object?> dictionary)
        {
            table = dictionary;
            return true;
        }

        table = new Dictionary<string, object?>(StringComparer.Ordinal);
        return false;
    }

    private static DiagnosticCollectionLevel? ReadLevel(IReadOnlyDictionary<string, object?> source, string key)
        => source.TryGetValue(key, out var value)
           && value is string text
           && Enum.TryParse<DiagnosticCollectionLevel>(text.Replace("-", string.Empty), ignoreCase: true, out var level)
            ? level
            : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> source, string key)
        => source.TryGetValue(key, out var value) switch
        {
            true when value is bool boolean => boolean,
            true when value is string text && bool.TryParse(text, out var boolean) => boolean,
            _ => null,
        };

    private static int? ReadInt(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            int integer => integer,
            long integer when integer is >= int.MinValue and <= int.MaxValue => (int)integer,
            string text when int.TryParse(text, out var integer) => integer,
            _ => null,
        };
    }

    private static long? ReadLong(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            long integer => integer,
            int integer => integer,
            string text when long.TryParse(text, out var integer) => integer,
            _ => null,
        };
    }

    private static double? ReadDouble(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            double number => ClampSampleRate(number),
            float number => ClampSampleRate(number),
            decimal number => ClampSampleRate((double)number),
            string text when double.TryParse(text, out var number) => ClampSampleRate(number),
            _ => null,
        };
    }

    private static IReadOnlyList<string> ReadStringArray(IReadOnlyDictionary<string, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is string)
        {
            return Array.Empty<string>();
        }

        if (value is not System.Collections.IEnumerable enumerable)
        {
            return Array.Empty<string>();
        }

        return enumerable
            .OfType<object?>()
            .Select(static item => item?.ToString())
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .ToArray();
    }

    private static double ClampSampleRate(double value)
        => double.IsFinite(value) ? Math.Clamp(value, 0.0d, 1.0d) : 1.0d;
}
