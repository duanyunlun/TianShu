namespace TianShu.Configuration;

/// <summary>
/// 解析后的 TianShu 配置快照。
/// Resolved snapshot of the effective TianShu configuration.
/// </summary>
public sealed class ResolvedTianShuConfig
{
    public string ConfigFilePath { get; init; } = string.Empty;

    public string UserConfigPath { get; init; } = string.Empty;

    public string? ActiveProfile { get; init; }

    public string? Model { get; init; }

    public string? ModelProvider { get; init; }

    public string? ApprovalPolicy { get; init; }

    public string? SandboxMode { get; init; }

    public string? WebSearchMode { get; init; }

    public string? ServiceTier { get; init; }

    public bool? ModelReasoningEnabled { get; init; }

    public string? ModelReasoningEffort { get; init; }

    public string? ModelReasoningSummary { get; init; }

    public string? ModelVerbosity { get; init; }

    public long? ModelReasoningBudgetTokens { get; init; }

    public string? ProviderBaseUrl { get; init; }

    public string? ProviderEnvKey { get; init; }

    public string? ProviderWireApi { get; init; }

    public long? ProviderRequestMaxRetries { get; init; }

    public long? ProviderStreamMaxRetries { get; init; }

    public long? ProviderStreamIdleTimeoutMs { get; init; }

    public long? ProviderWebsocketConnectTimeoutMs { get; init; }

    public bool? ProviderSupportsWebsockets { get; init; }

    public string ProtocolAdapter { get; init; } = "openai-responses";

    /// <summary>
    /// 合并后的 Workspace Resolver 策略快照。
    /// Merged workspace resolver policy snapshot.
    /// </summary>
    public WorkspaceResolverEffectivePolicy WorkspaceResolverPolicy { get; init; } = WorkspaceResolverEffectivePolicy.Empty;

    /// <summary>
    /// 合并后的原生配置对象，用于后续模型级协议解析。
    /// Merged native configuration object used by later model-level protocol resolution.
    /// </summary>
    public Dictionary<string, object?> RawConfig { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<ResolvedTianShuConfigLayer> Layers { get; init; } = Array.Empty<ResolvedTianShuConfigLayer>();
}


