namespace TianShu.Contracts.Diagnostics;

/// <summary>
/// 诊断采集级别。
/// Diagnostic collection level.
/// </summary>
public enum DiagnosticCollectionLevel
{
    Off = 0,
    Summary = 1,
    Stats = 2,
    Artifact = 3,
    Verbose = 4,
}

/// <summary>
/// 标准诊断模块名。
/// Standard diagnostic module names.
/// </summary>
public static class DiagnosticModuleNames
{
    public const string Config = "config";
    public const string Thread = "thread";
    public const string Session = "session";
    public const string Context = "context";
    public const string Provider = "provider";
    public const string Tool = "tool";
    public const string Memory = "memory";
    public const string Governance = "governance";
    public const string Presentation = "presentation";
    public const string Recovery = "recovery";
    public const string Worker = "worker";
    public const string Diagnostics = "diagnostics";
}

/// <summary>
/// 单个模块的诊断采集选项。
/// Diagnostic collection options for a single module.
/// </summary>
public sealed record DiagnosticModuleCollectionOptions
{
    public DiagnosticCollectionLevel? Level { get; init; }

    public double? SampleRate { get; init; }

    public int? MaxItems { get; init; }
}

/// <summary>
/// 诊断 artifact 采集选项。
/// Diagnostic artifact collection options.
/// </summary>
public sealed record DiagnosticArtifactCollectionOptions
{
    public bool Enabled { get; init; }

    public long? MaxBytes { get; init; }
}

/// <summary>
/// 诊断遥测导出选项。
/// Diagnostic telemetry export options.
/// </summary>
public sealed record DiagnosticTelemetryOptions
{
    public bool Enabled { get; init; }

    public IReadOnlyList<string> Sinks { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 全局诊断采集选项。
/// Global diagnostic collection options.
/// </summary>
public sealed record DiagnosticCollectionOptions
{
    public static DiagnosticCollectionOptions Default { get; } = new();

    public bool Enabled { get; init; } = true;

    public DiagnosticCollectionLevel DefaultLevel { get; init; } = DiagnosticCollectionLevel.Stats;

    public IReadOnlyDictionary<string, DiagnosticModuleCollectionOptions> Modules { get; init; } =
        new Dictionary<string, DiagnosticModuleCollectionOptions>(StringComparer.OrdinalIgnoreCase);

    public DiagnosticArtifactCollectionOptions Artifacts { get; init; } = new();

    public DiagnosticTelemetryOptions Telemetry { get; init; } = new();
}

/// <summary>
/// 诊断采集决策。
/// Diagnostic collection decision.
/// </summary>
public sealed record DiagnosticCollectionDecision
{
    public required string ModuleName { get; init; }

    public required DiagnosticCollectionLevel EffectiveLevel { get; init; }

    public required DiagnosticCollectionLevel RequiredLevel { get; init; }

    public required bool ShouldCollect { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}
