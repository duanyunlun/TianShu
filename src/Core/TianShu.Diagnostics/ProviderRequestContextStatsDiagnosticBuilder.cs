using System.Text.Json;
using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// Provider 请求上下文统计的固定 diagnostics builder 适配器。
/// Fixed diagnostics builder adapter for provider request context statistics.
/// </summary>
public sealed class ProviderRequestContextStatsDiagnosticBuilder(
    ProviderRequestContextStatsDiagnosticBuilderOptions options,
    JsonSerializerOptions? jsonOptions = null) : IDiagnosticStatsBuilder<IReadOnlyDictionary<string, object?>, ProviderRequestContextStats>
{
    private readonly JsonSerializerOptions jsonOptions = jsonOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public ProviderRequestContextStats Build(IReadOnlyDictionary<string, object?> input, DiagnosticOperationContext context)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.ThreadId))
        {
            throw new InvalidOperationException("Provider request diagnostics require a thread id.");
        }

        if (string.IsNullOrWhiteSpace(context.TurnId))
        {
            throw new InvalidOperationException("Provider request diagnostics require a turn id.");
        }

        return ProviderRequestContextStatsBuilder.Build(
            input,
            new ProviderRequestContextStatsBuildOptions
            {
                ThreadId = context.ThreadId,
                TurnId = context.TurnId,
                RequestSequence = context.RequestSequence ?? options.RequestSequenceFallback,
                Model = options.Model,
                Provider = options.Provider,
                Transport = options.Transport,
                InputPropertyName = options.InputPropertyName,
                PayloadArtifact = options.PayloadArtifact,
            },
            jsonOptions);
    }
}

/// <summary>
/// Provider 请求上下文统计 diagnostics builder 选项。
/// Options for the provider request context diagnostics builder.
/// </summary>
public sealed record ProviderRequestContextStatsDiagnosticBuilderOptions
{
    public string? Model { get; init; }

    public string? Provider { get; init; }

    public required string Transport { get; init; }

    public string? InputPropertyName { get; init; }

    public DiagnosticArtifactManifest? PayloadArtifact { get; init; }

    public int RequestSequenceFallback { get; init; } = 1;
}
