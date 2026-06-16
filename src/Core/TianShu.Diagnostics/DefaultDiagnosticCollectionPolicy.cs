using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TianShu.Contracts.Diagnostics;
using TianShu.Contracts.Primitives;

namespace TianShu.Diagnostics;

/// <summary>
/// 默认诊断采集策略，支持模块级别、artifact 开关与 deterministic sampling。
/// Default diagnostic collection policy with module levels, artifact toggles, and deterministic sampling.
/// </summary>
public sealed class DefaultDiagnosticCollectionPolicy : IDiagnosticCollectionPolicy
{
    private readonly Func<DiagnosticCollectionOptions> resolveOptions;
    private readonly ConcurrentDictionary<string, ModuleItemCounter> itemCounters = new(StringComparer.OrdinalIgnoreCase);

    public DefaultDiagnosticCollectionPolicy(Func<DiagnosticCollectionOptions>? resolveOptions = null)
    {
        this.resolveOptions = resolveOptions ?? (() => DiagnosticCollectionOptions.Default);
    }

    public DiagnosticCollectionDecision ShouldCollect(
        string eventName,
        string? moduleName,
        DiagnosticCollectionLevel requiredLevel,
        DiagnosticOperationContext? operation,
        MetadataBag metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        var options = resolveOptions();
        var effectiveModule = NormalizeModule(moduleName) ?? InferModuleName(eventName);
        var moduleOptions = options.Modules.TryGetValue(effectiveModule, out var configuredModule)
            ? configuredModule
            : null;
        var effectiveLevel = moduleOptions?.Level ?? options.DefaultLevel;
        var sampleRate = moduleOptions?.SampleRate ?? 1.0d;

        if (!options.Enabled)
        {
            return Decision(effectiveModule, effectiveLevel, requiredLevel, false, "diagnostics_disabled");
        }

        if (effectiveLevel < requiredLevel || effectiveLevel == DiagnosticCollectionLevel.Off)
        {
            return Decision(effectiveModule, effectiveLevel, requiredLevel, false, "diagnostics_level_below_required");
        }

        if (IsFailureOrDegraded(metadata) || sampleRate >= 1.0d)
        {
            return CollectOrLimit(effectiveModule, effectiveLevel, requiredLevel, moduleOptions?.MaxItems);
        }

        if (sampleRate <= 0.0d)
        {
            return Decision(effectiveModule, effectiveLevel, requiredLevel, false, "diagnostics_sampled_out");
        }

        var sampled = DeterministicSample(eventName, operation, sampleRate);
        return sampled
            ? CollectOrLimit(effectiveModule, effectiveLevel, requiredLevel, moduleOptions?.MaxItems)
            : Decision(effectiveModule, effectiveLevel, requiredLevel, false, "diagnostics_sampled_out");
    }

    public bool ShouldWriteArtifact(string artifactKind, DiagnosticOperationContext? operation, MetadataBag metadata)
        => ShouldWriteArtifact(artifactKind, contentBytes: 0, operation, metadata);

    public bool ShouldWriteArtifact(
        string artifactKind,
        long contentBytes,
        DiagnosticOperationContext? operation,
        MetadataBag metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKind);
        var options = resolveOptions();
        if (!options.Enabled || !options.Artifacts.Enabled)
        {
            return false;
        }

        if (options.Artifacts.MaxBytes is { } maxBytes
            && maxBytes >= 0
            && contentBytes > maxBytes)
        {
            return false;
        }

        var moduleName = metadata.TryGetValue("diagnosticModule", out var module)
                         && !string.IsNullOrWhiteSpace(module.StringValue)
            ? module.StringValue
            : InferModuleName(artifactKind);
        var decision = ShouldCollect(
            "diagnostics/artifact/written",
            moduleName,
            DiagnosticCollectionLevel.Artifact,
            operation,
            metadata);
        return decision.ShouldCollect;
    }

    public static string InferModuleName(string eventName)
    {
        if (eventName.StartsWith("turn/context_slicing/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Context;
        }

        if (eventName.StartsWith("turn/provider_", StringComparison.OrdinalIgnoreCase)
            || eventName.StartsWith("provider/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Provider;
        }

        if (eventName.StartsWith("governance/", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("approval", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("permissions", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("requestUserInput", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Governance;
        }

        if (eventName.StartsWith("turn/tool", StringComparison.OrdinalIgnoreCase)
            || eventName.StartsWith("tool/", StringComparison.OrdinalIgnoreCase)
            || eventName.StartsWith("item/tool", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("commandExecution", StringComparison.OrdinalIgnoreCase)
            || eventName.Contains("fileChange", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Tool;
        }

        if (eventName.StartsWith("memory/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Memory;
        }

        if (eventName.StartsWith("recovery/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Recovery;
        }

        if (eventName.StartsWith("worker/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Worker;
        }

        if (eventName.StartsWith("diagnostics/", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticModuleNames.Diagnostics;
        }

        return DiagnosticModuleNames.Presentation;
    }

    private static DiagnosticCollectionDecision Decision(
        string moduleName,
        DiagnosticCollectionLevel effectiveLevel,
        DiagnosticCollectionLevel requiredLevel,
        bool shouldCollect,
        string reasonCode)
        => new()
        {
            ModuleName = moduleName,
            EffectiveLevel = effectiveLevel,
            RequiredLevel = requiredLevel,
            ShouldCollect = shouldCollect,
            ReasonCodes = [reasonCode],
        };

    private DiagnosticCollectionDecision CollectOrLimit(
        string moduleName,
        DiagnosticCollectionLevel effectiveLevel,
        DiagnosticCollectionLevel requiredLevel,
        int? maxItems)
        => TryReserveModuleItem(moduleName, maxItems)
            ? Decision(moduleName, effectiveLevel, requiredLevel, true, "diagnostics_collected")
            : Decision(moduleName, effectiveLevel, requiredLevel, false, "diagnostics_max_items_exceeded");

    private bool TryReserveModuleItem(string moduleName, int? maxItems)
    {
        if (maxItems is null or < 0)
        {
            itemCounters.TryRemove(moduleName, out _);
            return true;
        }

        var counter = itemCounters.GetOrAdd(moduleName, static _ => new ModuleItemCounter());
        lock (counter)
        {
            if (counter.MaxItems != maxItems.Value)
            {
                counter.MaxItems = maxItems.Value;
                counter.Count = 0;
            }

            if (counter.Count >= maxItems.Value)
            {
                return false;
            }

            counter.Count++;
            return true;
        }
    }

    private static string? NormalizeModule(string? moduleName)
        => string.IsNullOrWhiteSpace(moduleName) ? null : moduleName.Trim().ToLowerInvariant();

    private static bool IsFailureOrDegraded(MetadataBag metadata)
    {
        if (metadata.TryGetValue("status", out var status)
            && !string.Equals(status.StringValue, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status.StringValue, "info", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (metadata.TryGetValue("failureKind", out var failureKind)
            && !string.IsNullOrWhiteSpace(failureKind.StringValue))
        {
            return true;
        }

        return metadata.TryGetValue("degraded", out var degraded)
               && string.Equals(degraded.StringValue, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DeterministicSample(string eventName, DiagnosticOperationContext? operation, double sampleRate)
    {
        var key = string.Join(
            "|",
            eventName,
            operation?.TraceId,
            operation?.ThreadId,
            operation?.TurnId,
            operation?.RequestSequence?.ToString(),
            operation?.OperationId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var bucket = BitConverter.ToUInt32(hash, 0) / (double)uint.MaxValue;
        return bucket <= sampleRate;
    }

    private sealed class ModuleItemCounter
    {
        public int? MaxItems { get; set; }

        public int Count { get; set; }
    }
}
