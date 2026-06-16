using TianShu.Contracts.Diagnostics;

namespace TianShu.Diagnostics;

/// <summary>
/// Provider 请求上下文统计构建选项。
/// Build options for provider request context statistics.
/// </summary>
public sealed record ProviderRequestContextStatsBuildOptions
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required int RequestSequence { get; init; }

    public string? Model { get; init; }

    public string? Provider { get; init; }

    public required string Transport { get; init; }

    public string? InputPropertyName { get; init; }

    public DiagnosticArtifactManifest? PayloadArtifact { get; init; }
}
