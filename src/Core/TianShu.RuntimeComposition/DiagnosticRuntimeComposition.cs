using TianShu.AppHost.Tools.Runtime.Diagnostics;
using TianShu.Configuration;
using TianShu.Contracts.Diagnostics;
using TianShu.Diagnostics;

namespace TianShu.RuntimeComposition;

/// <summary>
/// 诊断与遥测 sink 的运行时组合入口。
/// Runtime composition entry point for diagnostics and telemetry sinks.
/// </summary>
internal static class DiagnosticRuntimeComposition
{
    /// <summary>
    /// 根据用户级 diagnostic sink manifest 创建运行时 sink 集。
    /// Creates runtime sink set from user-level diagnostic sink manifests.
    /// </summary>
    public static DiagnosticRuntimeSinkSet CreateDiagnosticSinks(
        string tianShuHome,
        IDiagnosticCollectionPolicy diagnosticPolicy,
        Func<string, string, string, string, string?, object, CancellationToken, Task> persistTurnLogAsync,
        Func<string, object, CancellationToken, Task> writeNotificationAsync,
        Func<DiagnosticEventEnvelope, string?> resolveModuleName,
        Func<DiagnosticEventEnvelope, DiagnosticCollectionLevel> resolveRequiredLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tianShuHome);
        ArgumentNullException.ThrowIfNull(diagnosticPolicy);
        ArgumentNullException.ThrowIfNull(persistTurnLogAsync);
        ArgumentNullException.ThrowIfNull(writeNotificationAsync);
        ArgumentNullException.ThrowIfNull(resolveModuleName);
        ArgumentNullException.ThrowIfNull(resolveRequiredLevel);

        var root = Path.GetFullPath(tianShuHome);
        var packages = new TianShuDiagnosticSinkManifestConfiguration().LoadEnabledPackages(root);
        var enabledSinks = packages
            .SelectMany(package => package.Sinks
                .Where(static sink => sink.Enabled)
                .Select(sink => (Package: package, Sink: sink)))
            .OrderBy(static item => item.Package.Priority)
            .ThenBy(static item => item.Sink.Priority)
            .ThenBy(static item => item.Sink.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var eventSinks = new List<IDiagnosticEventSink>();
        foreach (var item in enabledSinks)
        {
            if (IsTurnLogSink(item.Sink.Type))
            {
                eventSinks.Add(new TurnLogDiagnosticEventSink(persistTurnLogAsync, writeNotificationAsync));
            }
        }

        if (eventSinks.Count == 0)
        {
            eventSinks.Add(new TurnLogDiagnosticEventSink(persistTurnLogAsync, writeNotificationAsync));
        }

        var innerEventSink = eventSinks.Count == 1
            ? eventSinks[0]
            : new CompositeDiagnosticEventSink(eventSinks);
        var eventSink = new FilteringDiagnosticEventSink(
            innerEventSink,
            diagnosticPolicy,
            resolveModuleName,
            resolveRequiredLevel);

        var artifactSink = enabledSinks.FirstOrDefault(static item => IsArtifactFileSink(item.Sink.Type));
        IDiagnosticArtifactWriter artifactWriter = artifactSink.Sink is null
            ? new FileDiagnosticArtifactWriter(TianShuHomePathUtilities.ResolveDataPathFromHome(
                root,
                "artifacts",
                "diagnostics",
                "provider-requests"))
            : new FileDiagnosticArtifactWriter(TianShuDiagnosticSinkManifestConfiguration.ResolveSinkTargetFullPath(artifactSink.Package, artifactSink.Sink));
        artifactWriter = new FilteringDiagnosticArtifactWriter(artifactWriter, diagnosticPolicy);

        return new DiagnosticRuntimeSinkSet(
            eventSink,
            artifactWriter,
            enabledSinks.Select(static item => $"{item.Package.Id}/{item.Sink.Id}").ToArray(),
            packages.Count == 0 ? "fallback" : "manifest");
    }

    private static bool IsTurnLogSink(string? type)
        => string.Equals(type, "turn-log", StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, "turn_log", StringComparison.OrdinalIgnoreCase);

    private static bool IsArtifactFileSink(string? type)
        => string.Equals(type, "artifact-file", StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, "artifact_file", StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, "local-file", StringComparison.OrdinalIgnoreCase)
           || string.Equals(type, "file", StringComparison.OrdinalIgnoreCase);
}

internal sealed record DiagnosticRuntimeSinkSet(
    IDiagnosticEventSink EventSink,
    IDiagnosticArtifactWriter ProviderRequestPayloadArtifactWriter,
    IReadOnlyList<string> EnabledSinkIds,
    string SourceType);
